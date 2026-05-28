using WifiTester.Core.Models;

namespace WifiTester.Core.Dashboard;

/// Biezacy stan do pokazania w dashboardzie. Aktualizowany zdarzeniami MonitoringService.
/// Czysta logika (bez WPF) — testowalna w Core.
public sealed class DashboardState
{
    private readonly int _recentDefectLimit;
    private readonly List<Defect> _recentDefects = new();

    public DashboardState(int recentDefectLimit) => _recentDefectLimit = recentDefectLimit;

    public WifiSample? LatestWifi { get; private set; }
    public Dictionary<string, LatencySample> LatestLatencyByTarget { get; } = new();
    public IReadOnlyList<Defect> RecentDefects => _recentDefects;
    public int TotalDefects { get; private set; }

    public void OnWifiSample(WifiSample s) => LatestWifi = s;

    public void OnLatency(LatencySample s) => LatestLatencyByTarget[s.Target] = s;

    public void OnDefect(Defect d)
    {
        TotalDefects++;
        _recentDefects.Insert(0, d);
        if (_recentDefects.Count > _recentDefectLimit)
            _recentDefects.RemoveAt(_recentDefects.Count - 1);
    }
}
