using System.Xml.Linq;
using DriversUpdator.Core.Machine;
using DriversUpdator.Core.Models;

namespace DriversUpdator.Core.Sources.Vendors;

/// <summary>
/// Catalogue public Lenovo (celui de Lenovo System Update / Thin Installer), par type de machine.
/// </summary>
public sealed class LenovoSource : IDriverSource
{
    private readonly HttpClient _http;
    private Task<PnpPatternIndex>? _index;

    public LenovoSource(HttpClient http) => _http = http;

    public string Name => "Lenovo";

    public bool Supports(DeviceInfo device)
        => SystemInfo.Current.Manufacturer.Contains("Lenovo", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<DriverCandidate>> FindAsync(DeviceInfo device, CancellationToken ct = default)
        => (await (_index ??= LoadAsync(ct))).Find(device);

    private async Task<PnpPatternIndex> LoadAsync(CancellationToken ct)
    {
        var sys = SystemInfo.Current;
        var os = sys.IsWindows11 ? "Win11" : "Win10";
        var catalogUrl = $"https://download.lenovo.com/catalog/{sys.LenovoMachineType}_{os}.xml";
        var catalog = XDocument.Parse(await _http.GetStringAsync(catalogUrl, ct));

        var descriptors = catalog.Descendants("package")
            .Select(p => (string?)p.Attribute("location") ?? p.Element("location")?.Value)
            .Where(u => !string.IsNullOrEmpty(u))
            .Distinct()
            .ToList();

        var index = new PnpPatternIndex();
        var gate = new SemaphoreSlim(8);
        var loaded = await Task.WhenAll(descriptors.Select(async url =>
        {
            await gate.WaitAsync(ct);
            try { return (url!, XDocument.Parse(await _http.GetStringAsync(url, ct))); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return (url!, (XDocument?)null); }
            finally { gate.Release(); }
        }));

        foreach (var (url, doc) in loaded)
        {
            var pkg = doc?.Root;
            if (pkg is null) continue;

            var file = pkg.Descendants("Installer").Descendants("Name").FirstOrDefault()?.Value.Trim();
            if (file is null) continue;
            var downloadUrl = url[..(url.LastIndexOf('/') + 1)] + file;
            var title = pkg.Element("Title")?.Elements("Desc").FirstOrDefault()?.Value.Trim() ?? (string?)pkg.Attribute("name") ?? file;
            var version = (string?)pkg.Attribute("version");
            DateTime? date = DateTime.TryParse(pkg.Element("ReleaseDate")?.Value, out var d) ? d : null;

            foreach (var pnp in pkg.Descendants("_PnPID").Select(e => e.Value))
            {
                index.Add(pnp, _ => new DriverCandidate
                {
                    SourceName = Name,
                    MatchedId = "",
                    Description = title,
                    Version = DriverVersion.TryParse(version),
                    Date = date,
                    DownloadUrl = downloadUrl,
                    InstallerArguments = "/VERYSILENT /NORESTART",
                });
            }
        }
        return index;
    }
}
