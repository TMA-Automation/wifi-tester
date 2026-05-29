using WifiTester.Core.Abstractions;
using WifiTester.Core.Config;
using WifiTester.Core.Dashboard;
using WifiTester.Core.Models;
using WifiTester.Core.Monitoring;
using WifiTester.Tests.Fakes;
using Xunit;

/// E2E głównego scenariusza (standard TMA): realny strumień próbek → MonitoringService
/// → wykrycie defektu → DashboardState (agregat, na którym opiera się GUI). Weryfikuje
/// wynik, który zobaczy użytkownik: stan WiFi, latencje i wykryty defekt rozłączenia.
public class EndToEndPipelineTests
{
    private sealed class ScriptedWifi : IWifiSource
    {
        private readonly Queue<WifiSample> _s;
        public ScriptedWifi(IEnumerable<WifiSample> s) => _s = new(s);
#pragma warning disable CS0067
        public event EventHandler<WifiEvent>? WifiEventRaised;
#pragma warning restore CS0067
        public WifiSample Sample() => _s.Count > 1 ? _s.Dequeue() : _s.Peek();
    }
    private sealed class FixedProbe : INetworkProbe
    {
        public Task<LatencySample> PingAsync(string target, CancellationToken ct = default)
            => Task.FromResult(new LatencySample(DateTimeOffset.UnixEpoch, target, 12, true));
    }
    private sealed class NoThroughput : IThroughputTester
    {
        public Task<ThroughputSample> MeasureAsync(CancellationToken ct = default)
            => Task.FromResult(new ThroughputSample(DateTimeOffset.UnixEpoch, 100, 0, "fake"));
    }

    private static WifiSample Connected(string bssid) =>
        new(DateTimeOffset.UnixEpoch, "Wi-Fi", WifiState.Connected, "TMA", bssid,
            -58, 72, WifiBand.Band5GHz, 36, "ax", 433, 433);
    private static WifiSample Disconnected() =>
        new(DateTimeOffset.UnixEpoch, "Wi-Fi", WifiState.Disconnected, null, null,
            0, 0, WifiBand.Unknown, 0, null, 0, 0);

    [Fact]
    public async Task Full_flow_connect_then_disconnect_surfaces_defect_in_dashboard()
    {
        var cfg = MonitorConfig.Default();
        cfg.PingTargets = new() { "8.8.8.8" };
        cfg.ThroughputEnabled = false;

        var svc = new MonitoringService(cfg,
            new ScriptedWifi(new[] { Connected("ap1"), Disconnected() }),
            new FixedProbe(), new NoThroughput(), new FakeClock());

        // Pełne okablowanie jak w GUI (DashboardViewModel).
        var dash = new DashboardState(recentDefectLimit: 100);
        svc.WifiSampleCollected += (_, s) => dash.OnWifiSample(s);
        svc.LatencyCollected += (_, l) => dash.OnLatency(l);
        svc.DefectDetected += (_, d) => dash.OnDefect(d);

        await svc.RunOnceAsync(CancellationToken.None);   // połączenie z ap1
        await svc.RunOnceAsync(CancellationToken.None);   // rozłączenie

        // Artefakt widziany przez użytkownika:
        Assert.Equal(WifiState.Disconnected, dash.LatestWifi!.State);
        Assert.Equal(12, dash.LatestLatencyByTarget["8.8.8.8"].RttMs);
        Assert.True(dash.TotalDefects >= 1);
        Assert.Contains(dash.RecentDefects, d => d.Type == DefectType.Disconnect);
    }
}
