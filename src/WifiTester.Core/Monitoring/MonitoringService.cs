using WifiTester.Core.Abstractions;
using WifiTester.Core.Config;
using WifiTester.Core.Detection;
using WifiTester.Core.Models;
using WifiTester.Core.Wifi;

namespace WifiTester.Core.Monitoring;

/// Pętla monitorująca jako serwis. Emituje zdarzenia; subskrybenci (host, GUI) decydują
/// co zapisać/pokazać. Roaming jest wyprowadzany ze strumienia próbek przez RoamTracker.
public sealed class MonitoringService
{
    private readonly MonitorConfig _cfg;
    private readonly IWifiSource _wifi;
    private readonly INetworkProbe _probe;
    private readonly IThroughputTester _throughput;
    private readonly DefectDetector _detector;
    private readonly RoamTracker _roam = new();
    private DateTimeOffset _lastThroughput = DateTimeOffset.MinValue;

    public event EventHandler<WifiSample>? WifiSampleCollected;
    public event EventHandler<WifiEvent>? WifiEventDetected;
    public event EventHandler<LatencySample>? LatencyCollected;
    public event EventHandler<ThroughputSample>? ThroughputCollected;
    public event EventHandler<Defect>? DefectDetected;

    public MonitoringService(MonitorConfig cfg, IWifiSource wifi, INetworkProbe probe,
        IThroughputTester throughput, IClock clock)
    {
        _cfg = cfg; _wifi = wifi; _probe = probe; _throughput = throughput;
        _detector = new DefectDetector(cfg, clock);
        _detector.DefectRaised += (_, d) => DefectDetected?.Invoke(this, d);
    }

    /// Jeden cykl pomiarowy (bez opóźnienia). Wygodne do testów i do osadzenia w pętli/timerze.
    public async Task RunOnceAsync(CancellationToken ct)
    {
        var sample = _wifi.Sample();
        WifiSampleCollected?.Invoke(this, sample);
        _detector.OnWifiSample(sample);

        var ev = _roam.Track(sample);
        if (ev is not null)
        {
            WifiEventDetected?.Invoke(this, ev);
            _detector.OnWifiEvent(ev);
        }

        foreach (var target in _cfg.PingTargets)
        {
            var lat = await _probe.PingAsync(target, ct);
            LatencyCollected?.Invoke(this, lat);
            _detector.OnLatencySample(lat);
        }

        if (_cfg.ThroughputEnabled &&
            DateTimeOffset.Now - _lastThroughput > TimeSpan.FromMinutes(_cfg.ThroughputIntervalMinutes))
        {
            var tp = await _throughput.MeasureAsync(ct);
            ThroughputCollected?.Invoke(this, tp);
            _detector.OnThroughputSample(tp);
            _lastThroughput = DateTimeOffset.Now;
        }
    }

    /// Ciągła pętla odporna na wyjątki (jeden zły cykl nie ubija agenta 24/7).
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Console.Error.WriteLine($"[BŁĄD pętli] {ex.Message}"); }

            try { await Task.Delay(TimeSpan.FromSeconds(_cfg.WifiSampleSeconds), ct); }
            catch (TaskCanceledException) { break; }
        }
    }
}
