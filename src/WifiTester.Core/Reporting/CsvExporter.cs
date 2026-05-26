using System.Globalization;
using System.Text;
using WifiTester.Core.Models;

namespace WifiTester.Core.Reporting;

public static class CsvExporter
{
    public static string DefectsToCsv(IEnumerable<Defect> defects)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Start,End,Type,Severity,Value,Threshold,Ap,Description");
        foreach (var d in defects)
            sb.AppendLine(string.Join(",",
                Esc(d.Start.ToString("o")), Esc(d.End.ToString("o")),
                Esc(d.Type.ToString()), Esc(d.Severity.ToString()),
                Esc(d.MetricValue.ToString(CultureInfo.InvariantCulture)),
                Esc(d.Threshold.ToString(CultureInfo.InvariantCulture)),
                Esc(d.ApBssid ?? ""), Esc(d.Description)));
        return sb.ToString();
    }

    private static string Esc(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
}
