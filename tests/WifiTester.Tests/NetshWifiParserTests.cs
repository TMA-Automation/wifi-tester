using WifiTester.Core.Models;
using WifiTester.Core.Wifi;
using Xunit;

public class NetshWifiParserTests
{
    private const string Sample = @"
There is 1 interface on the system:

    Name                   : Wi-Fi
    State                  : connected
    SSID                   : FIRMA-WIFI
    BSSID                  : a4:b1:c2:d3:e4:f5
    Radio type             : 802.11ac
    Band                   : 5 GHz
    Channel                : 36
    Signal                 : 72%
    Receive rate (Mbps)    : 433.3
    Transmit rate (Mbps)   : 433.3
";

    [Fact]
    public void Parses_connected_interface()
    {
        var ts = DateTimeOffset.UnixEpoch;
        var s = NetshWifiParser.Parse(Sample, ts);
        Assert.Equal(WifiState.Connected, s.State);
        Assert.Equal("FIRMA-WIFI", s.Ssid);
        Assert.Equal("a4:b1:c2:d3:e4:f5", s.Bssid);
        Assert.Equal(WifiBand.Band5GHz, s.Band);
        Assert.Equal(36, s.Channel);
        Assert.Equal(72, s.SignalQuality);
        Assert.Equal(433, s.TxRateMbps);
        Assert.Equal(433, s.RxRateMbps);
        Assert.Equal(-64, s.RssiDbm);
    }

    [Fact]
    public void Parses_disconnected_when_no_interface()
    {
        var s = NetshWifiParser.Parse("There is 1 interface on the system:\n\n    Name : Wi-Fi\n    State : disconnected\n", DateTimeOffset.UnixEpoch);
        Assert.Equal(WifiState.Disconnected, s.State);
    }

    private const string Win11Sample = @"
There is 1 interface on the system:

    Name                   : Wi-Fi
    Description            : Intel(R) Wi-Fi 6E AX211 160MHz
    State                  : connected
    SSID                   : TMA
    AP BSSID               : 04:01:a1:24:fb:20
    Band                   : 5 GHz
    Channel                : 153
    Radio type             : 802.11ax
    Receive rate (Mbps)    : 432
    Transmit rate (Mbps)   : 1201
    Signal                 : 88%
    Rssi                   : -65
";

    [Fact]
    public void Parses_ap_bssid_label()
    {
        var s = NetshWifiParser.Parse(Win11Sample, DateTimeOffset.UnixEpoch);
        Assert.Equal("04:01:a1:24:fb:20", s.Bssid);
    }

    [Fact]
    public void Prefers_real_rssi_field_over_signal_percent()
    {
        var s = NetshWifiParser.Parse(Win11Sample, DateTimeOffset.UnixEpoch);
        Assert.Equal(-65, s.RssiDbm);
        Assert.Equal(88, s.SignalQuality);
    }
}
