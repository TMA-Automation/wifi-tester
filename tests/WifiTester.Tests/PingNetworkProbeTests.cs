using WifiTester.Core.Abstractions;
using WifiTester.Core.Probing;
using Xunit;

public class PingNetworkProbeTests
{
    [Fact]
    public async Task Ping_localhost_succeeds()
    {
        INetworkProbe probe = new PingNetworkProbe();
        var s = await probe.PingAsync("127.0.0.1");
        Assert.True(s.Success);
        Assert.Equal("127.0.0.1", s.Target);
        Assert.True(s.RttMs >= 0);
    }

    [Fact]
    public async Task Ping_invalid_host_reports_failure_not_throws()
    {
        INetworkProbe probe = new PingNetworkProbe();
        var s = await probe.PingAsync("this.host.does.not.exist.invalid");
        Assert.False(s.Success);
    }
}
