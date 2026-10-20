using System.Xml.Linq;
using DriversUpdator.Core.Machine;
using DriversUpdator.Core.Models;

namespace DriversUpdator.Core.Sources.Vendors;

/// <summary>
/// Catalogue public Dell (CatalogPC.cab, celui de Dell Command | Update) :
/// pilotes filtrés par modèle de PC (System SKU) et par identifiant PCI.
/// </summary>
public sealed class DellSource : IDriverSource
{
    private const string CatalogUrl = "https://downloads.dell.com/catalog/CatalogPC.cab";

    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private Task<Dictionary<string, List<DriverCandidate>>>? _index;

    public DellSource(HttpClient http, string cacheDir)
    {
        _http = http;
        _cacheDir = Path.Combine(cacheDir, "dell");
    }

    public string Name => "Dell";

    public bool Supports(DeviceInfo device)
        => SystemInfo.Current.Manufacturer.Contains("Dell", StringComparison.OrdinalIgnoreCase)
           && HardwareId.PciPair(device.HardwareIds) is not null;

    public async Task<IReadOnlyList<DriverCandidate>> FindAsync(DeviceInfo device, CancellationToken ct = default)
    {
        var index = await (_index ??= LoadAsync(ct));
        var pair = HardwareId.PciPair(device.HardwareIds)!.Value;
        return index.TryGetValue($"{pair.Ven}&{pair.Dev}", out var list) ? list : Array.Empty<DriverCandidate>();
    }

    private async Task<Dictionary<string, List<DriverCandidate>>> LoadAsync(CancellationToken ct)
    {
        var cab = await CachedDownload.GetFileAsync(_http, CatalogUrl, _cacheDir, TimeSpan.FromDays(1), ct);
        var xml = await CachedDownload.ExpandCabAsync(cab, ct);
        var root = XDocument.Load(xml).Root!;
        var baseUrl = "https://" + ((string?)root.Attribute("baseLocation") ?? "downloads.dell.com").TrimEnd('/') + "/";
        var sku = SystemInfo.Current.SystemSku;

        var index = new Dictionary<string, List<DriverCandidate>>(StringComparer.OrdinalIgnoreCase);
        foreach (var comp in root.Elements().Where(e => e.Name.LocalName == "SoftwareComponent"))
        {
            var type = comp.Elements().FirstOrDefault(e => e.Name.LocalName == "ComponentType")?.Attribute("value")?.Value;
            if (type != "DRVR") continue;

            // Le pilote doit viser ce modèle de PC.
            var systems = comp.Descendants().Where(e => e.Name.LocalName == "Model")
                .Select(m => (string?)m.Attribute("systemID"));
            if (!systems.Any(s => string.Equals(s, sku, StringComparison.OrdinalIgnoreCase))) continue;

            var path = (string?)comp.Attribute("path");
            if (path is null || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

            var name = comp.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value.Trim() ?? path;
            DateTime? date = DateTime.TryParse((string?)comp.Attribute("dateTime"), out var d) ? d : null;
            var version = (string?)comp.Attribute("vendorVersion");

            foreach (var pci in comp.Descendants().Where(e => e.Name.LocalName == "PCIInfo"))
            {
                var key = $"{(string?)pci.Attribute("vendorID")}&{(string?)pci.Attribute("deviceID")}";
                if (!index.TryGetValue(key, out var list)) index[key] = list = new();
                list.Add(new DriverCandidate
                {
                    SourceName = Name,
                    MatchedId = $"PCI\\VEN_{pci.Attribute("vendorID")?.Value}&DEV_{pci.Attribute("deviceID")?.Value}".ToUpperInvariant(),
                    Description = name,
                    Version = DriverVersion.TryParse(version),
                    Date = date,
                    DownloadUrl = baseUrl + path,
                    InstallerArguments = "/s",
                });
            }
        }
        return index;
    }
}
