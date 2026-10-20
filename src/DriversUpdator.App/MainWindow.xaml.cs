using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Data;
using DriversUpdator.Core.Install;
using DriversUpdator.Core.Machine;
using DriversUpdator.Core.Matching;
using DriversUpdator.Core.Models;
using DriversUpdator.Core.Scanning;
using DriversUpdator.Core.Sources;
using DriversUpdator.Core.Sources.Vendors;
using Microsoft.Win32;

namespace DriversUpdator.App;

public partial class MainWindow : Window
{
    private static readonly string AppDir = AppContext.BaseDirectory;
    private static readonly string CacheDir = Path.Combine(AppDir, "cache");

    private readonly ObservableCollection<DeviceRow> _rows = new();
    private readonly ICollectionView _view;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly UpdateCatalogSource _catalog;
    private readonly DriverInstaller _installer;
    private SdioPackSource _sdio;
    private readonly IProgress<string> _log;

    public MainWindow()
    {
        InitializeComponent();
        _log = new Progress<string>(msg => { LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n"); LogBox.ScrollToEnd(); });
        _catalog = new UpdateCatalogSource(_http);
        _installer = new DriverInstaller(CacheDir, _http, _catalog);
        _sdio = new SdioPackSource(Path.Combine(AppDir, "packs"), CacheDir);

        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DeviceRow.Category)));
        _view.SortDescriptions.Add(new SortDescription(nameof(DeviceRow.CategoryOrder), ListSortDirection.Ascending));
        _view.SortDescriptions.Add(new SortDescription(nameof(DeviceRow.Name), ListSortDirection.Ascending));
        _view.Filter = FilterRow;
        DeviceList.ItemsSource = _view;

