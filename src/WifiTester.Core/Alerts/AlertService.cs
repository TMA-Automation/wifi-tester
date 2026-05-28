using WifiTester.Core.Abstractions;
using WifiTester.Core.Models;

namespace WifiTester.Core.Alerts;

/// Zamienia defekty na alerty z debounce per typ defektu.
public sealed class AlertService
{
    private readonly int _cooldownSeconds;
    private readonly IClock _clock;
    private readonly Dictionary<DefectType, DateTimeOffset> _lastAlert = new();

    public event EventHandler<Alert>? AlertRaised;

    public AlertService(int cooldownSeconds, IClock clock)
    {
        _cooldownSeconds = cooldownSeconds;
        _clock = clock;
    }

    public void OnDefect(Defect d)
    {
        if (_lastAlert.TryGetValue(d.Type, out var last) &&
            (d.Start - last).TotalSeconds < _cooldownSeconds)
            return;

        _lastAlert[d.Type] = d.Start;
        AlertRaised?.Invoke(this, new Alert(d.Start, d.Severity,
            $"WifiTester: {d.Type}", d.Description));
    }
}
