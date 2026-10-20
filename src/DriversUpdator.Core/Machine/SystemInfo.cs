using System.Management;
using Microsoft.Win32;

namespace DriversUpdator.Core.Machine;

/// <summary>Identité de la machine, utilisée par les catalogues constructeurs (Dell, HP, Lenovo).</summary>
public sealed record SystemInfo(
    string Manufacturer,
    string Model,
    string SystemSku,
    string BaseBoardProduct,
    int OsBuild,
    string OsDisplayVersion)
{
    public bool IsWindows11 => OsBuild >= 22000;

    /// <summary>Type de machine Lenovo (4 premiers caractères du modèle, ex. 20XW).</summary>
    public string LenovoMachineType => Model.Length >= 4 ? Model[..4].ToUpperInvariant() : Model;

    private static readonly Lazy<SystemInfo> _current = new(Load);
    public static SystemInfo Current => _current.Value;

    private static SystemInfo Load()
    {
        string Query(string wql, string prop)
        {
            try
            {
                using var s = new ManagementObjectSearcher(wql);
                foreach (ManagementObject mo in s.Get())
                    using (mo) return (mo[prop]?.ToString() ?? "").Trim();
            }
            catch { /* WMI indisponible */ }
            return "";
        }

        using var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        int.TryParse(cv?.GetValue("CurrentBuildNumber") as string, out var build);

        return new SystemInfo(
            Query("SELECT Manufacturer FROM Win32_ComputerSystem", "Manufacturer"),
            Query("SELECT Model FROM Win32_ComputerSystem", "Model"),
            Query("SELECT SystemSKUNumber FROM Win32_ComputerSystem", "SystemSKUNumber"),
            Query("SELECT Product FROM Win32_BaseBoard", "Product"),
            build,
            cv?.GetValue("DisplayVersion") as string ?? "");
    }
}
