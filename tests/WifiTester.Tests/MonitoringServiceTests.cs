using WifiTester.Core.Abstractions;
using WifiTester.Core.Config;
using WifiTester.Core.Models;
using WifiTester.Core.Monitoring;
using WifiTester.Tests.Fakes;
using Xunit;

public class MonitoringServiceTests
{
    private sealed class FixedWifi : IWifiSource
    {
        private readonly Queue<WifiSample> _samples;
        public FixedWifi(IEnumerable<WifiSample> s) => _samples = new(s);
        public event EventHandler<WifiEvent>? WifiEventRaised;
        public WifiSample Sample() => _samples.Count > 1 ? _samples.Dequeue() : _samples.Peek();
    }
    private sealed class NoProbe : INetworkProbe
    {
        public Task<LatencySample> PingAsync(string target, CancellationToken ct = default)
            => Task.FromResult(new LatencySample(DateTimeOffset.UnixEpoch, target, 5, true));
    }
    private sealed class NoThroughput : IThroughputTester
    {
        public Task<ThroughputSample> MeasureAsync(CancellationToken ct = default)
            => Task.FromResult(new ThroughputSample(DateTimeOffset.UnixEpoch, 100, 0, "fake"));
    }

    private static WifiSample W(string bssid) =>
        new(DateTimeOffset.UnixEpoch, "Wi-Fi", WifiState.Connected, "S", bssid,
            -60, 60, WifiBand.Band5GHz, 36, "ax", 300, 300);

    [Fact]
    public async Task Runs_one_tick_and_emits_sample_and_latency()
    {
        var cfg = MonitorConfig.Default();
        cfg.PingTargets = new() { "8.8.8.8" };
        cfg.ThroughputEnabled = false;
        var svc = new MonitoringService(cfg, new FixedWifi(new[] { W("ap1") }),
            new NoProbe(), new NoThroughput(), new FakeClock());
        var samples = new List<WifiSample>();
        var lats = new List<LatencySample>();
        svc.WifiSampleCollected += (_, s) => samples.Add(s);
        svc.LatencyCollected += (_, l) => lats.Add(l);

        await svc.RunOnceAsync(CancellationToken.None);

        Assert.Single(samples);
        Assert.Single(lats);
        Assert.Equal("ap1", samples[0].Bssid);
    }

    [Fact]
    public async Task Emits_roam_event_and_defect_on_bssid_change()
    {
        var cfg = MonitorConfig.Default();
        cfg.PingTargets = new();
        cfg.ThroughputEnabled = false;
        var wifi = new FixedWifi(new[] { W("ap1"), W("ap2") });
        var svc = new MonitoringService(cfg, wifi, new NoProbe(), new NoThroughput(), new FakeClock());
        var events = new List<WifiEvent>();
        svc.WifiEventDetected += (_, e) => events.Add(e);

        await svc.RunOnceAsync(CancellationToken.None);
        await svc.RunOnceAsync(CancellationToken.None);

        Assert.Contains(events, e => e.Type == WifiEventType.Connected);
        Assert.Contains(events, e => e.Type == WifiEventType.Roamed && e.ToBssid == "ap2");
    }
}
