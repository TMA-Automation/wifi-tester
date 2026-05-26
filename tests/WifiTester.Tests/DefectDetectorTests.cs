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
}
