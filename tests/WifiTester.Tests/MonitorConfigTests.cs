using WifiTester.Core.Config;
using Xunit;

public class MonitorConfigTests
{
    [Fact]
    public void Default_has_sane_values()
    {
        var c = MonitorConfig.Default();
        Assert.Equal(5, c.WifiSampleSeconds);
        Assert.Contains("8.8.8.8", c.PingTargets);
        Assert.Equal(-75, c.WeakSignalWarnDbm);
        Assert.Equal(30, c.RetentionDays);
    }

    [Fact]
    public void Roundtrips_through_json()
    {
        var c = MonitorConfig.Default();
        c.PingTargets = new() { "1.1.1.1" };
        var json = c.ToJson();
        var back = MonitorConfig.FromJson(json);
        Assert.Equal(new[] { "1.1.1.1" }, back.PingTargets);
    }
}
