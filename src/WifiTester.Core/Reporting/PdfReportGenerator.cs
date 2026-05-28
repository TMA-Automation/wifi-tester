using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WifiTester.Core.Models;

namespace WifiTester.Core.Reporting;

public static class PdfReportGenerator
{
    public static byte[] Generate(ReportData d)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(40);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Text("Raport WifiTester").FontSize(18).Bold();

                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text($"Okres: {d.From:g} – {d.To:g}");
                    col.Item().Text($"Liczba defektów: {d.TotalDefects}").Bold();
                    col.Item().Text($"Średni RSSI: {d.AverageRssi} dBm");
                    col.Item().Text($"Najgorszy AP: {d.WorstAp ?? "—"}");

                    col.Item().PaddingTop(10).Text("Lista defektów").FontSize(13).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2); c.RelativeColumn(2);
                            c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(4);
                        });
                        foreach (var h in new[] { "Czas", "Typ", "Waga", "AP", "Opis" })
                            table.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text(h).Bold();
                        foreach (var df in d.Defects)
                        {
                            table.Cell().Padding(3).Text($"{df.Start:g}");
                            table.Cell().Padding(3).Text(df.Type.ToString());
                            table.Cell().Padding(3).Text(df.Severity.ToString());
                            table.Cell().Padding(3).Text(df.ApBssid ?? "");
                            table.Cell().Padding(3).Text(df.Description);
                        }
                    });
                });

                page.Footer().AlignCenter().Text(x => { x.Span("WifiTester • "); x.Span($"{DateTimeOffset.Now:g}"); });
            });
        }).GeneratePdf();
    }
}
