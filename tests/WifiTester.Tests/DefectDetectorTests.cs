using WifiTester.Core.Config;
using WifiTester.Core.Detection;
using WifiTester.Core.Models;
using WifiTester.Tests.Fakes;
using Xunit;

public class DefectDetectorTests
{
    private static (DefectDetector d, List<Defect> defects, FakeClock clock) Make()
    {
        var clock = new FakeClock();
        var det = new DefectDetector(MonitorConfig.Default(), clock);
        var captured = new List<Defect>();
        det.DefectRaised += (_, def) => captured.Add(def);
        return (det, captured, clock);
    }

    private static WifiSample Wifi(DateTimeOffset ts, int rssi, string bssid = "ap1") =>
        new(ts, "Wi-Fi", WifiState.Connected, "S", bssid, rssi, 50, WifiBand.Band5GHz, 36, "ac", 300, 300);

    [Fact]
    public void Weak_signal_sustained_raises_warning()
    {
        var (d, defects, c) = Make();
        d.OnWifiSample(Wifi(c.Now, -78));
        c.Advance(TimeSpan.FromSeconds(35));
        d.OnWifiSample(Wifi(c.Now, -78));
        Assert.Contains(defects, x => x.Type == DefectType.WeakSignal && x.Severity == Severity.Warning);
    }

    [Fact]
    public void Strong_signal_does_not_raise()
    {
        var (d, defects, c) = Make();
        d.OnWifiSample(Wifi(c.Now, -55));
        c.Advance(TimeSpan.FromSeconds(35));
        d.OnWifiSample(Wifi(c.Now, -55));
        Assert.Empty(defects);
    }

    [Fact]
    public void High_latency_raises_defect()
    {
        var (d, defects, _) = Make();
        d.OnLatencySample(new LatencySample(DateTimeOffset.UnixEpoch, "gateway", 150, true));
        Assert.Contains(defects, x => x.Type == DefectType.HighLatency);
    }

    [Fact]
    public void Disconnect_event_raises_defect()
    {
        var (d, defects, _) = Make();
        d.OnWifiEvent(new WifiEvent(DateTimeOffset.UnixEpoch, WifiEventType.Disconnected, "ap1", null, "lost"));
        Assert.Contains(defects, x => x.Type == DefectType.Disconnect);
    }

    [Fact]
    public void Roaming_storm_raises_after_threshold()
    {
        var (d, defects, c) = Make();
        for (int i = 0; i < 4; i++)
        {
            d.OnWifiEvent(new WifiEvent(c.Now, WifiEventType.Roamed, "ap1", "ap2", null));
            c.Advance(TimeSpan.FromSeconds(30));
        }
        Assert.Contains(defects, x => x.Type == DefectType.RoamingStorm);
    }

    [Fact]
    public void Packet_loss_above_threshold_raises()
    {
        var (d, defects, c) = Make();
        for (int i = 0; i < 20; i++)
            d.OnLatencySample(new LatencySample(c.Now, "8.8.8.8", 10, Success: i >= 3));
        Assert.Contains(defects, x => x.Type == DefectType.PacketLoss);
    }

    [Fact]
    public void Throughput_below_threshold_raises()
    {
        var (d, defects, _) = Make();
        d.OnThroughputSample(new ThroughputSample(DateTimeOffset.UnixEpoch, 5, 5, "srv"));
        Assert.Contains(defects, x => x.Type == DefectType.ThroughputDrop);
    }
}
