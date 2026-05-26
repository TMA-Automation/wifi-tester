using System.Net;
using System.Text;

namespace WifiTester.Core.Reporting;

public static class HtmlReportGenerator
{
    public static string Generate(ReportData d)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"pl\"><head><meta charset=\"utf-8\">");
        sb.Append("<title>Raport WifiTester</title>");
        sb.Append("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:2rem}" +
                  "table{border-collapse:collapse;width:100%}td,th{border:1px solid #ccc;padding:6px}" +
                  "th{background:#f0f0f0;text-align:left}.crit{color:#b00}</style></head><body>");
        sb.Append($"<h1>Raport WifiTester</h1>");
        sb.Append($"<p>Okres: {d.From:g} – {d.To:g}</p>");
        sb.Append("<h2>Podsumowanie</h2><ul>");
        sb.Append($"<li>Liczba defektów: <b>{d.TotalDefects}</b></li>");
        sb.Append($"<li>Średni RSSI: <b>{d.AverageRssi} dBm</b></li>");
        sb.Append($"<li>Najgorszy AP: <b>{WebUtility.HtmlEncode(d.WorstAp ?? "—")}</b></li>");
        sb.Append("</ul>");
        sb.Append("<h3>Defekty wg typu</h3><ul>");
        foreach (var kv in d.DefectsByType)
            sb.Append($"<li>{kv.Key}: {kv.Value}</li>");
        sb.Append("</ul>");
        sb.Append("<h2>Lista defektów</h2><table><tr><th>Czas</th><th>Typ</th>" +
                  "<th>Waga</th><th>AP</th><th>Opis</th></tr>");
        foreach (var df in d.Defects)
        {
            var cls = df.Severity == Models.Severity.Critical ? " class=\"crit\"" : "";
            sb.Append($"<tr{cls}><td>{df.Start:g}</td><td>{df.Type}</td><td>{df.Severity}</td>" +
                      $"<td>{WebUtility.HtmlEncode(df.ApBssid ?? "")}</td>" +
                      $"<td>{WebUtility.HtmlEncode(df.Description)}</td></tr>");
        }
        sb.Append("</table></body></html>");
        return sb.ToString();
    }
}
