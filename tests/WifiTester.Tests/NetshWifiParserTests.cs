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
}
