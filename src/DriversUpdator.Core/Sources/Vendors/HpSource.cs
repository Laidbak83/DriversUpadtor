using System.Xml.Linq;
using DriversUpdator.Core.Machine;
using DriversUpdator.Core.Models;

namespace DriversUpdator.Core.Sources.Vendors;

/// <summary>
/// Fichiers de référence publics HP (ceux de HP Image Assistant), par identifiant de carte mère.
/// </summary>
public sealed class HpSource : IDriverSource
{
    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private Task<PnpPatternIndex>? _index;

    public HpSource(HttpClient http, string cacheDir)
    {
        _http = http;
        _cacheDir = Path.Combine(cacheDir, "hp");
    }

    public string Name => "HP";

    public bool Supports(DeviceInfo device)
    {
        var m = SystemInfo.Current.Manufacturer;
        return m.Equals("HP", StringComparison.OrdinalIgnoreCase) || m.Contains("Hewlett", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<DriverCandidate>> FindAsync(DeviceInfo device, CancellationToken ct = default)
        => (await (_index ??= LoadAsync(ct))).Find(device);

    private async Task<PnpPatternIndex> LoadAsync(CancellationToken ct)
    {
        var sys = SystemInfo.Current;
        var platform = sys.BaseBoardProduct.ToUpperInvariant();
        var os = sys.IsWindows11 ? "11.0" : "10.0";
        var url = $"https://hpia.hpcloud.hp.com/ref/{platform}/{platform}_64_{os}.{sys.OsDisplayVersion}.cab";

        var cab = await CachedDownload.GetFileAsync(_http, url, _cacheDir, TimeSpan.FromDays(1), ct);
        var root = XDocument.Load(await CachedDownload.ExpandCabAsync(cab, ct)).Root!;

        // SoftPaqs : Id → informations de téléchargement.
        var softpaqs = root.Descendants("UpdateInfo")
            .Where(u => (u.Element("Category")?.Value ?? "").StartsWith("Driver", StringComparison.OrdinalIgnoreCase))
            .GroupBy(u => u.Element("Id")?.Value ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        DriverCandidate Make(XElement sp) => new()
        {
            SourceName = Name,
            MatchedId = "",
            Description = sp.Element("Name")?.Value.Trim() ?? "",
            Version = DriverVersion.TryParse(sp.Element("Version")?.Value),
            Date = DateTime.TryParse(sp.Element("ReleaseDate")?.Value, out var d) ? d : null,
            DownloadUrl = "https://" + (sp.Element("Url")?.Value ?? "").Replace("https://", "").Replace("http://", ""),
            InstallerArguments = "-s",
        };

        var index = new PnpPatternIndex();
        // Correspondance périphérique → SoftPaq : <Device><DeviceID>...</DeviceID><SoftPaqID>sp...</SoftPaqID></Device>
        foreach (var dev in root.Descendants().Where(e => e.Element("DeviceID") is not null))
        {
            var spId = dev.Elements().FirstOrDefault(e => e.Name.LocalName.StartsWith("SoftPaq", StringComparison.OrdinalIgnoreCase)
                                                          || e.Name.LocalName == "Id")?.Value;
            if (spId is null || !softpaqs.TryGetValue(spId, out var sp)) continue;
            index.Add(dev.Element("DeviceID")!.Value, _ => Make(sp));
        }
        return index;
    }
}
