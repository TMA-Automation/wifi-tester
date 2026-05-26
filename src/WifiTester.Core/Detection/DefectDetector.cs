using WifiTester.Core.Abstractions;
using WifiTester.Core.Config;
using WifiTester.Core.Models;

namespace WifiTester.Core.Detection;

public sealed class DefectDetector
{
    private readonly MonitorConfig _cfg;
    private readonly IClock _clock;

    private DateTimeOffset? _weakSince;
    private bool _weakReported;

    public event EventHandler<Defect>? DefectRaised;

    public DefectDetector(MonitorConfig cfg, IClock clock)
    {
        _cfg = cfg;
        _clock = clock;
    }

    public void OnWifiSample(WifiSample s)
    {
        if (s.State != WifiState.Connected) { _weakSince = null; _weakReported = false; return; }
        EvaluateWeakSignal(s);
    }

    private void EvaluateWeakSignal(WifiSample s)
    {
        if (s.RssiDbm <= _cfg.WeakSignalWarnDbm)
        {
            _weakSince ??= s.Timestamp;
            var sustained = (s.Timestamp - _weakSince.Value).TotalSeconds;
            if (!_weakReported && sustained >= _cfg.WeakSignalSustainSeconds)
            {
                var sev = s.RssiDbm <= _cfg.WeakSignalCriticalDbm ? Severity.Critical : Severity.Warning;
                Raise(new Defect(_weakSince.Value, s.Timestamp, DefectType.WeakSignal, sev,
                    s.RssiDbm, _cfg.WeakSignalWarnDbm, s.Bssid,
                    $"Słaby sygnał {s.RssiDbm} dBm przez {sustained:F0}s na {s.Bssid}"));
                _weakReported = true;
            }
        }
        else { _weakSince = null; _weakReported = false; }
    }

    public void OnLatencySample(LatencySample s)
    {
        if (s.Success && s.RttMs > _cfg.HighLatencyMs)
            Raise(new Defect(s.Timestamp, s.Timestamp, DefectType.HighLatency, Severity.Warning,
                s.RttMs, _cfg.HighLatencyMs, null,
                $"Wysoka latencja do {s.Target}: {s.RttMs:F0} ms"));
    }

    private void Raise(Defect d) => DefectRaised?.Invoke(this, d);
}
