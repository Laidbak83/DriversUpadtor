using System.Text.RegularExpressions;

namespace DriversUpdator.Core.Sources;

/// <summary>Petits utilitaires sur les identifiants matériels (PCI\VEN_xxxx&DEV_xxxx...).</summary>
public static partial class HardwareId
{
    public static string? Vendor(string id) => Get(id, VenRegex());
    public static string? Device(string id) => Get(id, DevRegex());

    /// <summary>Premier couple fabricant/périphérique trouvé parmi les IDs.</summary>
    public static (string Ven, string Dev)? PciPair(IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            var v = Vendor(id);
            var d = Device(id);
            if (v is not null && d is not null) return (v, d);
        }
        return null;
    }

    private static string? Get(string id, Regex r)
    {
        var m = r.Match(id);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }

    [GeneratedRegex(@"(?:VEN|VID)_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VenRegex();

    [GeneratedRegex(@"(?:DEV|PID)_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex DevRegex();
}
