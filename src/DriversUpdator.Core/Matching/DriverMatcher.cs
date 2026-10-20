using DriversUpdator.Core.Models;
using DriversUpdator.Core.Sources;

namespace DriversUpdator.Core.Matching;

public sealed record UpdateResult(DeviceInfo Device, DriverCandidate? Best, bool IsUpdate);

/// <summary>Interroge toutes les sources et garde le meilleur pilote pour chaque périphérique.</summary>
public sealed class DriverMatcher
{
    private readonly IReadOnlyList<IDriverSource> _sources;
    private readonly IProgress<string>? _log;
    private readonly HashSet<string> _reportedFailures = new();

    public DriverMatcher(IEnumerable<IDriverSource> sources, IProgress<string>? log = null)
    {
        _sources = sources.ToList();
        _log = log;
    }

    public async Task<UpdateResult> CheckAsync(DeviceInfo device, CancellationToken ct = default)
    {
        var all = new List<DriverCandidate>();
        foreach (var source in _sources.Where(s => s.Supports(device)))
        {
            try { all.AddRange(await source.FindAsync(device, ct)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Une source en panne ne doit pas bloquer les autres ; on la signale une seule fois.
                lock (_reportedFailures)
                {
                    if (_reportedFailures.Add(source.Name)) _log?.Report($"Source {source.Name} en erreur : {ex.Message}");
                }
            }
        }

        var ids = device.AllIds.ToList();
        foreach (var c in all)
        {
            var i = ids.FindIndex(x => x.Equals(c.MatchedId, StringComparison.OrdinalIgnoreCase));
            c.MatchRank = i < 0 ? int.MaxValue : i;
        }

        // Comme Windows : d'abord l'ID le plus précis, puis le pilote le plus récent.
        var best = all
            .OrderBy(c => c.MatchRank)
            .ThenByDescending(c => c.Date ?? DateTime.MinValue)
            .ThenByDescending(c => c.Version)
            .FirstOrDefault();

        return new UpdateResult(device, best, IsNewer(best, device));
    }

    private static bool IsNewer(DriverCandidate? c, DeviceInfo d)
    {
        if (c is null) return false;
        if (!d.HasDriver) return true;
        // Les pilotes intégrés à Windows portent tous la date fictive du 21/06/2006 :
        // la date n'est pas fiable, on ne compare que les versions.
        return c.Version is not null && d.InstalledVersion is not null && c.Version > d.InstalledVersion;
    }
}
