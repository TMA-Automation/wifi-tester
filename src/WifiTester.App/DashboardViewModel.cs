using System.Collections.ObjectModel;
using System.ComponentModel;
using WifiTester.Core.Dashboard;
using WifiTester.Core.Models;
using WifiTester.Core.Monitoring;
using WifiTester.Core.Updates;

namespace WifiTester.App;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private readonly DashboardState _state = new(recentDefectLimit: 100);

    public ObservableCollection<string> Defects { get; } = new();
    public ObservableCollection<string> Latencies { get; } = new();

    public string Status { get; private set; } = "—";
    public string Ap { get; private set; } = "—";
    public string Signal { get; private set; } = "—";
    public string LinkRate { get; private set; } = "—";
    public int DefectCount { get; private set; }

    public string Version => "v" + AppInfo.Version;

    public bool UpdateAvailable { get; private set; }
    public string UpdateText { get; private set; } = "";

    public void SetUpdate(UpdateCheck? u)
    {
        UpdateAvailable = u is { Available: true };
        UpdateText = UpdateAvailable
            ? $"⬆  Dostępna aktualizacja: v{u!.Latest} (aktualna: {Version})"
            : "";
        Raise(nameof(UpdateAvailable)); Raise(nameof(UpdateText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DashboardViewModel(MonitoringService svc)
    {
        svc.WifiSampleCollected += (_, s) => OnUi(() => { _state.OnWifiSample(s); RefreshWifi(); });
        svc.LatencyCollected += (_, l) => OnUi(() => { _state.OnLatency(l); RefreshLatency(); });
        svc.DefectDetected += (_, d) => OnUi(() =>
        {
            _state.OnDefect(d);
            Defects.Insert(0, $"{d.Start:HH:mm:ss} [{d.Severity}] {d.Type}: {d.Description}");
            while (Defects.Count > 100) Defects.RemoveAt(Defects.Count - 1);
            DefectCount = _state.TotalDefects; Raise(nameof(DefectCount));
        });
    }

    private void RefreshWifi()
    {
        var w = _state.LatestWifi;
        Status = w?.State switch
        {
            null => "—",
            WifiState.Connected => "Połączono",
            WifiState.Disconnected => "Rozłączono",
            WifiState.NoAdapter => "Brak karty WiFi",
            WifiState.LocationDenied => "Brak dostępu — włącz usługi lokalizacji w Windows",
            _ => w.State.ToString()
        };
        Ap = w is { State: WifiState.Connected } ? $"{w.Ssid} / {w.Bssid} (kan. {w.Channel}, {w.Band})" : "—";
        Signal = w is { State: WifiState.Connected } ? $"{w.RssiDbm} dBm ({w.SignalQuality}%)" : "—";
        LinkRate = w is { State: WifiState.Connected } ? $"tx {w.TxRateMbps} / rx {w.RxRateMbps} Mbps" : "—";
        Raise(nameof(Status)); Raise(nameof(Ap)); Raise(nameof(Signal)); Raise(nameof(LinkRate));
    }

    private void RefreshLatency()
    {
        Latencies.Clear();
        foreach (var kv in _state.LatestLatencyByTarget)
            Latencies.Add(kv.Value.Success ? $"{kv.Key}: {kv.Value.RttMs:F0} ms" : $"{kv.Key}: brak odp.");
    }

    private static void OnUi(Action a) => System.Windows.Application.Current.Dispatcher.Invoke(a);
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
