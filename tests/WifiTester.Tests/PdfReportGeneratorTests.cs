using WifiTester.Core.Models;
using WifiTester.Core.Reporting;
using Xunit;

public class PdfReportGeneratorTests
{
    [Fact]
    public void Generates_nonempty_pdf_with_pdf_header()
    {
        var t = DateTimeOffset.UnixEpoch;
        var data = ReportData.Build(t, t.AddHours(1),
            new List<WifiSample>(),
            new List<Defect> {
                new(t, t, DefectType.Disconnect, Severity.Critical, 0, 0, "ap1", "Rozłączenie z ap1")
            });
        var bytes = PdfReportGenerator.Generate(data);
        Assert.True(bytes.Length > 500);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, bytes[..4]);
    }
}
