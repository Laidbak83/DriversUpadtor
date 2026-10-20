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

    /// <summary>
    /// Vrai pour le matériel réel d'un fabricant (PCI, USB, audio...). Faux pour les périphériques
    /// virtuels ou purement Windows (volumes, files d'impression, ACPI génériques) : inutile de les chercher.
    /// </summary>
    public bool IsHardware => !HasDriver || HardwareIds.Any(id =>
        (id.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase)
         || id.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)
         || id.StartsWith(@"HDAUDIO\", StringComparison.OrdinalIgnoreCase)
         || id.StartsWith(@"BTH", StringComparison.OrdinalIgnoreCase)
         || id.StartsWith(@"HID\VID_", StringComparison.OrdinalIgnoreCase)
         || id.StartsWith(@"ACPI\VEN_", StringComparison.OrdinalIgnoreCase))
        && !id.Contains("VEN_MSFT", StringComparison.OrdinalIgnoreCase)
        && !id.Contains("VEN_PNP", StringComparison.OrdinalIgnoreCase));

    /// <summary>Tous les identifiants dans l'ordre de priorité de Windows.</summary>
    public IEnumerable<string> AllIds => HardwareIds.Concat(CompatibleIds);
}
