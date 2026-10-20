using DriversUpdator.Core.Models;

namespace DriversUpdator.Core.Sources;

/// <summary>
/// Une source de pilotes (packs SDIO, Microsoft Update Catalog, sites fabricants...).
/// Pour ajouter un fabricant, il suffit d'implémenter cette interface.
/// </summary>
public interface IDriverSource
{
    string Name { get; }

    /// <summary>Indique si la source sait traiter ce périphérique (ex. : seulement VEN_10DE pour NVIDIA).</summary>
    bool Supports(DeviceInfo device) => true;

    Task<IReadOnlyList<DriverCandidate>> FindAsync(DeviceInfo device, CancellationToken ct = default);
}
