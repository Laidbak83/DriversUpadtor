namespace DriversUpdator.Core.Models;

/// <summary>Un périphérique détecté sur la machine.</summary>
public sealed class DeviceInfo
{
    public required string InstanceId { get; init; }
    public string Name { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string ClassName { get; init; } = "";

    /// <summary>Identifiants matériels, du plus précis au plus générique.</summary>
    public IReadOnlyList<string> HardwareIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CompatibleIds { get; init; } = Array.Empty<string>();

    public DriverVersion? InstalledVersion { get; init; }
    public DateTime? InstalledDate { get; init; }
    public string? InstalledInf { get; init; }
    public string? InstalledProvider { get; init; }

    /// <summary>Code d'erreur du gestionnaire de périphériques (0 = OK, 28 = pas de pilote).</summary>
    public int ProblemCode { get; init; }

    public bool HasDriver => InstalledVersion is not null && ProblemCode != 28;

    /// <summary>Tous les identifiants dans l'ordre de priorité de Windows.</summary>
    public IEnumerable<string> AllIds => HardwareIds.Concat(CompatibleIds);
}
