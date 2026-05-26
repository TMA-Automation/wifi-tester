using System.Linq;
using WifiTester.Core.Abstractions;
using WifiTester.Core.Config;
using WifiTester.Core.Models;

namespace WifiTester.Core.Detection;

public sealed class DefectDetector
{
    private readonly MonitorConfig _cfg;
    private readonly IClock _clock;

    private DateTimeOffset? _weakSince;
    private Severity? _weakReportedSeverity;

    private readonly Queue<DateTimeOffset> _roamTimes = new();
    private readonly Queue<bool> _pingWindow = new();
    private const int PingWindowSize = 20;

    public event EventHandler<Defect>? DefectRaised;

    public DefectDetector(MonitorConfig cfg, IClock clock)
    {
        _cfg = cfg;
        _clock = clock;
    }

    public void OnWifiSample(WifiSample s)
    {
        if (s.State != WifiState.Connected) { _weakSince = null; _weakReportedSeverity = null; return; }
        EvaluateWeakSignal(s);
    }

    private void EvaluateWeakSignal(WifiSample s)
    {
        if (s.RssiDbm <= _cfg.WeakSignalWarnDbm)
        {
            _weakSince ??= s.Timestamp;
            var sustained = (s.Timestamp - _weakSince.Value).TotalSeconds;
            if (sustained >= _cfg.WeakSignalSustainSeconds)
            {
                var sev = s.RssiDbm <= _cfg.WeakSignalCriticalDbm ? Severity.Critical : Severity.Warning;
                // Zgłoś raz na epizod, ale pozwól na jednorazową eskalację Warning -> Critical.
                if (_weakReportedSeverity is null || sev > _weakReportedSeverity)
                {
                    Raise(new Defect(_weakSince.Value, s.Timestamp, DefectType.WeakSignal, sev,
                        s.RssiDbm, _cfg.WeakSignalWarnDbm, s.Bssid,
                        $"Słaby sygnał {s.RssiDbm} dBm przez {sustained:F0}s na {s.Bssid}"));
                    _weakReportedSeverity = sev;
                }
            }
        }
        else { _weakSince = null; _weakReportedSeverity = null; }
    }

    public void OnWifiEvent(WifiEvent e)
    {
        if (e.Type == WifiEventType.Disconnected)
            Raise(new Defect(e.Timestamp, e.Timestamp, DefectType.Disconnect, Severity.Critical,
                0, 0, e.FromBssid, $"Rozłączenie z {e.FromBssid} ({e.Reason})"));

        if (e.Type == WifiEventType.Roamed)
        {
            _roamTimes.Enqueue(e.Timestamp);
            var windowStart = e.Timestamp.AddMinutes(-_cfg.RoamStormWindowMinutes);
            while (_roamTimes.Count > 0 && _roamTimes.Peek() < windowStart) _roamTimes.Dequeue();
            if (_roamTimes.Count > _cfg.RoamStormCount)
                Raise(new Defect(_roamTimes.Peek(), e.Timestamp, DefectType.RoamingStorm, Severity.Warning,
                    _roamTimes.Count, _cfg.RoamStormCount, e.ToBssid,
                    $"Roaming storm: {_roamTimes.Count} zmian AP w {_cfg.RoamStormWindowMinutes} min"));
        }
    }

    public void OnLatencySample(LatencySample s)
    {
        if (s.Success && s.RttMs > _cfg.HighLatencyMs)
            Raise(new Defect(s.Timestamp, s.Timestamp, DefectType.HighLatency, Severity.Warning,
                s.RttMs, _cfg.HighLatencyMs, null,
                $"Wysoka latencja do {s.Target}: {s.RttMs:F0} ms"));

        _pingWindow.Enqueue(s.Success);
        while (_pingWindow.Count > PingWindowSize) _pingWindow.Dequeue();
        if (_pingWindow.Count == PingWindowSize)
        {
            var lossPct = 100.0 * _pingWindow.Count(ok => !ok) / _pingWindow.Count;
            if (lossPct > _cfg.PacketLossPercent)
                Raise(new Defect(s.Timestamp, s.Timestamp, DefectType.PacketLoss, Severity.Warning,
                    lossPct, _cfg.PacketLossPercent, null,
                    $"Utrata pakietów {lossPct:F0}% do {s.Target}"));
        }
    }

    public void OnThroughputSample(ThroughputSample s)
    {
        if (s.DownMbps < _cfg.ThroughputMinDownMbps)
            Raise(new Defect(s.Timestamp, s.Timestamp, DefectType.ThroughputDrop, Severity.Warning,
                s.DownMbps, _cfg.ThroughputMinDownMbps, null,
                $"Spadek przepustowości: {s.DownMbps:F1} Mbps (próg {_cfg.ThroughputMinDownMbps})"));
    }

    private void Raise(Defect d) => DefectRaised?.Invoke(this, d);
}
