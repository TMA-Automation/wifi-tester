using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using WifiTester.Core.Abstractions;
using WifiTester.Core.Alerts;
using WifiTester.Core.Config;
using WifiTester.Core.Diagnostics;
using WifiTester.Core.Models;
using WifiTester.Core.Monitoring;
using WifiTester.Core.Probing;
using WifiTester.Core.Storage;
using WifiTester.Core.Updates;
using WifiTester.Platform;

namespace WifiTester.App;

public partial class App : System.Windows.Application
{
    private TrayIcon? _tray;
    private Repository? _repo;
    private MonitoringService? _svc;
    private AlertService? _alerts;
    private DashboardWindow? _dashboard;
    private UpdateCheck? _update;
    private bool _locationAlertShown;
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(24) };
    private readonly CancellationTokenSource _cts = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.Ensure();
        Log.Init(AppPaths.Log);
        Log.Write($"[START] WifiTester v{AppInfo.Version}");

        var cfg = MonitorConfig.Load(AppPaths.Config);
        cfg.Save(AppPaths.Config);
        cfg.PingTargets = cfg.PingTargets
            .Select(t => t == "gateway" ? (GatewayFinder.Get() ?? "127.0.0.1") : t).ToList();

        _repo = new Repository(AppPaths.Database);
        var recording = new Recording(_repo, cfg.RetentionDays);

        _svc = new MonitoringService(cfg, new ManagedNativeWifiSource(),
            new PingNetworkProbe(), new HttpThroughputTester(cfg.ThroughputUrl), new SystemClock());
        recording.Attach(_svc);

        _alerts = new AlertService(cooldownSeconds: 120, new SystemClock());
        _svc.DefectDetected += (_, d) => _alerts.OnDefect(d);

        // Logowanie zmian AP (connect/disconnect/roam).
        _svc.WifiEventDetected += (_, ev) => Log.Write(ev.Type switch
        {
            WifiEventType.Connected => $"[AP] połączono z {ev.ToBssid}",
            WifiEventType.Disconnected => $"[AP] rozłączono z {ev.FromBssid}",
            WifiEventType.Roamed => $"[AP] zmiana AP: {ev.FromBssid} → {ev.ToBssid}",
            _ => $"[AP] {ev.Type}"
        });

        // Jednorazowy alert, gdy WLAN odmawia danych z powodu wyłączonej lokalizacji.
        _svc.WifiSampleCollected += (_, s) =>
        {
            if (s.State == WifiState.LocationDenied && !_locationAlertShown)
            {
                _locationAlertShown = true;
                Dispatcher.Invoke(() => _tray?.ShowAlert("Brak dostępu do WiFi",
                    "Włącz usługi lokalizacji w Windows (Prywatność → Lokalizacja), w tym dostęp aplikacji klasycznych.",
                    ToolTipIcon.Warning));
            }
        };

        _tray = new TrayIcon();
        _tray.OpenDashboardRequested += ShowDashboard;
        _tray.GenerateReportRequested += () => Reporting.GenerateAndOpen(_repo!);
        _tray.ExitRequested += () => Shutdown();
        _alerts.AlertRaised += (_, a) => Dispatcher.Invoke(() =>
            _tray!.ShowAlert(a.Title, a.Message,
                a.Severity == Core.Models.Severity.Critical ? ToolTipIcon.Error : ToolTipIcon.Warning));

        _ = _svc.RunAsync(_cts.Token);

        // Auto-aktualizacja: sprawdzenie przy starcie i raz na dobę.
        _updateTimer.Tick += (_, _) => _ = CheckForUpdateAsync();
        _updateTimer.Start();
        _ = CheckForUpdateAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        var result = await UpdateService.CheckAsync(AppInfo.Repo, AppInfo.Version, AppInfo.AssetName);
        if (result is null) return;          // brak sieci/repo — cicho
        _update = result;
        Dispatcher.Invoke(() =>
        {
            _dashboard?.ApplyUpdate(result);
            if (result.Available) PromptInstall();
        });
    }

    private void PromptInstall()
    {
        if (_update is not { Available: true }) return;
        var answer = System.Windows.MessageBox.Show(
            $"Dostępna jest nowsza wersja WifiTester: v{_update.Latest} (masz v{AppInfo.Version}).\n\nZainstalować teraz? Aplikacja pobierze aktualizację i uruchomi się ponownie.",
            "Aktualizacja WifiTester", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes) _ = InstallUpdateAsync();
    }

    private async Task InstallUpdateAsync()
    {
        if (_update?.AssetUrl is not { } url)
        {
            // Brak assetu — otwórz stronę release jako fallback.
            if (_update?.ReleaseUrl is { } rel)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(rel) { UseShellExecute = true });
            return;
        }
        try
        {
            _tray?.ShowAlert("Aktualizacja", "Pobieranie nowej wersji…", ToolTipIcon.Info);
            await SelfUpdate.DownloadAndApplyAsync(url);
            Shutdown();   // proces pomocniczy podmieni plik i uruchomi nową wersję
        }
        catch (Exception ex)
        {
            Log.Write($"[UPDATE] błąd: {ex.Message}");
            _tray?.ShowAlert("Aktualizacja nieudana", "Nie udało się pobrać aktualizacji.", ToolTipIcon.Error);
        }
    }

    private void ShowDashboard()
    {
        Dispatcher.Invoke(() =>
        {
            if (_dashboard is null)
            {
                _dashboard = new DashboardWindow(_svc!);
                _dashboard.SettingsRequested += OpenSettings;
                _dashboard.UpdateRequested += PromptInstall;
                _dashboard.ApplyUpdate(_update);
                _dashboard.Closed += (_, _) => _dashboard = null;
            }
            _dashboard.Show();
            _dashboard.Activate();
        });
    }

    private void OpenSettings()
    {
        var win = new SettingsWindow(AppPaths.Config) { Owner = _dashboard };
        if (win.ShowDialog() == true)
            System.Windows.MessageBox.Show("Zapisano. Zmiany zaczną działać po ponownym uruchomieniu aplikacji.",
                "Ustawienia", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _cts.Cancel();
        _tray?.Dispose();
        _repo?.Dispose();
        base.OnExit(e);
    }
}
