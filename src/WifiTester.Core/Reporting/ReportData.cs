using WifiTester.Core.Models;

namespace WifiTester.Core.Reporting;

public sealed class ReportData
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public int TotalDefects { get; init; }
    public Dictionary<DefectType, int> DefectsByType { get; init; } = new();
    public double AverageRssi { get; init; }
    public string? WorstAp { get; init; }
    public IReadOnlyList<Defect> Defects { get; init; } = Array.Empty<Defect>();

    public static ReportData Build(DateTimeOffset from, DateTimeOffset to,
        IReadOnlyList<WifiSample> wifi, IReadOnlyList<Defect> defects)
    {
        var connected = wifi.Where(w => w.State == WifiState.Connected).ToList();
        var byType = defects.GroupBy(d => d.Type).ToDictionary(g => g.Key, g => g.Count());
        var worstAp = defects.Where(d => d.ApBssid != null)
            .GroupBy(d => d.ApBssid!)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key).FirstOrDefault();

        return new ReportData
        {
            From = from,
            To = to,
            TotalDefects = defects.Count,
            DefectsByType = byType,
            AverageRssi = connected.Count > 0 ? Math.Round(connected.Average(w => w.RssiDbm)) : 0,
            WorstAp = worstAp,
            Defects = defects
        };
    }
}
