using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using DriversUpdator.Core.Models;
using DriversUpdator.Core.Sources.Vendors;

namespace DriversUpdator.App;

public enum RowState { Unchecked, UpToDate, Update, Missing, NotFound, Installing, Installed, Failed }

/// <summary>Ligne affichée dans la liste.</summary>
public sealed class DeviceRow : INotifyPropertyChanged
{
    // Date fictive de tous les pilotes intégrés à Windows.
    private static readonly DateTime InboxDate = new(2006, 6, 21);

    public DeviceRow(DeviceInfo device)
    {
        Device = device;
        (Category, CategoryOrder, Icon) = Categorize(device);
        Vendor = VendorDirectory.Find(device)?.Name ?? device.Manufacturer;
        State = device.HasDriver ? RowState.Unchecked : RowState.Missing;
    }

    public DeviceInfo Device { get; }
    public string Name => Device.Name;
    public string Vendor { get; }
    public string Category { get; }
    public int CategoryOrder { get; }
    public string Icon { get; }
    public string HardwareId => Device.HardwareIds.FirstOrDefault() ?? "";

    public string Installed => !Device.HasDriver ? "Aucun pilote"
        : Device.InstalledDate?.Date == InboxDate ? $"{Device.InstalledVersion}  ·  Windows"
        : $"{Device.InstalledVersion}  ·  {Device.InstalledDate:dd/MM/yyyy}";

    private DriverCandidate? _best;
    public DriverCandidate? Best
    {
        get => _best;
        set { _best = value; OnChanged(); OnChanged(nameof(Available)); OnChanged(nameof(Details)); }
    }

    public string Available => Best is null ? "—"
        : $"{Best.Version?.ToString() ?? "?"}  ·  {Best.Date:dd/MM/yyyy}  ·  {Best.SourceName}";

    public string Details => $"{Device.Name}\n{HardwareId}\nFournisseur actuel : {Device.InstalledProvider}"
                             + (Best is null ? "" : $"\n\nProposé : {Best.Description}");

    private RowState _state;
    public RowState State
    {
        get => _state;
        set
        {
            _state = value;
            OnChanged(); OnChanged(nameof(StateText)); OnChanged(nameof(StateBrush)); OnChanged(nameof(CanInstall));
        }
    }

    public string StateText => State switch
    {
        RowState.UpToDate => "À jour",
        RowState.Update => "Mise à jour",
        RowState.Missing => "Pilote manquant",
        RowState.NotFound => "Rien de plus récent",
        RowState.Installing => "Installation…",
        RowState.Installed => "Installé",
        RowState.Failed => "Échec",
        _ => "Non vérifié",
    };

    public Brush StateBrush => State switch
    {
        RowState.UpToDate or RowState.Installed => Brushes.SeaGreen,
        RowState.Update => new SolidColorBrush(Color.FromRgb(0xCA, 0x50, 0x10)),
        RowState.Missing or RowState.Failed => new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C)),
        RowState.Installing => new SolidColorBrush(Color.FromRgb(0x00, 0x5F, 0xB8)),
        _ => Brushes.Gray,
    };

    public bool CanInstall => Best is not null && State is RowState.Update or RowState.Missing or RowState.Failed;

    private bool _selected;
    public bool Selected { get => _selected; set { _selected = value; OnChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Catégorie lisible, ordre d'affichage et icône (police Segoe Fluent Icons / MDL2).</summary>
    private static (string, int, string) Categorize(DeviceInfo d) => d.ClassName.ToLowerInvariant() switch
    {
        _ when !d.HasDriver => ("Pilotes manquants", 0, ""),
        "display" => ("Carte graphique", 1, ""),
        "net" => ("Réseau", 2, ""),
        "media" or "audioendpoint" or "audioprocessingobject" => ("Audio", 3, ""),
        "system" => ("Carte mère et chipset", 4, ""),
        "scsiadapter" or "hdc" or "diskdrive" or "storagevolume" => ("Stockage", 5, ""),
        "usb" or "usbdevice" => ("USB", 6, ""),
        "bluetooth" => ("Bluetooth", 7, ""),
        "hidclass" or "keyboard" or "mouse" => ("Clavier, souris et manettes", 8, ""),
        "monitor" => ("Écrans", 9, ""),
        "processor" => ("Processeur", 10, ""),
        "camera" or "image" => ("Caméras", 11, ""),
        "printer" or "printqueue" => ("Imprimantes", 12, ""),
        _ => ("Autres", 20, ""),
    };
}
