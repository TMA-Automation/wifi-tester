using WifiTester.Core.Models;
using WifiTester.Core.Wifi;
using Xunit;

public class RoamTrackerTests
{
    private static WifiSample Connected(string bssid, int sec = 0) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(sec), "Wi-Fi", WifiState.Connected,
            "S", bssid, -60, 60, WifiBand.Band5GHz, 36, "ax", 300, 300);

    private static WifiSample Disconnected(int sec) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(sec), "Wi-Fi", WifiState.Disconnected,
            null, null, 0, 0, WifiBand.Unknown, 0, null, 0, 0);

    [Fact]
    public void First_connection_emits_connected()
    {
        var t = new RoamTracker();
        var ev = t.Track(Connected("ap1"));
        Assert.NotNull(ev);
        Assert.Equal(WifiEventType.Connected, ev!.Type);
        Assert.Equal("ap1", ev.ToBssid);
    }

    [Fact]
    public void Same_bssid_emits_nothing()
    {
        var t = new RoamTracker();
        t.Track(Connected("ap1"));
        Assert.Null(t.Track(Connected("ap1", 5)));
    }

    [Fact]
    public void Bssid_change_emits_roamed_with_both_endpoints()
    {
        var t = new RoamTracker();
        t.Track(Connected("ap1"));
        var ev = t.Track(Connected("ap2", 5));
        Assert.NotNull(ev);
        Assert.Equal(WifiEventType.Roamed, ev!.Type);
        Assert.Equal("ap1", ev.FromBssid);
        Assert.Equal("ap2", ev.ToBssid);
    }

    [Fact]
    public void Transition_to_disconnected_emits_disconnected()
    {
        var t = new RoamTracker();
        t.Track(Connected("ap1"));
        var ev = t.Track(Disconnected(5));
        Assert.NotNull(ev);
        Assert.Equal(WifiEventType.Disconnected, ev!.Type);
        Assert.Equal("ap1", ev.FromBssid);
    }

    [Fact]
    public void Reconnect_after_disconnect_emits_connected()
    {
        var t = new RoamTracker();
        t.Track(Connected("ap1"));
        t.Track(Disconnected(5));
        var ev = t.Track(Connected("ap2", 10));
        Assert.NotNull(ev);
        Assert.Equal(WifiEventType.Connected, ev!.Type);
        Assert.Equal("ap2", ev.ToBssid);
    }

    [Fact]
    public void Null_bssid_while_connected_is_ignored_for_roam()
    {
        var t = new RoamTracker();
        t.Track(Connected("ap1"));
        var sampleNoBssid = new WifiSample(DateTimeOffset.UnixEpoch.AddSeconds(5), "Wi-Fi",
            WifiState.Connected, "S", null, -60, 60, WifiBand.Band5GHz, 36, "ax", 300, 300);
        Assert.Null(t.Track(sampleNoBssid));
    }
}
