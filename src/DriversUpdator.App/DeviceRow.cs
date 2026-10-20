using System.ComponentModel;
using System.Runtime.CompilerServices;
using DriversUpdator.Core.Models;

namespace DriversUpdator.App;

/// <summary>Ligne affichée dans la grille.</summary>
public sealed class DeviceRow : INotifyPropertyChanged
{
    public DeviceRow(DeviceInfo device) => Device = device;

    public DeviceInfo Device { get; }
    public string Name => Device.Name;
    public string ClassName => Device.ClassName;
    public string HardwareId => Device.HardwareIds.FirstOrDefault() ?? "";
    public string Installed => Device.HasDriver
        ? $"{Device.InstalledVersion} ({Device.InstalledDate:dd/MM/yyyy})"
        : "Aucun pilote";

    private DriverCandidate? _best;
    public DriverCandidate? Best
    {
        get => _best;
        set { _best = value; OnChanged(); OnChanged(nameof(Available)); OnChanged(nameof(Source)); }
    }

    public string Available => Best is null ? "" : $"{Best.Version} ({Best.Date:dd/MM/yyyy})";
    public string Source => Best?.SourceName ?? "";

    private string _status = "";
    public string Status { get => _status; set { _status = value; OnChanged(); } }

    private bool _selected;
    public bool Selected { get => _selected; set { _selected = value; OnChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
