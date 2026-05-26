namespace WifiTester.Core.Models;

public enum DefectType
{
    Disconnect, RoamingStorm, WeakSignal, HighLatency,
    PacketLoss, ThroughputDrop, LowLinkRate
}
public enum Severity { Info, Warning, Critical }

public record Defect(
    DateTimeOffset Start,
    DateTimeOffset End,
    DefectType Type,
    Severity Severity,
    double MetricValue,
    double Threshold,
    string? ApBssid,
    string Description);
