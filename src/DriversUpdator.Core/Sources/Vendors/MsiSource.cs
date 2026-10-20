using System.Text.Json;
using System.Text.RegularExpressions;
using DriversUpdator.Core.Machine;
using DriversUpdator.Core.Models;

namespace DriversUpdator.Core.Sources.Vendors;

/// <summary>
/// Pilotes de la page support MSI du modèle exact (service web non documenté du site msi.com).
/// MSI ne liste pas d'identifiants matériels : on associe un pilote à un périphérique
/// par le fabricant (Intel, Realtek...) et le type (LAN, Audio...) présents dans son titre.
/// </summary>
public sealed partial class MsiSource : IDriverSource
{
    private readonly HttpClient _http;
    private Task<List<Package>>? _packages;

    public MsiSource(HttpClient http) => _http = http;

    public string Name => "MSI";

    private sealed record Package(string Title, string? Version, DateTime? Date, string Url);

    // Classe Windows → mots attendus dans le titre MSI.
    private static readonly Dictionary<string, string[]> ClassKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Net"] = new[] { "LAN", "Ethernet", "WLAN", "Wi-Fi", "WiFi", "Wireless" },
        ["MEDIA"] = new[] { "Audio" },
        ["SCSIAdapter"] = new[] { "RST", "Rapid Storage", "SATA", "RAID", "Optane" },
        ["HDC"] = new[] { "RST", "Rapid Storage", "SATA", "RAID" },
        ["Bluetooth"] = new[] { "Bluetooth" },
        ["System"] = new[] { "Chipset", "Management Engine", "ME Driver", "Serial IO" },
        ["USB"] = new[] { "USB" },
    };

    public bool Supports(DeviceInfo device)
        => SystemInfo.Current.Manufacturer.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase)
           && ClassKeywords.ContainsKey(device.ClassName);

    public async Task<IReadOnlyList<DriverCandidate>> FindAsync(DeviceInfo device, CancellationToken ct = default)
    {
        var packages = await (_packages ??= LoadAsync(ct));
        var vendor = VendorDirectory.Find(device)?.Name;
        if (vendor is null) return Array.Empty<DriverCandidate>();
        var keywords = ClassKeywords[device.ClassName];

        return packages
            .Where(p => p.Title.Contains(vendor.Split(' ')[0], StringComparison.OrdinalIgnoreCase)
                        && keywords.Any(k => p.Title.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .Select(p => new DriverCandidate
            {
                SourceName = Name,
                // Correspondance par nom, donc la moins précise : on la classe après les IDs exacts.
                MatchedId = device.AllIds.Last(),
                Description = p.Title,
                Version = DriverVersion.TryParse(p.Version),
                Date = p.Date,
                DownloadUrl = p.Url,
            })
            .ToList();
    }

    /// <summary>"Z370 GAMING PRO CARBON (MS-7B45)" → "Z370-GAMING-PRO-CARBON".</summary>
    public static string ProductSlug(string baseBoard)
    {
        var name = ParenRegex().Replace(baseBoard, "").Trim();
        return SpaceRegex().Replace(name, "-");
    }

    private async Task<List<Package>> LoadAsync(CancellationToken ct)
    {
        var slug = ProductSlug(SystemInfo.Current.BaseBoardProduct);
        var url = $"https://www.msi.com/api/v1/product/support/panel?product={Uri.EscapeDataString(slug)}&type=driver";
        using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, ct));

        var os = SystemInfo.Current.IsWindows11 ? "11" : "10";
        var result = new List<Package>();
        Collect(doc.RootElement, null, result);

        // Si la réponse est classée par système, on garde celui de la machine en priorité.
        var forOs = result.Where(p => p.Title.Contains("Win" + os) || !p.Title.Contains("Win")).ToList();
        return forOs.Count > 0 ? forOs : result;
    }

    /// <summary>
    /// Parcourt la réponse JSON sans supposer sa structure exacte : tout objet qui contient
    /// une URL de .zip/.exe et un titre est un pilote.
    /// </summary>
    private static void Collect(JsonElement e, string? group, List<Package> into)
    {
        if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var i in e.EnumerateArray()) Collect(i, group, into);
            return;
        }
        if (e.ValueKind != JsonValueKind.Object) return;

        string? title = null, version = null, date = null, url = null;
        foreach (var p in e.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.String)
            {
                var v = p.Value.GetString() ?? "";
                var n = p.Name.ToLowerInvariant();
                if (n.Contains("url") && (v.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || v.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))) url = v;
                else if (n.Contains("title") || n == "name") title = v;
                else if (n.Contains("version")) version = v;
                else if (n.Contains("release") || n.Contains("date")) date = v;
            }
            else
            {
                // Le nom de la propriété parente sert souvent de groupe (ex. "Win10 64").
                Collect(p.Value, p.Name, into);
            }
        }

        if (url is not null && title is not null)
        {
            var fullTitle = group is null ? title : $"{title} [{group}]";
            into.Add(new Package(fullTitle, version, DateTime.TryParse(date, out var d) ? d : null, url));
        }
    }

    [GeneratedRegex(@"\(.*?\)")]
    private static partial Regex ParenRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();
}
