using WifiTester.Core.Models;
using WifiTester.Core.Reporting;
using Xunit;

public class CsvExporterTests
{
    [Fact]
    public void Defects_to_csv_has_header_and_row()
    {
        var defects = new List<Defect> {
            new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DefectType.Disconnect,
                Severity.Critical, 0, 0, "ap1", "Rozłączenie")
        };
        var csv = CsvExporter.DefectsToCsv(defects);
        Assert.StartsWith("Start,End,Type,Severity,Value,Threshold,Ap,Description", csv);
        Assert.Contains("Disconnect", csv);
        Assert.Contains("ap1", csv);
    }

    [Fact]
    public void Escapes_commas_in_description()
    {
        var defects = new List<Defect> {
            new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DefectType.WeakSignal,
                Severity.Warning, -78, -75, "ap1", "Słaby, sygnał")
        };
        var csv = CsvExporter.DefectsToCsv(defects);
        Assert.Contains("\"Słaby, sygnał\"", csv);
    }
}
