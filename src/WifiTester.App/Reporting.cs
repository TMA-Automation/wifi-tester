using System.Diagnostics;
using System.IO;
using WifiTester.Core.Reporting;
using WifiTester.Core.Storage;

namespace WifiTester.App;

internal static class Reporting
{
    public static void GenerateAndOpen(Repository repo)
    {
        var to = DateTimeOffset.Now;
        var from = to.AddDays(-1);
        var data = ReportData.Build(from, to, repo.GetWifiSamples(from, to), repo.GetDefects(from, to));

        var htmlPath = Path.Combine(AppPaths.Dir, $"raport_{to:yyyyMMdd_HHmm}.html");
        File.WriteAllText(htmlPath, HtmlReportGenerator.Generate(data));
        File.WriteAllText(Path.ChangeExtension(htmlPath, ".csv"), CsvExporter.DefectsToCsv(data.Defects));
        File.WriteAllBytes(Path.ChangeExtension(htmlPath, ".pdf"), PdfReportGenerator.Generate(data));

        Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
    }
}
