using System.Management;
using DriversUpdator.Core.Models;

namespace DriversUpdator.Core.Scanning;

/// <summary>Inventaire du matériel via WMI.</summary>
public static class DeviceScanner
{
    public static Task<IReadOnlyList<DeviceInfo>> ScanAsync(CancellationToken ct = default)
        => Task.Run(() => Scan(ct), ct);

    public static IReadOnlyList<DeviceInfo> Scan(CancellationToken ct = default)
    {
        var drivers = LoadSignedDrivers();
        var devices = new List<DeviceInfo>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceID, Name, Manufacturer, PNPClass, HardwareID, CompatibleID, ConfigManagerErrorCode FROM Win32_PnPEntity");

        foreach (ManagementObject mo in searcher.Get())
        {
            ct.ThrowIfCancellationRequested();
            using (mo)
            {
                var id = mo["DeviceID"] as string;
                if (string.IsNullOrEmpty(id)) continue;

                var hwIds = (mo["HardwareID"] as string[]) ?? Array.Empty<string>();
                // Les périphériques sans identifiant matériel (logiciels, racine) ne nous intéressent pas.
                if (hwIds.Length == 0) continue;

                drivers.TryGetValue(id, out var drv);

                devices.Add(new DeviceInfo
                {
                    InstanceId = id,
                    Name = mo["Name"] as string ?? id,
                    Manufacturer = mo["Manufacturer"] as string ?? "",
                    ClassName = mo["PNPClass"] as string ?? "",
                    HardwareIds = hwIds.Select(h => h.ToUpperInvariant()).ToArray(),
                    CompatibleIds = ((mo["CompatibleID"] as string[]) ?? Array.Empty<string>())
                        .Select(h => h.ToUpperInvariant()).ToArray(),
                    ProblemCode = Convert.ToInt32(mo["ConfigManagerErrorCode"] ?? 0),
                    InstalledVersion = DriverVersion.TryParse(drv?.Version),
                    InstalledDate = drv?.Date,
                    InstalledInf = drv?.Inf,
                    InstalledProvider = drv?.Provider,
                });
            }
        }

        return devices.OrderBy(d => d.ClassName).ThenBy(d => d.Name).ToList();
    }

    private sealed record SignedDriver(string? Version, DateTime? Date, string? Inf, string? Provider);

    private static Dictionary<string, SignedDriver> LoadSignedDrivers()
    {
        var map = new Dictionary<string, SignedDriver>(StringComparer.OrdinalIgnoreCase);
        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceID, DriverVersion, DriverDate, InfName, DriverProviderName FROM Win32_PnPSignedDriver");

        foreach (ManagementObject mo in searcher.Get())
        {
            using (mo)
            {
                if (mo["DeviceID"] is not string id) continue;
                DateTime? date = null;
                if (mo["DriverDate"] is string cim && cim.Length >= 8)
                {
                    try { date = ManagementDateTimeConverter.ToDateTime(cim); } catch { /* date illisible */ }
                }
                map[id] = new SignedDriver(
                    mo["DriverVersion"] as string, date,
                    mo["InfName"] as string, mo["DriverProviderName"] as string);
            }
        }
        return map;
    }
}
