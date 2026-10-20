using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Data;
using DriversUpdator.Core.Install;
using DriversUpdator.Core.Machine;
using DriversUpdator.Core.Matching;
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
    private readonly HttpClient _http = new();
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
        DriverGrid.ItemsSource = _rows;
        Loaded += async (_, _) =>
        {
            var sys = await Task.Run(() => SystemInfo.Current);
            _log.Report($"PC : {sys.Manufacturer} {sys.Model} (SKU {sys.SystemSku}, carte mère {sys.BaseBoardProduct}), Windows build {sys.OsBuild} {sys.OsDisplayVersion}");
            if (await _sdio.LoadIndexAsync())
                _log.Report($"Index SDIO chargé : {_sdio.EntryCount} identifiants matériels.");
            else
                _log.Report("Aucun index SDIO. Placez les packs .7z dans le dossier 'packs' puis cliquez sur « Indexer ».");
        };
    }

    private async Task RunBusy(Func<Task> action)
    {
        IsEnabled = false;
        try { await action(); }
        catch (Exception ex) { _log.Report("Erreur : " + ex.Message); }
        finally { IsEnabled = true; }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await RunBusy(async () =>
    {
        _log.Report("Scan du matériel...");
        var devices = await DeviceScanner.ScanAsync();
        _rows.Clear();
        foreach (var d in devices) _rows.Add(new DeviceRow(d) { Status = d.HasDriver ? "" : "Pilote manquant" });
        _log.Report($"{devices.Count} périphériques détectés, {devices.Count(d => !d.HasDriver)} sans pilote.");
    });

    private async void Index_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Dossier contenant les packs SDIO (.7z)" };
        if (dlg.ShowDialog(this) != true) return;
        await RunBusy(async () =>
        {
            _sdio = new SdioPackSource(dlg.FolderName, CacheDir);
            await _sdio.BuildIndexAsync(_log);
        });
    }

    private async void Check_Click(object sender, RoutedEventArgs e) => await RunBusy(async () =>
    {
        if (_rows.Count == 0) { _log.Report("Lancez d'abord le scan."); return; }

        var sources = new List<IDriverSource> { _sdio };
        if (OnlineCheck.IsChecked == true)
        {
            sources.Add(_catalog);
            sources.Add(new NvidiaSource(_http));
            sources.Add(new DellSource(_http, CacheDir));
            sources.Add(new HpSource(_http, CacheDir));
            sources.Add(new LenovoSource(_http));
        }
        var matcher = new DriverMatcher(sources, _log);

        _log.Report("Recherche des mises à jour...");
        using var gate = new SemaphoreSlim(4); // limite les requêtes simultanées au catalogue
        int done = 0, updates = 0;
        var tasks = _rows.ToList().Select(async row =>
        {
            await gate.WaitAsync();
            try
            {
                var r = await matcher.CheckAsync(row.Device);
                row.Best = r.Best;
                row.Status = r.IsUpdate ? (row.Device.HasDriver ? "Mise à jour disponible" : "Pilote trouvé") : row.Status;
                row.Selected = r.IsUpdate;
                if (r.IsUpdate) Interlocked.Increment(ref updates);
            }
            finally
            {
                gate.Release();
                if (Interlocked.Increment(ref done) % 20 == 0) _log.Report($"{done}/{_rows.Count} vérifiés...");
            }
        });
        await Task.WhenAll(tasks);
        _log.Report($"Terminé : {updates} pilote(s) à installer ou mettre à jour.");
    });

    private async void Install_Click(object sender, RoutedEventArgs e) => await RunBusy(async () =>
    {
        var selected = _rows.Where(r => r.Selected && r.Best is not null).ToList();
        if (selected.Count == 0) { _log.Report("Rien de sélectionné."); return; }

        if (MessageBox.Show(this, $"Installer {selected.Count} pilote(s) ?\nUn point de restauration sera créé avant.",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _log.Report("Création du point de restauration...");
        var ok = await Task.Run(() => DriverInstaller.CreateRestorePoint("DriversUpdator - avant mise à jour des pilotes"));
        _log.Report(ok ? "Point de restauration créé." : "Impossible de créer le point de restauration (protection système désactivée ?).");
        if (!ok && MessageBox.Show(this, "Continuer sans point de restauration ?", "Attention",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        foreach (var row in selected)
        {
            row.Status = "Installation...";
            _log.Report($"→ {row.Name}");
            var (success, output) = await _installer.InstallAsync(row.Best!, _log);
            row.Status = success ? "Installé" : "Échec";
            row.Selected = false;
            _log.Report(output.Trim());
        }
        _log.Report("Installation terminée. Un redémarrage peut être nécessaire.");
    });

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Dossier de sauvegarde des pilotes" };
        if (dlg.ShowDialog(this) != true) return;
        await RunBusy(async () =>
        {
            var target = Path.Combine(dlg.FolderName, $"Pilotes_{DateTime.Now:yyyyMMdd_HHmm}");
            _log.Report($"Sauvegarde vers {target}...");
            var (code, _) = await DriverInstaller.BackupAllAsync(target);
            _log.Report(code == 0 ? "Sauvegarde terminée." : $"pnputil a renvoyé le code {code}.");
        });
    }

    private void Web_Click(object sender, RoutedEventArgs e)
    {
        if (DriverGrid.SelectedItem is not DeviceRow row) { _log.Report("Sélectionnez un périphérique dans la liste."); return; }
        var query = Uri.EscapeDataString($"site:touslesdrivers.com {row.Name}");
        Process.Start(new ProcessStartInfo($"https://duckduckgo.com/?q={query}") { UseShellExecute = true });
    }

    private void Vendor_Click(object sender, RoutedEventArgs e)
    {
        if (DriverGrid.SelectedItem is not DeviceRow row) { _log.Report("Sélectionnez un périphérique dans la liste."); return; }
        var vendor = VendorDirectory.Find(row.Device);
        if (vendor is null) { _log.Report("Fabricant inconnu pour ce périphérique."); return; }
        _log.Report($"Ouverture du site {vendor.Name}...");
        Process.Start(new ProcessStartInfo(vendor.DriversUrl) { UseShellExecute = true });
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        var view = CollectionViewSource.GetDefaultView(_rows);
        view.Filter = OnlyUpdatesCheck.IsChecked == true
            ? o => o is DeviceRow r && (!r.Device.HasDriver || r.Best is not null && r.Selected)
            : null;
    }
}
