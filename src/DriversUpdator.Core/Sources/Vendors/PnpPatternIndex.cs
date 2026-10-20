using DriversUpdator.Core.Models;

namespace DriversUpdator.Core.Sources.Vendors;

/// <summary>
/// Index de pilotes par motif d'identifiant matériel. Les catalogues Lenovo et HP donnent souvent
/// des IDs partiels (ex. "PCI\VEN_8086&DEV_A0F0") : un ID du PC correspond s'il commence par le motif.
/// </summary>
internal sealed class PnpPatternIndex
{
    private readonly List<(string Pattern, DriverCandidate Candidate)> _items = new();

    public void Add(string pattern, Func<string, DriverCandidate> factory)
    {
        var p = pattern.Trim().TrimEnd('*').ToUpperInvariant();
        if (p.Length >= 8) _items.Add((p, factory(p)));
    }

    public IReadOnlyList<DriverCandidate> Find(DeviceInfo device)
    {
        var result = new List<DriverCandidate>();
        foreach (var id in device.HardwareIds)
        {
            foreach (var (pattern, candidate) in _items)
            {
                if (id.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    // MatchedId = ID réel du PC, pour que le classement par précision fonctionne.
                    result.Add(new DriverCandidate
                    {
                        SourceName = candidate.SourceName,
                        MatchedId = id,
                        Description = candidate.Description,
                        Version = candidate.Version,
                        Date = candidate.Date,
                        DownloadUrl = candidate.DownloadUrl,
                        InstallerArguments = candidate.InstallerArguments,
                    });
                }
            }
            if (result.Count > 0) break; // on garde l'ID le plus précis
        }
        return result;
    }
}
