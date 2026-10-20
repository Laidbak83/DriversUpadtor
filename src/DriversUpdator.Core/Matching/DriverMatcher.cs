using DriversUpdator.Core.Models;
using DriversUpdator.Core.Sources;

namespace DriversUpdator.Core.Matching;

public sealed record UpdateResult(DeviceInfo Device, DriverCandidate? Best, bool IsUpdate);

/// <summary>Interroge toutes les sources et garde le meilleur pilote pour chaque périphérique.</summary>
public sealed class DriverMatcher
{
    private readonly IReadOnlyList<IDriverSource> _sources;

    public DriverMatcher(IEnumerable<IDriverSource> sources) => _sources = sources.ToList();

    public async Task<UpdateResult> CheckAsync(DeviceInfo device, CancellationToken ct = default)
    {
        var all = new List<DriverCandidate>();
        foreach (var source in _sources.Where(s => s.Supports(device)))
        {
            try { all.AddRange(await source.FindAsync(device, ct)); }
            catch (OperationCanceledException) { throw; }
            catch { /* une source en panne ne doit pas bloquer les autres */ }
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
        if (c.Version is not null && d.InstalledVersion is not null) return c.Version > d.InstalledVersion;
        return c.Date is not null && d.InstalledDate is not null && c.Date > d.InstalledDate;
    }
}
