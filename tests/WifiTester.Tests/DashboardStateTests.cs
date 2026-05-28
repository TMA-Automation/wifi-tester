using WifiTester.Core.Dashboard;
using WifiTester.Core.Models;
using Xunit;

public class DashboardStateTests
{
    private static WifiSample W(string? bssid, int rssi) =>
        new(DateTimeOffset.UnixEpoch, "Wi-Fi", WifiState.Connected, "TMA", bssid,
            rssi, 70, WifiBand.Band5GHz, 36, "ax", 433, 433);

    [Fact]
    public void Tracks_latest_wifi_sample()
    {
        var s = new DashboardState(recentDefectLimit: 50);
        s.OnWifiSample(W("ap1", -60));
        s.OnWifiSample(W("ap1", -65));
        Assert.Equal(-65, s.LatestWifi!.RssiDbm);
    }

    [Fact]
    public void Tracks_latest_latency_per_target()
    {
        var s = new DashboardState(recentDefectLimit: 50);
        s.OnLatency(new LatencySample(DateTimeOffset.UnixEpoch, "8.8.8.8", 20, true));
        s.OnLatency(new LatencySample(DateTimeOffset.UnixEpoch, "8.8.8.8", 30, true));
        s.OnLatency(new LatencySample(DateTimeOffset.UnixEpoch, "gateway", 2, true));
        Assert.Equal(30, s.LatestLatencyByTarget["8.8.8.8"].RttMs);
        Assert.Equal(2, s.LatestLatencyByTarget["gateway"].RttMs);
    }

    [Fact]
    public void Keeps_recent_defects_newest_first_capped()
    {
        var s = new DashboardState(recentDefectLimit: 2);
        for (int i = 0; i < 3; i++)
            s.OnDefect(new Defect(DateTimeOffset.UnixEpoch.AddSeconds(i), DateTimeOffset.UnixEpoch,
                DefectType.Disconnect, Severity.Critical, 0, 0, "ap1", $"d{i}"));
        Assert.Equal(2, s.RecentDefects.Count);
        Assert.Equal("d2", s.RecentDefects[0].Description);
        Assert.Equal("d1", s.RecentDefects[1].Description);
    }

    [Fact]
    public void Counts_total_defects()
    {
        var s = new DashboardState(recentDefectLimit: 50);
        s.OnDefect(new Defect(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            DefectType.WeakSignal, Severity.Warning, 0, 0, "ap1", "x"));
        s.OnDefect(new Defect(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            DefectType.Disconnect, Severity.Critical, 0, 0, "ap1", "y"));
        Assert.Equal(2, s.TotalDefects);
    }
}
