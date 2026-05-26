using System.Text.Json;

namespace WifiTester.Core.Config;

public sealed class MonitorConfig
{
    public int WifiSampleSeconds { get; set; } = 5;
    public List<string> PingTargets { get; set; } = new() { "gateway", "8.8.8.8" };
    public int PingIntervalSeconds { get; set; } = 5;
    public int ThroughputIntervalMinutes { get; set; } = 60;
    public bool ThroughputEnabled { get; set; } = true;
    public string ThroughputUrl { get; set; } = "https://speed.cloudflare.com/__down?bytes=10000000";

    public int WeakSignalWarnDbm { get; set; } = -75;
    public int WeakSignalCriticalDbm { get; set; } = -82;
    public int WeakSignalSustainSeconds { get; set; } = 30;
    public int RoamStormCount { get; set; } = 3;
    public int RoamStormWindowMinutes { get; set; } = 5;
    public double HighLatencyMs { get; set; } = 100;
    public double PacketLossPercent { get; set; } = 5;
    public double ThroughputMinDownMbps { get; set; } = 10;
    public int LowLinkRateMbps { get; set; } = 24;

    public int RetentionDays { get; set; } = 30;

    public static MonitorConfig Default() => new();

    public string ToJson() =>
        JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

    public static MonitorConfig FromJson(string json) =>
        JsonSerializer.Deserialize<MonitorConfig>(json) ?? Default();

    public static MonitorConfig Load(string path) =>
        File.Exists(path) ? FromJson(File.ReadAllText(path)) : Default();

    public void Save(string path) => File.WriteAllText(path, ToJson());
}
