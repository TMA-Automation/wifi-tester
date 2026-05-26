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

    [Fact]
    public void Weak_signal_reports_once_then_escalates_to_critical()
    {
        var (d, defects, c) = Make();
        d.OnWifiSample(Wifi(c.Now, -78));               // start epizodu (Warning, -78)
        c.Advance(TimeSpan.FromSeconds(35));
        d.OnWifiSample(Wifi(c.Now, -78));               // Warning zgłoszony
        c.Advance(TimeSpan.FromSeconds(5));
        d.OnWifiSample(Wifi(c.Now, -78));               // dalej Warning -> brak ponownego zgłoszenia
        var afterWarning = defects.Count(x => x.Type == DefectType.WeakSignal);
        Assert.Equal(1, afterWarning);

        c.Advance(TimeSpan.FromSeconds(5));
        d.OnWifiSample(Wifi(c.Now, -85));               // pogorszenie do Critical -> jednorazowa eskalacja
        Assert.Contains(defects, x => x.Type == DefectType.WeakSignal && x.Severity == Severity.Critical);
        Assert.Equal(2, defects.Count(x => x.Type == DefectType.WeakSignal));
    }

    [Fact]
    public void Weak_signal_resets_after_recovery_and_can_retrigger()
    {
        var (d, defects, c) = Make();
        d.OnWifiSample(Wifi(c.Now, -78));
        c.Advance(TimeSpan.FromSeconds(35));
        d.OnWifiSample(Wifi(c.Now, -78));               // 1. Warning
        d.OnWifiSample(Wifi(c.Now, -50));               // powrót sygnału -> reset epizodu
        d.OnWifiSample(Wifi(c.Now, -78));               // nowy epizod start
        c.Advance(TimeSpan.FromSeconds(35));
        d.OnWifiSample(Wifi(c.Now, -78));               // 2. Warning (nowy epizod)
        Assert.Equal(2, defects.Count(x => x.Type == DefectType.WeakSignal));
    }

    [Fact]
    public void Disconnect_resets_weak_signal_episode()
    {
        var (d, defects, c) = Make();
        d.OnWifiSample(Wifi(c.Now, -78));
        c.Advance(TimeSpan.FromSeconds(20));            // jeszcze poniżej progu 30s
        d.OnWifiSample(new WifiSample(c.Now, "Wi-Fi", WifiState.Disconnected,
            null, null, 0, 0, WifiBand.Unknown, 0, null, 0, 0));  // reset
        c.Advance(TimeSpan.FromSeconds(20));
        d.OnWifiSample(Wifi(c.Now, -78));               // liczenie sustain zaczyna się od nowa
        Assert.DoesNotContain(defects, x => x.Type == DefectType.WeakSignal);
    }

    [Fact]
    public void Roaming_spread_over_time_does_not_raise_storm()
    {
        var (d, defects, c) = Make();
        for (int i = 0; i < 5; i++)
        {
            d.OnWifiEvent(new WifiEvent(c.Now, WifiEventType.Roamed, "ap1", "ap2", null));
            c.Advance(TimeSpan.FromMinutes(2));         // > okno 5 min na 4 zdarzenia
        }
        Assert.DoesNotContain(defects, x => x.Type == DefectType.RoamingStorm);
    }

    [Fact]
    public void Packet_loss_at_threshold_boundary_does_not_raise()
    {
        var (d, defects, c) = Make();
        // 1 strata / 20 = 5%, a próg to "> 5%" -> nie zgłasza
        for (int i = 0; i < 20; i++)
            d.OnLatencySample(new LatencySample(c.Now, "8.8.8.8", 10, Success: i != 0));
        Assert.DoesNotContain(defects, x => x.Type == DefectType.PacketLoss);
    }
}
