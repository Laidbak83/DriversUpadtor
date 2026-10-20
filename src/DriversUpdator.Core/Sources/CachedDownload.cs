using System.Diagnostics;

namespace DriversUpdator.Core.Sources;

/// <summary>Téléchargement avec cache disque (les catalogues constructeurs font plusieurs Mo).</summary>
internal static class CachedDownload
{
    public static async Task<string> GetFileAsync(HttpClient http, string url, string cacheDir, TimeSpan maxAge, CancellationToken ct)
    {
        Directory.CreateDirectory(cacheDir);
        var file = Path.Combine(cacheDir, Path.GetFileName(new Uri(url).LocalPath));
        if (File.Exists(file) && DateTime.Now - File.GetLastWriteTime(file) < maxAge) return file;

        var tmp = file + ".part";
        await using (var src = await http.GetStreamAsync(url, ct))
        await using (var dst = File.Create(tmp))
            await src.CopyToAsync(dst, ct);
        File.Move(tmp, file, overwrite: true);
        return file;
    }

    /// <summary>Décompresse un .cab contenant un seul fichier XML et renvoie son chemin.</summary>
    public static async Task<string> ExpandCabAsync(string cab, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetDirectoryName(cab)!, Path.GetFileNameWithoutExtension(cab));
        Directory.CreateDirectory(dir);
        var existing = Directory.GetFiles(dir, "*.xml").FirstOrDefault();
        if (existing is not null && File.GetLastWriteTime(existing) >= File.GetLastWriteTime(cab)) return existing;

        var psi = new ProcessStartInfo("expand.exe", $"-F:* \"{cab}\" \"{dir}\"") { CreateNoWindow = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        await p.WaitForExitAsync(ct);
        var xml = Directory.GetFiles(dir, "*.xml").FirstOrDefault()
                  ?? throw new InvalidOperationException($"Aucun XML dans {cab}");
        File.SetLastWriteTime(xml, DateTime.Now);
        return xml;
    }
}
