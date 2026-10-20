using DriversUpdator.Core.Models;

namespace DriversUpdator.Core.Sources.Vendors;

/// <summary>
/// Pages de téléchargement officielles des fabricants, par identifiant fabricant PCI/USB.
/// Sert aux fabricants sans catalogue public exploitable (AMD, Intel, Realtek...).
/// </summary>
public static class VendorDirectory
{
    public sealed record Vendor(string Name, string DriversUrl);

    private static readonly Dictionary<string, Vendor> ById = new(StringComparer.OrdinalIgnoreCase)
    {
        ["10DE"] = new("NVIDIA", "https://www.nvidia.com/fr-fr/drivers/"),
        ["1002"] = new("AMD", "https://www.amd.com/fr/support/download/drivers.html"),
        ["1022"] = new("AMD", "https://www.amd.com/fr/support/download/drivers.html"),
        ["8086"] = new("Intel", "https://www.intel.fr/content/www/fr/fr/support/detect.html"),
        ["8087"] = new("Intel", "https://www.intel.fr/content/www/fr/fr/support/detect.html"),
        ["10EC"] = new("Realtek", "https://www.realtek.com/Download/Overview"),
        ["0BDA"] = new("Realtek", "https://www.realtek.com/Download/Overview"),
        ["14E4"] = new("Broadcom", "https://www.broadcom.com/support/download-search"),
        ["168C"] = new("Qualcomm Atheros", "https://www.qualcomm.com/support"),
        ["17CB"] = new("Qualcomm", "https://www.qualcomm.com/support"),
        ["0CF3"] = new("Qualcomm Atheros", "https://www.qualcomm.com/support"),
        ["14C3"] = new("MediaTek", "https://www.mediatek.com/"),
        ["0E8D"] = new("MediaTek", "https://www.mediatek.com/"),
        ["1B21"] = new("ASMedia", "https://www.asmedia.com.tw/"),
        ["1B4B"] = new("Marvell", "https://www.marvell.com/support/downloads.html"),
        ["1969"] = new("Qualcomm Atheros (Killer)", "https://www.intel.fr/content/www/fr/fr/products/details/wireless/gaming.html"),
        ["144D"] = new("Samsung", "https://semiconductor.samsung.com/consumer-storage/support/tools/"),
        ["15B7"] = new("Western Digital / SanDisk", "https://support-en.wd.com/"),
        ["1C5C"] = new("SK hynix", "https://ssd.skhynix.com/download/"),
        ["1987"] = new("Phison", "https://www.phison.com/"),
        ["06CB"] = new("Synaptics", "https://www.synaptics.com/"),
        ["04F3"] = new("Elan", "https://www.emc.com.tw/"),
        ["046D"] = new("Logitech", "https://support.logi.com/"),
        ["1532"] = new("Razer", "https://www.razer.com/synapse-4"),
        ["1038"] = new("SteelSeries", "https://steelseries.com/gg"),
        ["1B1C"] = new("Corsair", "https://www.corsair.com/fr/fr/s/downloads"),
        ["045E"] = new("Microsoft", "https://support.microsoft.com/"),
        ["1043"] = new("ASUS", "https://www.asus.com/fr/support/download-center/"),
        ["0B05"] = new("ASUS", "https://www.asus.com/fr/support/download-center/"),
        ["1462"] = new("MSI", "https://fr.msi.com/support/download"),
        ["1458"] = new("Gigabyte", "https://www.gigabyte.com/fr/Support"),
        ["1849"] = new("ASRock", "https://www.asrock.com/support/index.fr.asp"),
        ["1028"] = new("Dell", "https://www.dell.com/support/home/fr-fr"),
        ["103C"] = new("HP", "https://support.hp.com/fr-fr/drivers"),
        ["17AA"] = new("Lenovo", "https://pcsupport.lenovo.com/fr/fr/"),
        ["1025"] = new("Acer", "https://www.acer.com/fr-fr/support/drivers-and-manuals"),
        ["1D72"] = new("Xiaomi", "https://www.mi.com/fr/support/"),
    };

    public static Vendor? Find(DeviceInfo device)
    {
        foreach (var id in device.HardwareIds)
        {
            var v = HardwareId.Vendor(id);
            if (v is not null && ById.TryGetValue(v, out var vendor)) return vendor;
        }
        return null;
    }
}
