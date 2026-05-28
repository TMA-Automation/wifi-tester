using WifiTester.Core.Alerts;
using WifiTester.Core.Models;
using WifiTester.Tests.Fakes;
using Xunit;

public class AlertServiceTests
{
    private static Defect Def(DefectType type, DateTimeOffset ts) =>
        new(ts, ts, type, Severity.Warning, 0, 0, "ap1", $"{type}");

    [Fact]
    public void First_defect_of_type_raises_alert()
    {
        var clock = new FakeClock();
        var svc = new AlertService(cooldownSeconds: 60, clock);
        Alert? got = null;
        svc.AlertRaised += (_, a) => got = a;
        svc.OnDefect(Def(DefectType.Disconnect, clock.Now));
        Assert.NotNull(got);
        Assert.Contains("Disconnect", got!.Title + got.Message);
    }

    [Fact]
    public void Same_type_within_cooldown_is_suppressed()
    {
        var clock = new FakeClock();
        var svc = new AlertService(cooldownSeconds: 60, clock);
        int count = 0;
        svc.AlertRaised += (_, _) => count++;
        svc.OnDefect(Def(DefectType.Disconnect, clock.Now));
        clock.Advance(TimeSpan.FromSeconds(30));
        svc.OnDefect(Def(DefectType.Disconnect, clock.Now));
        Assert.Equal(1, count);
    }

    [Fact]
    public void Same_type_after_cooldown_raises_again()
    {
        var clock = new FakeClock();
        var svc = new AlertService(cooldownSeconds: 60, clock);
        int count = 0;
        svc.AlertRaised += (_, _) => count++;
        svc.OnDefect(Def(DefectType.Disconnect, clock.Now));
        clock.Advance(TimeSpan.FromSeconds(90));
        svc.OnDefect(Def(DefectType.Disconnect, clock.Now));
        Assert.Equal(2, count);
    }

    [Fact]
    public void Different_types_are_independent()
    {
        var clock = new FakeClock();
        var svc = new AlertService(cooldownSeconds: 60, clock);
        int count = 0;
        svc.AlertRaised += (_, _) => count++;
        svc.OnDefect(Def(DefectType.Disconnect, clock.Now));
        svc.OnDefect(Def(DefectType.WeakSignal, clock.Now));
        Assert.Equal(2, count);
    }
}
