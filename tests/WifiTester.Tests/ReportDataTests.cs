using WifiTester.Core.Models;
using WifiTester.Core.Reporting;
using Xunit;

public class ReportDataTests
{
    [Fact]
    public void Summarizes_defects_and_signal()
    {
        var t = DateTimeOffset.UnixEpoch;
        var wifi = new List<WifiSample> {
            new(t, "Wi-Fi", WifiState.Connected, "S", "ap1", -60, 70, WifiBand.Band5GHz, 36, "ac", 300, 300),
            new(t.AddSeconds(5), "Wi-Fi", WifiState.Connected, "S", "ap1", -70, 50, WifiBand.Band5GHz, 36, "ac", 200, 200),
        };
        var defects = new List<Defect> {
            new(t, t, DefectType.Disconnect, Severity.Critical, 0, 0, "ap1", "d1"),
            new(t, t, DefectType.WeakSignal, Severity.Warning, -78, -75, "ap1", "d2"),
        };
        var r = ReportData.Build(t, t.AddMinutes(1), wifi, defects);
        Assert.Equal(2, r.TotalDefects);
        Assert.Equal(1, r.DefectsByType[DefectType.Disconnect]);
        Assert.Equal(-65, r.AverageRssi);
        Assert.Equal("ap1", r.WorstAp);
    }
}
