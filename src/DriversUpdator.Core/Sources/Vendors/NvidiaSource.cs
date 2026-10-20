using System.Text.Json;
using DriversUpdator.Core.Machine;
using DriversUpdator.Core.Models;

namespace DriversUpdator.Core.Sources.Vendors;

/// <summary>
/// Dernier pilote GeForce/RTX officiel, via le service web utilisé par GeForce Experience / l'app NVIDIA.
/// </summary>
public sealed class NvidiaSource : IDriverSource
{
    private const string Endpoint =
        "https://gfwsl.geforce.com/nvidia_web_services/controller.gfeclientcontent.NG.php/com.nvidia.services.GFEClientContent_NG.getDispDrvrByDevid/";

    private readonly HttpClient _http;
    public NvidiaSource(HttpClient http) => _http = http;

    public string Name => "NVIDIA";

    public bool Supports(DeviceInfo device)
        => device.ClassName.Equals("Display", StringComparison.OrdinalIgnoreCase)
           && device.HardwareIds.Any(id => HardwareId.Vendor(id) == "10DE");

    public async Task<IReadOnlyList<DriverCandidate>> FindAsync(DeviceInfo device, CancellationToken ct = default)
    {
        var pair = HardwareId.PciPair(device.HardwareIds);
        if (pair is null) return Array.Empty<DriverCandidate>();

        var os = SystemInfo.Current;
        var query = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["dIDa"] = new[] { $"{pair.Value.Dev}_{pair.Value.Ven}" },
            ["osC"] = "10.0",
            ["osB"] = os.OsBuild.ToString(),
            ["is6"] = "1",
            ["lg"] = "1033",
            ["iLp"] = "0",
            ["prvMd"] = "0",
            ["gcV"] = "3.28.0.417",
            ["gIsB"] = "0",
            ["dch"] = "1",
            ["upCRD"] = "0",
            ["isCRD"] = "0",
        });

        var json = await _http.GetStringAsync(Endpoint + Uri.EscapeDataString(query), ct);
        using var doc = JsonDocument.Parse(json);

        var version = FindString(doc.RootElement, "Version");
        var url = FindString(doc.RootElement, "DownloadURLAdmin") ?? FindString(doc.RootElement, "DownloadURL");
        if (version is null || url is null) return Array.Empty<DriverCandidate>();

        DateTime? date = DateTime.TryParse(FindString(doc.RootElement, "ReleaseDateTime"), out var d) ? d : null;

        return new[]
        {
            new DriverCandidate
            {
                SourceName = Name,
                MatchedId = device.HardwareIds[0],
                Description = $"NVIDIA GeForce {version}",
                Version = ToWindowsVersion(version, device.InstalledVersion),
                Date = date,
                DownloadUrl = url,
                InstallerArguments = "-s -noreboot",
            },
        };
    }

    /// <summary>
    /// NVIDIA publie "566.36" alors que Windows affiche "32.0.15.6636" :
    /// les 5 derniers chiffres de la version Windows forment le numéro NVIDIA.
    /// </summary>
    private static DriverVersion? ToWindowsVersion(string nvidia, DriverVersion? installed)
    {
        if (!int.TryParse(nvidia.Replace(".", ""), out var n)) return null;
        var major = installed?.Major ?? 32;
        var minor = installed?.Minor ?? 0;
        return new DriverVersion(major, minor, 10 + n / 10000, n % 10000);
    }

    /// <summary>Cherche récursivement une propriété texte : la structure de la réponse varie selon les versions du service.</summary>
    private static string? FindString(JsonElement e, string name)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    if (p.NameEquals(name) && p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString();
                    var r = FindString(p.Value, name);
                    if (r is not null) return r;
                }
                break;
            case JsonValueKind.Array:
                foreach (var i in e.EnumerateArray())
                {
                    var r = FindString(i, name);
                    if (r is not null) return r;
                }
                break;
        }
        return null;
    }
}
