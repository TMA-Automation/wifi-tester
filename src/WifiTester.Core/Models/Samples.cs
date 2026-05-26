namespace WifiTester.Core.Models;

public enum WifiState { Connected, Disconnected, NoAdapter }
public enum WifiBand { Unknown, Band24GHz, Band5GHz, Band6GHz }
public enum WifiEventType { Connected, Disconnected, Roamed, SignalChange }

public record WifiSample(
    DateTimeOffset Timestamp,
    string InterfaceName,
    WifiState State,
    string? Ssid,
    string? Bssid,
    int RssiDbm,
    int SignalQuality,   // 0-100
    WifiBand Band,
    int Channel,
    string? PhyType,
    int TxRateMbps,
    int RxRateMbps);

public record WifiEvent(
    DateTimeOffset Timestamp,
    WifiEventType Type,
    string? FromBssid,
    string? ToBssid,
    string? Reason);

public record LatencySample(
    DateTimeOffset Timestamp,
    string Target,
    double RttMs,
    bool Success);

public record ThroughputSample(
    DateTimeOffset Timestamp,
    double DownMbps,
    double UpMbps,
    string Server);
