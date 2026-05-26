using WifiTester.Core.Models;
using WifiTester.Core.Reporting;
using Xunit;

public class HtmlReportGeneratorTests
{
    [Fact]
    public void Html_contains_summary_and_defect_rows()
    {
        var t = DateTimeOffset.UnixEpoch;
        var data = ReportData.Build(t, t.AddHours(1),
            new List<WifiSample>(),
            new List<Defect> {
                new(t, t, DefectType.Disconnect, Severity.Critical, 0, 0, "ap1", "Rozłączenie z ap1")
            });
        var html = HtmlReportGenerator.Generate(data);
        Assert.Contains("<html", html);
        Assert.Contains("Rozłączenie z ap1", html);
        Assert.Contains("Liczba defektów", html);
    }
}
