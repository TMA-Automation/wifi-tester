using WifiTester.Core.Monitoring;
using WifiTester.Core.Storage;

namespace WifiTester.App;

internal sealed class Recording
{
    private readonly Repository _repo;
    private DateTimeOffset _lastPurge = DateTimeOffset.MinValue;
    private readonly int _retentionDays;

    public Recording(Repository repo, int retentionDays)
    {
        _repo = repo;
        _retentionDays = retentionDays;
    }

    public void Attach(MonitoringService svc)
    {
        svc.WifiSampleCollected += (_, s) => _repo.SaveWifiSample(s);
        svc.WifiEventDetected += (_, e) => _repo.SaveWifiEvent(e);
        svc.LatencyCollected += (_, l) => _repo.SaveLatencySample(l);
        svc.ThroughputCollected += (_, t) => _repo.SaveThroughputSample(t);
        svc.DefectDetected += (_, d) => _repo.SaveDefect(d);
        svc.WifiSampleCollected += (_, _) =>
        {
            if (DateTimeOffset.Now - _lastPurge > TimeSpan.FromHours(1))
            {
                _repo.Purge(_retentionDays);
                _lastPurge = DateTimeOffset.Now;
            }
        };
    }
}