        Loaded += async (_, _) =>
        {
            var sys = await Task.Run(() => SystemInfo.Current);
            PcText.Text = $"{sys.Manufacturer} · {sys.BaseBoardProduct} · Windows {(sys.IsWindows11 ? "11" : "10")} {sys.OsDisplayVersion} (build {sys.OsBuild})";
            if (await _sdio.LoadIndexAsync())
                _log.Report($"Base hors ligne SDIO chargée : {_sdio.EntryCount} identifiants matériels.");
        };
    }

    // ───────────── Analyse ─────────────

    private async void Analyze_Click(object sender, RoutedEventArgs e) => await RunBusy(async () =>
    {
        SetProgress("Inventaire du matériel…", 0);
        var devices = await DeviceScanner.ScanAsync();
        _rows.Clear();
        foreach (var d in devices) _rows.Add(new DeviceRow(d));
        EmptyState.Visibility = Visibility.Collapsed;

        // On ne cherche que pour le vrai matériel : volumes, files d'impression, ACPI génériques n'ont pas de pilote constructeur.
        var toCheck = _rows.Where(r => r.Device.IsHardware).ToList();
        _log.Report($"{devices.Count} périphériques détectés, {toCheck.Count} composants matériels à vérifier.");

        var sources = new List<IDriverSource>();
        if (_sdio.EntryCount > 0) sources.Add(_sdio);
        if (OnlineCheck.IsChecked == true)
        {
            sources.Add(_catalog);
            sources.Add(new NvidiaSource(_http));
            sources.Add(new DellSource(_http, CacheDir));
            sources.Add(new HpSource(_http, CacheDir));
            sources.Add(new LenovoSource(_http));
        }
        if (sources.Count == 0) { _log.Report("Aucune source : activez la recherche en ligne ou indexez des packs SDIO."); return; }
        var matcher = new DriverMatcher(sources, _log);

        using var gate = new SemaphoreSlim(4); // limite les requêtes simultanées
        int done = 0;
        await Task.WhenAll(toCheck.Select(async row =>
        {
            await gate.WaitAsync();
            try
            {
                var r = await matcher.CheckAsync(row.Device);
                row.Best = r.IsUpdate ? r.Best : null;
                row.State = r.IsUpdate ? (row.Device.HasDriver ? RowState.Update : RowState.Missing)
                          : row.Device.HasDriver ? (r.Best is null ? RowState.NotFound : RowState.UpToDate)
                          : RowState.Missing;
                row.Selected = r.IsUpdate;
            }
            finally
            {
                gate.Release();
                var n = Interlocked.Increment(ref done);
                SetProgress($"Vérification {n}/{toCheck.Count}…", 100.0 * n / toCheck.Count);
            }
        }));

        _log.Report($"Résultats par source → {matcher.HitSummary()}");
        RefreshSummary();
        SetProgress(null, null);
    });

    private void RefreshSummary()
    {
        var hw = _rows.Where(r => r.Device.IsHardware).ToList();
        UpdatesCount.Text = hw.Count(r => r.State == RowState.Update).ToString();
        MissingCount.Text = hw.Count(r => r.State == RowState.Missing).ToString();
        UpToDateCount.Text = hw.Count(r => r.State is RowState.UpToDate or RowState.NotFound or RowState.Installed).ToString();
        HardwareCount.Text = hw.Count.ToString();
        _view.Refresh();
        Selection_Changed(this, new RoutedEventArgs());
    }

    // ───────────── Installation ─────────────

    private async void Install_Click(object sender, RoutedEventArgs e) => await RunBusy(async () =>
    {
        var selected = _rows.Where(r => r.Selected && r.Best is not null).ToList();
        if (selected.Count == 0) return;

        var list = string.Join("\n", selected.Select(r => $"• {r.Name}  →  {r.Best!.Version}"));
        if (MessageBox.Show(this, $"Installer {selected.Count} pilote(s) ?\n\n{list}\n\nUn point de restauration sera créé avant.",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        SetProgress("Création du point de restauration…", 0);
        var ok = await Task.Run(() => DriverInstaller.CreateRestorePoint("DriversUpdator - avant mise à jour des pilotes"));
        _log.Report(ok ? "Point de restauration créé." : "Impossible de créer le point de restauration (protection système désactivée ?).");
        if (!ok && MessageBox.Show(this, "Le point de restauration n'a pas pu être créé.\nContinuer quand même ?", "Attention",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        int i = 0;
        bool needsReboot = false;
        foreach (var row in selected)
        {
            SetProgress($"Installation {++i}/{selected.Count} : {row.Name}", 100.0 * (i - 1) / selected.Count);
            row.State = RowState.Installing;
            _log.Report($"→ {row.Name} ({row.Best!.SourceName} {row.Best.Version})");
            try
            {
                var (success, output) = await _installer.InstallAsync(row.Best, _log);
                row.State = success ? RowState.Installed : RowState.Failed;
                needsReboot |= output.Contains("3010") || output.Contains("redémarr", StringComparison.OrdinalIgnoreCase);
                _log.Report(output.Trim());
            }
            catch (Exception ex)
            {
                row.State = RowState.Failed;
                _log.Report("Échec : " + ex.Message);
            }
            row.Selected = false;
        }
        SetProgress(null, null);
        RefreshSummary();

        var failed = selected.Count(r => r.State == RowState.Failed);
        MessageBox.Show(this,
            $"{selected.Count - failed} pilote(s) installé(s)" + (failed > 0 ? $", {failed} échec(s) (voir le journal)." : ".")
            + (needsReboot ? "\n\nRedémarrez le PC pour terminer l'installation." : ""),
            "Installation terminée", MessageBoxButton.OK, failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    });

    private void Selection_Changed(object sender, RoutedEventArgs e)
    {
        var n = _rows.Count(r => r.Selected);
        InstallButton.IsEnabled = n > 0;
        InstallButtonText.Text = n > 0 ? $"Installer ({n})" : "Installer la sélection";
    }

    // ───────────── Outils ─────────────

    private async void Index_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Dossier contenant les packs SDIO (.7z)" };
        if (dlg.ShowDialog(this) != true) return;
        await RunBusy(async () =>
        {
            SetProgress("Indexation des packs SDIO…", null);
            _sdio = new SdioPackSource(dlg.FolderName, CacheDir);
            await _sdio.BuildIndexAsync(_log);
            SetProgress(null, null);
        });
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Dossier de sauvegarde des pilotes" };
        if (dlg.ShowDialog(this) != true) return;
        await RunBusy(async () =>
        {
            var target = Path.Combine(dlg.FolderName, $"Pilotes_{DateTime.Now:yyyyMMdd_HHmm}");
            SetProgress("Sauvegarde des pilotes…", null);
            var (code, _) = await DriverInstaller.BackupAllAsync(target);
            SetProgress(null, null);
            _log.Report(code == 0 ? $"Pilotes sauvegardés dans {target}" : $"pnputil a renvoyé le code {code}.");
        });
    }

    private void OpenCache_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(CacheDir);
        Process.Start(new ProcessStartInfo(CacheDir) { UseShellExecute = true });
    }

    private void Vendor_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not DeviceRow row) return;
        var vendor = VendorDirectory.Find(row.Device);
        if (vendor is null) { _log.Report($"Fabricant inconnu pour {row.Name}."); return; }
        OpenUrl(vendor.DriversUrl);
    }

    private void Web_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not DeviceRow row) return;
        OpenUrl("https://duckduckgo.com/?q=" + Uri.EscapeDataString($"site:touslesdrivers.com {row.Name}"));
    }

    private void CopyId_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is DeviceRow row) Clipboard.SetText(row.HardwareId);
    }

    // ───────────── Affichage ─────────────

    private bool FilterRow(object o)
    {
        if (o is not DeviceRow r) return false;
        return FilterBox.SelectedIndex switch
        {
            0 => r.State is RowState.Update or RowState.Missing or RowState.Installing or RowState.Installed or RowState.Failed,
            1 => r.Device.IsHardware,
            _ => true,
        };
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => _view?.Refresh();

    private void SetProgress(string? text, double? percent)
    {
        ProgressText.Text = text ?? "";
        Progress.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        Progress.IsIndeterminate = percent is null;
        if (percent is not null) Progress.Value = percent.Value;
    }

    private async Task RunBusy(Func<Task> action)
    {
        AnalyzeButton.IsEnabled = InstallButton.IsEnabled = false;
        try { await action(); }
        catch (Exception ex)
        {
            _log.Report("Erreur : " + ex.Message);
            MessageBox.Show(this, ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetProgress(null, null);
            AnalyzeButton.IsEnabled = true;
            Selection_Changed(this, new RoutedEventArgs());
        }
    }

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
