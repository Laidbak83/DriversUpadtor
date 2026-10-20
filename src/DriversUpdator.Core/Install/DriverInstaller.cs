using System.Diagnostics;
using System.Management;
using DriversUpdator.Core.Models;
using DriversUpdator.Core.Sources;

namespace DriversUpdator.Core.Install;

/// <summary>Installation, sauvegarde et point de restauration. Nécessite les droits administrateur.</summary>
public sealed class DriverInstaller
{
    private readonly string _downloadDir;
    private readonly HttpClient _http;
    private readonly UpdateCatalogSource _catalog;

    public DriverInstaller(string cacheDir, HttpClient http, UpdateCatalogSource catalog)
    {
        _downloadDir = Path.Combine(cacheDir, "downloads");
        _http = http;
        _catalog = catalog;
    }

    /// <summary>Crée un point de restauration système (type DEVICE_DRIVER_INSTALL).</summary>
    public static bool CreateRestorePoint(string description)
    {
        try
        {
            using var cls = new ManagementClass(new ManagementScope(@"\\.\root\default"), new ManagementPath("SystemRestore"), null);
            using var args = cls.GetMethodParameters("CreateRestorePoint");
            args["Description"] = description;
            args["RestorePointType"] = 10; // DEVICE_DRIVER_INSTALL
            args["EventType"] = 100;       // BEGIN_SYSTEM_CHANGE
            using var result = cls.InvokeMethod("CreateRestorePoint", args, null);
            return Convert.ToUInt32(result["ReturnValue"]) == 0;
        }
        catch
        {
            return false; // protection système désactivée, par exemple
        }
    }

    /// <summary>Exporte tous les pilotes tiers installés (pnputil /export-driver).</summary>
    public static Task<(int ExitCode, string Output)> BackupAllAsync(string targetDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(targetDir);
        return RunAsync("pnputil.exe", $"/export-driver * \"{targetDir}\"", ct);
    }

    public async Task<(bool Ok, string Log)> InstallAsync(DriverCandidate candidate, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        string infArgument;

        if (candidate.LocalInfPath is not null)
        {
            infArgument = $"\"{candidate.LocalInfPath}\"";
        }
        else if (candidate.DownloadUrl is not null && IsExecutable(candidate.DownloadUrl))
        {
            return await RunVendorInstallerAsync(candidate, progress, ct);
        }
        else if (candidate.DownloadUrl is not null)
        {
            var folder = await DownloadAndExtractAsync(candidate, progress, ct);
            infArgument = $"\"{Path.Combine(folder, "*.inf")}\" /subdirs";
        }
        else
        {
            return (false, "Aucun fichier à installer.");
        }

        progress?.Report("Installation via pnputil...");
        var (code, output) = await RunAsync("pnputil.exe", $"/add-driver {infArgument} /install", ct);
        // 0 = OK, 3010 = OK mais redémarrage nécessaire
        return (code is 0 or 3010, output);
    }

    private static bool IsExecutable(string url)
        => !url.StartsWith("catalog:", StringComparison.Ordinal)
           && Path.GetExtension(new Uri(url).LocalPath).Equals(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Installeur constructeur (.exe) : silencieux si on connaît les arguments, sinon assistant normal.</summary>
    private async Task<(bool, string)> RunVendorInstallerAsync(DriverCandidate c, IProgress<string>? progress, CancellationToken ct)
    {
        var file = await DownloadAsync(c.DownloadUrl!, progress, ct);
        progress?.Report(c.InstallerArguments is null
            ? "Lancement de l'installeur, suivez l'assistant..."
            : "Installation silencieuse en cours (cela peut prendre plusieurs minutes)...");

        var psi = new ProcessStartInfo(file, c.InstallerArguments ?? "") { UseShellExecute = c.InstallerArguments is null };
        using var p = Process.Start(psi)!;
        await p.WaitForExitAsync(ct);
        return (p.ExitCode is 0 or 3010 or 1641, $"{Path.GetFileName(file)} terminé (code {p.ExitCode}).");
    }

    private async Task<string> DownloadAsync(string url, IProgress<string>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(_downloadDir);
        var fileName = Path.GetFileName(new Uri(url).LocalPath);
        var file = Path.Combine(_downloadDir, fileName);
        progress?.Report($"Téléchargement de {fileName}...");
        await using (var src = await _http.GetStreamAsync(url, ct))
        await using (var dst = File.Create(file))
            await src.CopyToAsync(dst, ct);
        return file;
    }

    private async Task<string> DownloadAndExtractAsync(DriverCandidate c, IProgress<string>? progress, CancellationToken ct)
    {
        var url = c.DownloadUrl!;
        if (url.StartsWith("catalog:", StringComparison.Ordinal))
        {
            url = await _catalog.ResolveDownloadUrlAsync(url["catalog:".Length..], ct)
                  ?? throw new InvalidOperationException("Lien de téléchargement introuvable sur le catalogue.");
        }

        var file = await DownloadAsync(url, progress, ct);
        var fileName = Path.GetFileName(file);
        var target = Path.Combine(_downloadDir, Path.GetFileNameWithoutExtension(fileName));
        Directory.CreateDirectory(target);
        progress?.Report("Décompression...");
        if (file.EndsWith(".cab", StringComparison.OrdinalIgnoreCase))
        {
            var (code, output) = await RunAsync("expand.exe", $"-F:* \"{file}\" \"{target}\"", ct);
            if (code != 0) throw new InvalidOperationException("expand.exe a échoué : " + output);
        }
        else if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(file, target, overwriteFiles: true);
        }
        else
        {
            throw new NotSupportedException($"Format non géré pour l'instant : {fileName}");
        }
        return target;
    }

    private static async Task<(int, string)> RunAsync(string exe, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, await stdout + await stderr);
    }
}
