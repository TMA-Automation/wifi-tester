using WifiTester.Core.Abstractions;
using WifiTester.Core.Config;
using WifiTester.Core.Models;
using WifiTester.Core.Storage;
using WifiTester.Core.Detection;

namespace WifiTester.Host;

public sealed class MonitorLoop
{
    private readonly MonitorConfig _cfg;
    private readonly IWifiSource _wifi;
    private readonly INetworkProbe _probe;
    private readonly IThroughputTester _throughput;
    private readonly Repository _repo;
    private readonly DefectDetector _detector;
    private DateTimeOffset _lastThroughput = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPurge = DateTimeOffset.MinValue;

    public MonitorLoop(MonitorConfig cfg, IWifiSource wifi, INetworkProbe probe,
        IThroughputTester throughput, Repository repo, IClock clock)
    {
        _cfg = cfg; _wifi = wifi; _probe = probe; _throughput = throughput; _repo = repo;
        _detector = new DefectDetector(cfg, clock);
        _detector.DefectRaised += (_, d) => { _repo.SaveDefect(d); Console.WriteLine($"[DEFEKT] {d.Type} {d.Severity}: {d.Description}"); };
        _wifi.WifiEventRaised += (_, e) => { _repo.SaveWifiEvent(e); _detector.OnWifiEvent(e); };
    }

    public async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("WifiTester host uruchomiony. Ctrl+C aby zakończyć.");
        while (!ct.IsCancellationRequested)
        {
            // Jeden zły cykl (np. wyjątek SQLite, błąd procesu netsh) nie może ubić agenta 24/7.
            try
            {
                var sample = _wifi.Sample();
                _repo.SaveWifiSample(sample);
                _detector.OnWifiSample(sample);

                foreach (var target in _cfg.PingTargets)
                {
                    var lat = await _probe.PingAsync(target, ct);
                    _repo.SaveLatencySample(lat);
                    _detector.OnLatencySample(lat);
                }

                if (_cfg.ThroughputEnabled &&
                    DateTimeOffset.Now - _lastThroughput > TimeSpan.FromMinutes(_cfg.ThroughputIntervalMinutes))
                {
                    var tp = await _throughput.MeasureAsync(ct);
                    _repo.SaveThroughputSample(tp);
                    _detector.OnThroughputSample(tp);
                    _lastThroughput = DateTimeOffset.Now;
                }

                if (DateTimeOffset.Now - _lastPurge > TimeSpan.FromHours(1))
                {
                    _repo.Purge(_cfg.RetentionDays);
                    _lastPurge = DateTimeOffset.Now;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[BŁĄD pętli] {ex.Message}");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(_cfg.WifiSampleSeconds), ct); }
            catch (TaskCanceledException) { break; }
        }
    }
}
