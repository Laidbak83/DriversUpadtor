using System.Diagnostics;
using System.Text.Json;
using DriversUpdator.Core.Inf;
using DriversUpdator.Core.Models;

namespace DriversUpdator.Core.Sources;

/// <summary>
/// Source hors ligne basée sur les driverpacks de Snappy Driver Installer Origin
/// (archives .7z remplies de .inf). Les packs sont décompressés dans un dossier cache
/// puis indexés : identifiant matériel → .inf.
/// </summary>
public sealed class SdioPackSource : IDriverSource
{
    private readonly string _packsDir;
    private readonly string _extractDir;
    private readonly string _indexFile;
    private Dictionary<string, List<DriverIndexEntry>> _index = new(StringComparer.OrdinalIgnoreCase);

    public string Name => "SDIO";

    public SdioPackSource(string packsDir, string cacheDir)
    {
        _packsDir = packsDir;
        _extractDir = Path.Combine(cacheDir, "sdio");
        _indexFile = Path.Combine(cacheDir, "sdio-index.json");
    }

    public int EntryCount => _index.Count;

    /// <summary>Charge l'index déjà construit, s'il existe.</summary>
    public async Task<bool> LoadIndexAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_indexFile)) return false;
        await using var fs = File.OpenRead(_indexFile);
        var entries = await JsonSerializer.DeserializeAsync<List<DriverIndexEntry>>(fs, cancellationToken: ct) ?? new();
        SetIndex(entries);
        return true;
    }

    /// <summary>Décompresse les packs .7z (via 7-Zip) puis indexe tous les .inf trouvés.</summary>
    public async Task BuildIndexAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_extractDir);

        foreach (var pack in Directory.EnumerateFiles(_packsDir, "*.7z"))
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.Combine(_extractDir, Path.GetFileNameWithoutExtension(pack));
            if (Directory.Exists(target)) continue; // déjà décompressé
            progress?.Report($"Décompression de {Path.GetFileName(pack)}...");
            await ExtractAsync(pack, target, ct);
        }

        var entries = new List<DriverIndexEntry>();
        // On indexe aussi les .inf posés directement dans le dossier des packs.
        var infs = Directory.EnumerateFiles(_extractDir, "*.inf", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(_packsDir, "*.inf", SearchOption.AllDirectories));
        int count = 0;
        foreach (var inf in infs)
        {
            ct.ThrowIfCancellationRequested();
            try { entries.AddRange(InfParser.Parse(inf)); }
            catch (Exception ex) { progress?.Report($"INF ignoré ({ex.Message}) : {inf}"); }
            if (++count % 500 == 0) progress?.Report($"{count} fichiers .inf indexés...");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_indexFile)!);
        await using (var fs = File.Create(_indexFile))
            await JsonSerializer.SerializeAsync(fs, entries, cancellationToken: ct);

        SetIndex(entries);
        progress?.Report($"Index terminé : {count} .inf, {_index.Count} identifiants matériels.");
    }

    public Task<IReadOnlyList<DriverCandidate>> FindAsync(DeviceInfo device, CancellationToken ct = default)
    {
        var result = new List<DriverCandidate>();
        foreach (var id in device.AllIds)
        {
            if (!_index.TryGetValue(id, out var list)) continue;
            result.AddRange(list.Select(e => new DriverCandidate
            {
                SourceName = Name,
                MatchedId = id,
                Description = e.Description,
                Version = DriverVersion.TryParse(e.Version),
                Date = e.Date,
                LocalInfPath = e.InfPath,
            }));
        }
        return Task.FromResult<IReadOnlyList<DriverCandidate>>(result);
    }

    private void SetIndex(IEnumerable<DriverIndexEntry> entries)
    {
        _index = entries.GroupBy(e => e.HardwareId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    private static async Task ExtractAsync(string archive, string target, CancellationToken ct)
    {
        var sevenZip = Find7Zip() ?? throw new FileNotFoundException(
            "7-Zip est nécessaire pour décompresser les packs SDIO (https://www.7-zip.org).");
        var psi = new ProcessStartInfo(sevenZip, $"x \"{archive}\" -o\"{target}\" -y")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0) throw new InvalidOperationException($"7-Zip a échoué ({p.ExitCode}) sur {archive}");
    }

    private static string? Find7Zip()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "7z.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
