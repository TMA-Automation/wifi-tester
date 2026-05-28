using System.Windows;
using System.Windows.Forms;
using WifiTester.Core.Abstractions;
using WifiTester.Core.Alerts;
using WifiTester.Core.Config;
using WifiTester.Core.Monitoring;
using WifiTester.Core.Probing;
using WifiTester.Core.Storage;
using WifiTester.Platform;

namespace WifiTester.App;

public partial class App : System.Windows.Application
{
    private TrayIcon? _tray;
    private Repository? _repo;
    private MonitoringService? _svc;
    private AlertService? _alerts;
    private DashboardWindow? _dashboard;
    private readonly CancellationTokenSource _cts = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.Ensure();

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

        _tray = new TrayIcon();
        _tray.OpenDashboardRequested += ShowDashboard;
        _tray.GenerateReportRequested += () => Reporting.GenerateAndOpen(_repo!);
        _tray.ExitRequested += () => Shutdown();
        _alerts.AlertRaised += (_, a) => Dispatcher.Invoke(() =>
            _tray!.ShowAlert(a.Title, a.Message,
                a.Severity == Core.Models.Severity.Critical ? ToolTipIcon.Error : ToolTipIcon.Warning));

        _ = _svc.RunAsync(_cts.Token);
    }

    private void ShowDashboard()
    {
        Dispatcher.Invoke(() =>
        {
            if (_dashboard is null)
            {
                _dashboard = new DashboardWindow(_svc!);
                _dashboard.Closed += (_, _) => _dashboard = null;
            }
            _dashboard.Show();
            _dashboard.Activate();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _cts.Cancel();
        _tray?.Dispose();
        _repo?.Dispose();
        base.OnExit(e);
    }
}
