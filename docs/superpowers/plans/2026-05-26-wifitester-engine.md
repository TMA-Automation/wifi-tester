# WifiTester — Silnik monitorujący (Plan 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Zbudować headless silnik (`WifiTester.Core`) + konsolowy host, który ciągle zbiera próbki WiFi/latencji/przepustowości do SQLite, wykrywa defekty regułami i generuje raport CSV/HTML — w pełni testowalny i użyteczny bez GUI.

**Architecture:** Czysta biblioteka .NET z modelami i logiką (detektor defektów, repozytorium SQLite, parser `netsh`, sonda sieciowa, konfiguracja) ukrytą za interfejsami źródeł. Konsolowy host spina samplery w pętli i zapisuje dane. GUI (Plan 2) podłączy się do tych samych interfejsów.

**Tech Stack:** .NET 8, C#, xUnit, Microsoft.Data.Sqlite, Dapper, ScottPlot (raport), System.Net.NetworkInformation.

---

## Struktura plików

```
WifiTester.sln
src/WifiTester.Core/
  WifiTester.Core.csproj
  Models/Samples.cs          # WifiSample, LatencySample, ThroughputSample, WifiEvent, enums
  Models/Defect.cs           # Defect, DefectType, Severity
  Config/MonitorConfig.cs    # konfiguracja + ładowanie/zapis JSON
  Abstractions/IWifiSource.cs
  Abstractions/INetworkProbe.cs
  Abstractions/IThroughputTester.cs
  Abstractions/IClock.cs
  Detection/DefectDetector.cs
  Storage/Repository.cs      # schemat + zapis/odczyt/retencja (SQLite)
  Probing/PingNetworkProbe.cs
  Probing/HttpThroughputTester.cs
  Wifi/NetshWifiParser.cs    # parser tekstu `netsh wlan show interfaces`
  Reporting/ReportData.cs    # agregacja danych do raportu
  Reporting/CsvExporter.cs
  Reporting/HtmlReportGenerator.cs
src/WifiTester.Host/
  WifiTester.Host.csproj     # konsolowy host (Worker/pętla)
  Program.cs
  MonitorLoop.cs
tests/WifiTester.Tests/
  WifiTester.Tests.csproj
  DefectDetectorTests.cs
  NetshWifiParserTests.cs
  RepositoryTests.cs
  ReportDataTests.cs
  CsvExporterTests.cs
  Fakes/FakeClock.cs
```

---

### Task 1: Scaffolding rozwiązania

**Files:**
- Create: `WifiTester.sln`, `src/WifiTester.Core/WifiTester.Core.csproj`, `src/WifiTester.Host/WifiTester.Host.csproj`, `tests/WifiTester.Tests/WifiTester.Tests.csproj`

- [ ] **Step 1: Utwórz solution i projekty**

Run (w katalogu repo):
```bash
dotnet new sln -n WifiTester
dotnet new classlib -n WifiTester.Core -o src/WifiTester.Core -f net8.0
dotnet new console -n WifiTester.Host -o src/WifiTester.Host -f net8.0
dotnet new xunit -n WifiTester.Tests -o tests/WifiTester.Tests -f net8.0
dotnet sln add src/WifiTester.Core src/WifiTester.Host tests/WifiTester.Tests
dotnet add src/WifiTester.Host reference src/WifiTester.Core
dotnet add tests/WifiTester.Tests reference src/WifiTester.Core
```

- [ ] **Step 2: Dodaj pakiety NuGet do Core**

Run:
```bash
dotnet add src/WifiTester.Core package Microsoft.Data.Sqlite --version 8.0.*
dotnet add src/WifiTester.Core package Dapper --version 2.1.*
dotnet add src/WifiTester.Core package ScottPlot --version 5.0.*
```

- [ ] **Step 3: Usuń pliki szablonowe**

Usuń `src/WifiTester.Core/Class1.cs`, `tests/WifiTester.Tests/UnitTest1.cs`.

- [ ] **Step 4: Zbuduj puste rozwiązanie**

Run: `dotnet build`
Expected: Build succeeded, 0 Error.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore: scaffold WifiTester solution"
```

---

### Task 2: Modele danych

**Files:**
- Create: `src/WifiTester.Core/Models/Samples.cs`, `src/WifiTester.Core/Models/Defect.cs`

- [ ] **Step 1: Utwórz `Samples.cs`**

```csharp
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
```

- [ ] **Step 2: Utwórz `Defect.cs`**

```csharp
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
```

- [ ] **Step 3: Zbuduj**

Run: `dotnet build src/WifiTester.Core`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: add core data models"
```

---

### Task 3: Abstrakcje (interfejsy źródeł + zegar)

**Files:**
- Create: `src/WifiTester.Core/Abstractions/IClock.cs`, `IWifiSource.cs`, `INetworkProbe.cs`, `IThroughputTester.cs`

- [ ] **Step 1: Utwórz `IClock.cs`**

```csharp
namespace WifiTester.Core.Abstractions;

public interface IClock { DateTimeOffset Now { get; } }

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
```

- [ ] **Step 2: Utwórz `IWifiSource.cs`**

```csharp
using WifiTester.Core.Models;

namespace WifiTester.Core.Abstractions;

public interface IWifiSource
{
    /// Odczyt bieżącego stanu połączenia WiFi (jednorazowy).
    WifiSample Sample();

    /// Zdarzenia WLAN (connect/disconnect/roam). Implementacja headless może nie emitować.
    event EventHandler<WifiEvent>? WifiEventRaised;
}
```

- [ ] **Step 3: Utwórz `INetworkProbe.cs`**

```csharp
using WifiTester.Core.Models;

namespace WifiTester.Core.Abstractions;

public interface INetworkProbe
{
    /// Pojedynczy ping do celu.
    Task<LatencySample> PingAsync(string target, CancellationToken ct = default);
}
```

- [ ] **Step 4: Utwórz `IThroughputTester.cs`**

```csharp
using WifiTester.Core.Models;

namespace WifiTester.Core.Abstractions;

public interface IThroughputTester
{
    Task<ThroughputSample> MeasureAsync(CancellationToken ct = default);
}
```

- [ ] **Step 5: Zbuduj i commit**

Run: `dotnet build src/WifiTester.Core`
Expected: Build succeeded.
```bash
git add -A
git commit -m "feat: add source abstractions and clock"
```

---

### Task 4: Konfiguracja

**Files:**
- Create: `src/WifiTester.Core/Config/MonitorConfig.cs`
- Test: `tests/WifiTester.Tests/MonitorConfigTests.cs`

- [ ] **Step 1: Napisz test (round-trip JSON + domyślne wartości)**

`tests/WifiTester.Tests/MonitorConfigTests.cs`:
```csharp
using WifiTester.Core.Config;
using Xunit;

public class MonitorConfigTests
{
    [Fact]
    public void Default_has_sane_values()
    {
        var c = MonitorConfig.Default();
        Assert.Equal(5, c.WifiSampleSeconds);
        Assert.Contains("8.8.8.8", c.PingTargets);
        Assert.Equal(-75, c.WeakSignalWarnDbm);
        Assert.Equal(30, c.RetentionDays);
    }

    [Fact]
    public void Roundtrips_through_json()
    {
        var c = MonitorConfig.Default();
        c.PingTargets = new() { "1.1.1.1" };
        var json = c.ToJson();
        var back = MonitorConfig.FromJson(json);
        Assert.Equal(new[] { "1.1.1.1" }, back.PingTargets);
    }
}
```

- [ ] **Step 2: Uruchom test — ma się NIE skompilować/FAIL**

Run: `dotnet test --filter MonitorConfigTests`
Expected: FAIL (MonitorConfig nie istnieje).

- [ ] **Step 3: Zaimplementuj `MonitorConfig.cs`**

```csharp
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

    // Progi defektów
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
```

- [ ] **Step 4: Uruchom test — PASS**

Run: `dotnet test --filter MonitorConfigTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add MonitorConfig with JSON persistence"
```

---

### Task 5: Parser `netsh wlan show interfaces`

**Files:**
- Create: `src/WifiTester.Core/Wifi/NetshWifiParser.cs`
- Test: `tests/WifiTester.Tests/NetshWifiParserTests.cs`

- [ ] **Step 1: Napisz test z zapisanym wyjściem `netsh`**

`tests/WifiTester.Tests/NetshWifiParserTests.cs`:
```csharp
using WifiTester.Core.Models;
using WifiTester.Core.Wifi;
using Xunit;

public class NetshWifiParserTests
{
    private const string Sample = @"
There is 1 interface on the system:

    Name                   : Wi-Fi
    State                  : connected
    SSID                   : FIRMA-WIFI
    BSSID                  : a4:b1:c2:d3:e4:f5
    Radio type             : 802.11ac
    Band                   : 5 GHz
    Channel                : 36
    Signal                 : 72%
    Receive rate (Mbps)    : 433.3
    Transmit rate (Mbps)   : 433.3
";

    [Fact]
    public void Parses_connected_interface()
    {
        var ts = DateTimeOffset.UnixEpoch;
        var s = NetshWifiParser.Parse(Sample, ts);
        Assert.Equal(WifiState.Connected, s.State);
        Assert.Equal("FIRMA-WIFI", s.Ssid);
        Assert.Equal("a4:b1:c2:d3:e4:f5", s.Bssid);
        Assert.Equal(WifiBand.Band5GHz, s.Band);
        Assert.Equal(36, s.Channel);
        Assert.Equal(72, s.SignalQuality);
        Assert.Equal(433, s.TxRateMbps);
        Assert.Equal(433, s.RxRateMbps);
        // 72% -> przybliżenie dBm: -100 + 72/2 = -64
        Assert.Equal(-64, s.RssiDbm);
    }

    [Fact]
    public void Parses_disconnected_when_no_interface()
    {
        var s = NetshWifiParser.Parse("There is 1 interface on the system:\n\n    Name : Wi-Fi\n    State : disconnected\n", DateTimeOffset.UnixEpoch);
        Assert.Equal(WifiState.Disconnected, s.State);
    }
}
```

- [ ] **Step 2: Uruchom test — FAIL**

Run: `dotnet test --filter NetshWifiParserTests`
Expected: FAIL (NetshWifiParser nie istnieje).

- [ ] **Step 3: Zaimplementuj `NetshWifiParser.cs`**

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using WifiTester.Core.Models;

namespace WifiTester.Core.Wifi;

public static class NetshWifiParser
{
    public static WifiSample Parse(string netshOutput, DateTimeOffset ts)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in netshOutput.Split('\n'))
        {
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            var val = line[(idx + 1)..].Trim();
            if (key.Length > 0 && !fields.ContainsKey(key)) fields[key] = val;
        }

        string? Get(params string[] keys)
        {
            foreach (var k in keys) if (fields.TryGetValue(k, out var v)) return v;
            return null;
        }

        var stateText = Get("State") ?? "disconnected";
        var state = stateText.Equals("connected", StringComparison.OrdinalIgnoreCase)
            ? WifiState.Connected : WifiState.Disconnected;

        if (state != WifiState.Connected)
            return new WifiSample(ts, Get("Name") ?? "Wi-Fi", state,
                null, null, 0, 0, WifiBand.Unknown, 0, null, 0, 0);

        int signal = ParseInt(Get("Signal")?.TrimEnd('%'));
        int rssi = -100 + signal / 2;  // przybliżenie z procentów

        return new WifiSample(
            ts,
            Get("Name") ?? "Wi-Fi",
            state,
            Get("SSID"),
            Get("BSSID"),
            rssi,
            signal,
            ParseBand(Get("Band")),
            ParseInt(Get("Channel")),
            Get("Radio type"),
            (int)ParseDouble(Get("Transmit rate (Mbps)")),
            (int)ParseDouble(Get("Receive rate (Mbps)")));
    }

    private static WifiBand ParseBand(string? b) => b switch
    {
        not null when b.StartsWith("2.4") => WifiBand.Band24GHz,
        not null when b.StartsWith("5") => WifiBand.Band5GHz,
        not null when b.StartsWith("6") => WifiBand.Band6GHz,
        _ => WifiBand.Unknown
    };

    private static int ParseInt(string? s) =>
        int.TryParse(Regex.Match(s ?? "", @"-?\d+").Value, out var v) ? v : 0;

    private static double ParseDouble(string? s) =>
        double.TryParse(Regex.Match(s ?? "", @"-?\d+(\.\d+)?").Value,
            NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
```

- [ ] **Step 4: Uruchom test — PASS**

Run: `dotnet test --filter NetshWifiParserTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add netsh wlan output parser"
```

---

### Task 6: Repozytorium SQLite

**Files:**
- Create: `src/WifiTester.Core/Storage/Repository.cs`
- Test: `tests/WifiTester.Tests/RepositoryTests.cs`

- [ ] **Step 1: Napisz test (zapis i odczyt + retencja)**

`tests/WifiTester.Tests/RepositoryTests.cs`:
```csharp
using WifiTester.Core.Models;
using WifiTester.Core.Storage;
using Xunit;

public class RepositoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"wt_{Guid.NewGuid():N}.db");

    [Fact]
    public void Saves_and_reads_wifi_samples_in_range()
    {
        using var repo = new Repository(_path);
        var t0 = DateTimeOffset.UnixEpoch;
        repo.SaveWifiSample(new WifiSample(t0, "Wi-Fi", WifiState.Connected,
            "S", "bssid1", -60, 80, WifiBand.Band5GHz, 36, "ac", 433, 433));
        var rows = repo.GetWifiSamples(t0.AddMinutes(-1), t0.AddMinutes(1));
        Assert.Single(rows);
        Assert.Equal("bssid1", rows[0].Bssid);
    }

    [Fact]
    public void Purge_removes_old_rows()
    {
        using var repo = new Repository(_path);
        var old = DateTimeOffset.Now.AddDays(-40);
        repo.SaveLatencySample(new LatencySample(old, "8.8.8.8", 10, true));
        repo.SaveLatencySample(new LatencySample(DateTimeOffset.Now, "8.8.8.8", 10, true));
        repo.Purge(retentionDays: 30);
        var rows = repo.GetLatencySamples(DateTimeOffset.Now.AddDays(-60), DateTimeOffset.Now);
        Assert.Single(rows);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
```

- [ ] **Step 2: Uruchom test — FAIL**

Run: `dotnet test --filter RepositoryTests`
Expected: FAIL (Repository nie istnieje).

- [ ] **Step 3: Zaimplementuj `Repository.cs`**

```csharp
using Dapper;
using Microsoft.Data.Sqlite;
using WifiTester.Core.Models;

namespace WifiTester.Core.Storage;

public sealed class Repository : IDisposable
{
    private readonly SqliteConnection _conn;

    public Repository(string dbPath)
    {
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        InitSchema();
    }

    private void InitSchema()
    {
        _conn.Execute(@"
CREATE TABLE IF NOT EXISTS wifi_samples(
  ts INTEGER, iface TEXT, state INTEGER, ssid TEXT, bssid TEXT,
  rssi INTEGER, quality INTEGER, band INTEGER, channel INTEGER,
  phy TEXT, tx INTEGER, rx INTEGER);
CREATE TABLE IF NOT EXISTS wifi_events(
  ts INTEGER, type INTEGER, from_bssid TEXT, to_bssid TEXT, reason TEXT);
CREATE TABLE IF NOT EXISTS latency_samples(
  ts INTEGER, target TEXT, rtt REAL, success INTEGER);
CREATE TABLE IF NOT EXISTS throughput_samples(
  ts INTEGER, down REAL, up REAL, server TEXT);
CREATE TABLE IF NOT EXISTS defects(
  ts_start INTEGER, ts_end INTEGER, type INTEGER, severity INTEGER,
  metric REAL, threshold REAL, ap_bssid TEXT, descr TEXT);
CREATE INDEX IF NOT EXISTS ix_wifi_ts ON wifi_samples(ts);
CREATE INDEX IF NOT EXISTS ix_lat_ts ON latency_samples(ts);");
    }

    private static long U(DateTimeOffset t) => t.ToUnixTimeMilliseconds();
    private static DateTimeOffset D(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms);

    public void SaveWifiSample(WifiSample s) => _conn.Execute(
        @"INSERT INTO wifi_samples VALUES(@ts,@iface,@state,@ssid,@bssid,@rssi,@quality,@band,@channel,@phy,@tx,@rx)",
        new { ts = U(s.Timestamp), iface = s.InterfaceName, state = (int)s.State, ssid = s.Ssid,
              bssid = s.Bssid, rssi = s.RssiDbm, quality = s.SignalQuality, band = (int)s.Band,
              channel = s.Channel, phy = s.PhyType, tx = s.TxRateMbps, rx = s.RxRateMbps });

    public void SaveWifiEvent(WifiEvent e) => _conn.Execute(
        @"INSERT INTO wifi_events VALUES(@ts,@type,@f,@t,@r)",
        new { ts = U(e.Timestamp), type = (int)e.Type, f = e.FromBssid, t = e.ToBssid, r = e.Reason });

    public void SaveLatencySample(LatencySample s) => _conn.Execute(
        @"INSERT INTO latency_samples VALUES(@ts,@target,@rtt,@ok)",
        new { ts = U(s.Timestamp), s.Target, rtt = s.RttMs, ok = s.Success ? 1 : 0 });

    public void SaveThroughputSample(ThroughputSample s) => _conn.Execute(
        @"INSERT INTO throughput_samples VALUES(@ts,@d,@u,@srv)",
        new { ts = U(s.Timestamp), d = s.DownMbps, u = s.UpMbps, srv = s.Server });

    public void SaveDefect(Defect d) => _conn.Execute(
        @"INSERT INTO defects VALUES(@s,@e,@type,@sev,@m,@th,@bssid,@descr)",
        new { s = U(d.Start), e = U(d.End), type = (int)d.Type, sev = (int)d.Severity,
              m = d.MetricValue, th = d.Threshold, bssid = d.ApBssid, descr = d.Description });

    public List<WifiSample> GetWifiSamples(DateTimeOffset from, DateTimeOffset to) =>
        _conn.Query("SELECT * FROM wifi_samples WHERE ts BETWEEN @a AND @b ORDER BY ts",
            new { a = U(from), b = U(to) })
        .Select(r => new WifiSample(D((long)r.ts), (string)r.iface, (WifiState)(int)(long)r.state,
            r.ssid as string, r.bssid as string, (int)(long)r.rssi, (int)(long)r.quality,
            (WifiBand)(int)(long)r.band, (int)(long)r.channel, r.phy as string,
            (int)(long)r.tx, (int)(long)r.rx)).ToList();

    public List<LatencySample> GetLatencySamples(DateTimeOffset from, DateTimeOffset to) =>
        _conn.Query("SELECT * FROM latency_samples WHERE ts BETWEEN @a AND @b ORDER BY ts",
            new { a = U(from), b = U(to) })
        .Select(r => new LatencySample(D((long)r.ts), (string)r.target, (double)r.rtt, (long)r.success == 1))
        .ToList();

    public List<Defect> GetDefects(DateTimeOffset from, DateTimeOffset to) =>
        _conn.Query("SELECT * FROM defects WHERE ts_start BETWEEN @a AND @b ORDER BY ts_start",
            new { a = U(from), b = U(to) })
        .Select(r => new Defect(D((long)r.ts_start), D((long)r.ts_end), (DefectType)(int)(long)r.type,
            (Severity)(int)(long)r.severity, (double)r.metric, (double)r.threshold,
            r.ap_bssid as string, (string)r.descr)).ToList();

    public void Purge(int retentionDays)
    {
        var cutoff = U(DateTimeOffset.Now.AddDays(-retentionDays));
        foreach (var (tbl, col) in new[] {
            ("wifi_samples","ts"), ("wifi_events","ts"), ("latency_samples","ts"),
            ("throughput_samples","ts"), ("defects","ts_start") })
            _conn.Execute($"DELETE FROM {tbl} WHERE {col} < @c", new { c = cutoff });
    }

    public void Dispose() => _conn.Dispose();
}
```

- [ ] **Step 4: Uruchom test — PASS**

Run: `dotnet test --filter RepositoryTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add SQLite repository with retention"
```

---

### Task 7: Detektor defektów — słaby sygnał i wysoka latencja

**Files:**
- Create: `src/WifiTester.Core/Detection/DefectDetector.cs`, `tests/WifiTester.Tests/Fakes/FakeClock.cs`
- Test: `tests/WifiTester.Tests/DefectDetectorTests.cs`

- [ ] **Step 1: Utwórz `FakeClock.cs`**

```csharp
using WifiTester.Core.Abstractions;

namespace WifiTester.Tests.Fakes;

public sealed class FakeClock : IClock
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
    public void Advance(TimeSpan d) => Now += d;
}
```

- [ ] **Step 2: Napisz testy słabego sygnału i latencji**

`tests/WifiTester.Tests/DefectDetectorTests.cs`:
```csharp
using WifiTester.Core.Config;
using WifiTester.Core.Detection;
using WifiTester.Core.Models;
using WifiTester.Tests.Fakes;
using Xunit;

public class DefectDetectorTests
{
    private static (DefectDetector d, List<Defect> defects, FakeClock clock) Make()
    {
        var clock = new FakeClock();
        var det = new DefectDetector(MonitorConfig.Default(), clock);
        var captured = new List<Defect>();
        det.DefectRaised += (_, def) => captured.Add(def);
        return (det, captured, clock);
    }

    private static WifiSample Wifi(DateTimeOffset ts, int rssi, string bssid = "ap1") =>
        new(ts, "Wi-Fi", WifiState.Connected, "S", bssid, rssi, 50, WifiBand.Band5GHz, 36, "ac", 300, 300);

    [Fact]
    public void Weak_signal_sustained_raises_warning()
    {
        var (d, defects, c) = Make();
        d.OnWifiSample(Wifi(c.Now, -78));           // start słabego sygnału
        c.Advance(TimeSpan.FromSeconds(35));
        d.OnWifiSample(Wifi(c.Now, -78));           // po 35s (> 30s próg)
        Assert.Contains(defects, x => x.Type == DefectType.WeakSignal && x.Severity == Severity.Warning);
    }

    [Fact]
    public void Strong_signal_does_not_raise()
    {
        var (d, defects, c) = Make();
        d.OnWifiSample(Wifi(c.Now, -55));
        c.Advance(TimeSpan.FromSeconds(35));
        d.OnWifiSample(Wifi(c.Now, -55));
        Assert.Empty(defects);
    }

    [Fact]
    public void High_latency_raises_defect()
    {
        var (d, defects, _) = Make();
        d.OnLatencySample(new LatencySample(DateTimeOffset.UnixEpoch, "gateway", 150, true));
        Assert.Contains(defects, x => x.Type == DefectType.HighLatency);
    }
}
```

- [ ] **Step 3: Uruchom test — FAIL**

Run: `dotnet test --filter DefectDetectorTests`
Expected: FAIL (DefectDetector nie istnieje).

- [ ] **Step 4: Zaimplementuj `DefectDetector.cs` (sygnał + latencja)**

```csharp
using WifiTester.Core.Abstractions;
using WifiTester.Core.Config;
using WifiTester.Core.Models;

namespace WifiTester.Core.Detection;

public sealed class DefectDetector
{
    private readonly MonitorConfig _cfg;
    private readonly IClock _clock;

    private DateTimeOffset? _weakSince;
    private bool _weakReported;

    public event EventHandler<Defect>? DefectRaised;

    public DefectDetector(MonitorConfig cfg, IClock clock)
    {
        _cfg = cfg;
        _clock = clock;
    }

    public void OnWifiSample(WifiSample s)
    {
        if (s.State != WifiState.Connected) { _weakSince = null; _weakReported = false; return; }
        EvaluateWeakSignal(s);
    }

    private void EvaluateWeakSignal(WifiSample s)
    {
        if (s.RssiDbm <= _cfg.WeakSignalWarnDbm)
        {
            _weakSince ??= s.Timestamp;
            var sustained = (s.Timestamp - _weakSince.Value).TotalSeconds;
            if (!_weakReported && sustained >= _cfg.WeakSignalSustainSeconds)
            {
                var sev = s.RssiDbm <= _cfg.WeakSignalCriticalDbm ? Severity.Critical : Severity.Warning;
                Raise(new Defect(_weakSince.Value, s.Timestamp, DefectType.WeakSignal, sev,
                    s.RssiDbm, _cfg.WeakSignalWarnDbm, s.Bssid,
                    $"Słaby sygnał {s.RssiDbm} dBm przez {sustained:F0}s na {s.Bssid}"));
                _weakReported = true;
            }
        }
        else { _weakSince = null; _weakReported = false; }
    }

    public void OnLatencySample(LatencySample s)
    {
        if (s.Success && s.RttMs > _cfg.HighLatencyMs)
            Raise(new Defect(s.Timestamp, s.Timestamp, DefectType.HighLatency, Severity.Warning,
                s.RttMs, _cfg.HighLatencyMs, null,
                $"Wysoka latencja do {s.Target}: {s.RttMs:F0} ms"));
    }

    private void Raise(Defect d) => DefectRaised?.Invoke(this, d);
}
```

- [ ] **Step 5: Uruchom test — PASS**

Run: `dotnet test --filter DefectDetectorTests`
Expected: PASS (3 testy).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: detect weak signal and high latency defects"
```

---

### Task 8: Detektor — zrywki, roaming storm, packet loss, spadek przepustowości

**Files:**
- Modify: `src/WifiTester.Core/Detection/DefectDetector.cs`
- Modify: `tests/WifiTester.Tests/DefectDetectorTests.cs`

- [ ] **Step 1: Dopisz testy**

Dopisz do `DefectDetectorTests.cs` w klasie:
```csharp
    [Fact]
    public void Disconnect_event_raises_defect()
    {
        var (d, defects, _) = Make();
        d.OnWifiEvent(new WifiEvent(DateTimeOffset.UnixEpoch, WifiEventType.Disconnected, "ap1", null, "lost"));
        Assert.Contains(defects, x => x.Type == DefectType.Disconnect);
    }

    [Fact]
    public void Roaming_storm_raises_after_threshold()
    {
        var (d, defects, c) = Make();
        for (int i = 0; i < 4; i++)  // próg 3 w 5 min
        {
            d.OnWifiEvent(new WifiEvent(c.Now, WifiEventType.Roamed, "ap1", "ap2", null));
            c.Advance(TimeSpan.FromSeconds(30));
        }
        Assert.Contains(defects, x => x.Type == DefectType.RoamingStorm);
    }

    [Fact]
    public void Packet_loss_above_threshold_raises()
    {
        var (d, defects, c) = Make();
        // 20 próbek, 3 nieudane = 15% > 5%
        for (int i = 0; i < 20; i++)
            d.OnLatencySample(new LatencySample(c.Now, "8.8.8.8", 10, success: i >= 3));
        Assert.Contains(defects, x => x.Type == DefectType.PacketLoss);
    }

    [Fact]
    public void Throughput_below_threshold_raises()
    {
        var (d, defects, _) = Make();
        d.OnThroughputSample(new ThroughputSample(DateTimeOffset.UnixEpoch, 5, 5, "srv")); // próg 10
        Assert.Contains(defects, x => x.Type == DefectType.ThroughputDrop);
    }
```

- [ ] **Step 2: Uruchom — FAIL** (brak `OnWifiEvent`, `OnThroughputSample`, logiki)

Run: `dotnet test --filter DefectDetectorTests`
Expected: FAIL (kompilacja/asercje).

- [ ] **Step 3: Rozszerz `DefectDetector.cs`**

Dodaj pola obok istniejących:
```csharp
    private readonly Queue<DateTimeOffset> _roamTimes = new();
    private readonly Queue<bool> _pingWindow = new();
    private const int PingWindowSize = 20;
```

Dodaj metody w klasie:
```csharp
    public void OnWifiEvent(WifiEvent e)
    {
        if (e.Type == WifiEventType.Disconnected)
            Raise(new Defect(e.Timestamp, e.Timestamp, DefectType.Disconnect, Severity.Critical,
                0, 0, e.FromBssid, $"Rozłączenie z {e.FromBssid} ({e.Reason})"));

        if (e.Type == WifiEventType.Roamed)
        {
            _roamTimes.Enqueue(e.Timestamp);
            var windowStart = e.Timestamp.AddMinutes(-_cfg.RoamStormWindowMinutes);
            while (_roamTimes.Count > 0 && _roamTimes.Peek() < windowStart) _roamTimes.Dequeue();
            if (_roamTimes.Count > _cfg.RoamStormCount)
                Raise(new Defect(_roamTimes.Peek(), e.Timestamp, DefectType.RoamingStorm, Severity.Warning,
                    _roamTimes.Count, _cfg.RoamStormCount, e.ToBssid,
                    $"Roaming storm: {_roamTimes.Count} zmian AP w {_cfg.RoamStormWindowMinutes} min"));
        }
    }

    public void OnThroughputSample(ThroughputSample s)
    {
        if (s.DownMbps < _cfg.ThroughputMinDownMbps)
            Raise(new Defect(s.Timestamp, s.Timestamp, DefectType.ThroughputDrop, Severity.Warning,
                s.DownMbps, _cfg.ThroughputMinDownMbps, null,
                $"Spadek przepustowości: {s.DownMbps:F1} Mbps (próg {_cfg.ThroughputMinDownMbps})"));
    }
```

Rozszerz istniejące `OnLatencySample` o packet loss (dodaj na końcu metody):
```csharp
        _pingWindow.Enqueue(s.Success);
        while (_pingWindow.Count > PingWindowSize) _pingWindow.Dequeue();
        if (_pingWindow.Count == PingWindowSize)
        {
            var lossPct = 100.0 * _pingWindow.Count(ok => !ok) / _pingWindow.Count;
            if (lossPct > _cfg.PacketLossPercent)
                Raise(new Defect(s.Timestamp, s.Timestamp, DefectType.PacketLoss, Severity.Warning,
                    lossPct, _cfg.PacketLossPercent, null,
                    $"Utrata pakietów {lossPct:F0}% do {s.Target}"));
        }
```

- [ ] **Step 4: Uruchom — PASS**

Run: `dotnet test --filter DefectDetectorTests`
Expected: PASS (7 testów).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: detect disconnect, roaming storm, packet loss, throughput drop"
```

---

### Task 9: Sonda sieciowa (ping)

**Files:**
- Create: `src/WifiTester.Core/Probing/PingNetworkProbe.cs`
- Test: `tests/WifiTester.Tests/PingNetworkProbeTests.cs`

- [ ] **Step 1: Napisz test (localhost zawsze osiągalny)**

`tests/WifiTester.Tests/PingNetworkProbeTests.cs`:
```csharp
using WifiTester.Core.Abstractions;
using WifiTester.Core.Probing;
using Xunit;

public class PingNetworkProbeTests
{
    [Fact]
    public async Task Ping_localhost_succeeds()
    {
        INetworkProbe probe = new PingNetworkProbe();
        var s = await probe.PingAsync("127.0.0.1");
        Assert.True(s.Success);
        Assert.Equal("127.0.0.1", s.Target);
        Assert.True(s.RttMs >= 0);
    }

    [Fact]
    public async Task Ping_invalid_host_reports_failure_not_throws()
    {
        INetworkProbe probe = new PingNetworkProbe();
        var s = await probe.PingAsync("this.host.does.not.exist.invalid");
        Assert.False(s.Success);
    }
}
```

- [ ] **Step 2: Uruchom — FAIL**

Run: `dotnet test --filter PingNetworkProbeTests`
Expected: FAIL (PingNetworkProbe nie istnieje).

- [ ] **Step 3: Zaimplementuj `PingNetworkProbe.cs`**

```csharp
using System.Net.NetworkInformation;
using WifiTester.Core.Abstractions;
using WifiTester.Core.Models;

namespace WifiTester.Core.Probing;

public sealed class PingNetworkProbe : INetworkProbe
{
    public async Task<LatencySample> PingAsync(string target, CancellationToken ct = default)
    {
        var ts = DateTimeOffset.Now;
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target, 2000);
            return reply.Status == IPStatus.Success
                ? new LatencySample(ts, target, reply.RoundtripTime, true)
                : new LatencySample(ts, target, 0, false);
        }
        catch
        {
            return new LatencySample(ts, target, 0, false);
        }
    }
}
```

- [ ] **Step 4: Uruchom — PASS**

Run: `dotnet test --filter PingNetworkProbeTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add ICMP ping network probe"
```

---

### Task 10: Tester przepustowości (HTTP download)

**Files:**
- Create: `src/WifiTester.Core/Probing/HttpThroughputTester.cs`
- Test: `tests/WifiTester.Tests/HttpThroughputTesterTests.cs`

- [ ] **Step 1: Napisz test z lokalnym strumieniem (bez sieci)**

`tests/WifiTester.Tests/HttpThroughputTesterTests.cs`:
```csharp
using System.Net;
using WifiTester.Core.Probing;
using Xunit;

public class HttpThroughputTesterTests
{
    [Fact]
    public async Task Computes_positive_download_mbps_from_fake_handler()
    {
        // 1 MB ładunku przez fałszywy HttpMessageHandler
        var payload = new byte[1_000_000];
        var handler = new FakeHandler(payload);
        var tester = new HttpThroughputTester("http://test/down", new HttpClient(handler));
        var s = await tester.MeasureAsync();
        Assert.True(s.DownMbps > 0);
        Assert.Equal("http://test/down", s.Server);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly byte[] _data;
        public FakeHandler(byte[] data) => _data = data;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
               { Content = new ByteArrayContent(_data) });
    }
}
```

- [ ] **Step 2: Uruchom — FAIL**

Run: `dotnet test --filter HttpThroughputTesterTests`
Expected: FAIL (HttpThroughputTester nie istnieje).

- [ ] **Step 3: Zaimplementuj `HttpThroughputTester.cs`**

```csharp
using System.Diagnostics;
using WifiTester.Core.Abstractions;
using WifiTester.Core.Models;

namespace WifiTester.Core.Probing;

public sealed class HttpThroughputTester : IThroughputTester
{
    private readonly string _url;
    private readonly HttpClient _http;

    public HttpThroughputTester(string url, HttpClient? http = null)
    {
        _url = url;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<ThroughputSample> MeasureAsync(CancellationToken ct = default)
    {
        var ts = DateTimeOffset.Now;
        try
        {
            var sw = Stopwatch.StartNew();
            var bytes = await _http.GetByteArrayAsync(_url, ct);
            sw.Stop();
            var seconds = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
            var mbps = bytes.Length * 8.0 / 1_000_000.0 / seconds;
            return new ThroughputSample(ts, mbps, 0, _url);
        }
        catch
        {
            return new ThroughputSample(ts, 0, 0, _url);
        }
    }
}
```

- [ ] **Step 4: Uruchom — PASS**

Run: `dotnet test --filter HttpThroughputTesterTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add HTTP download throughput tester"
```

---

### Task 11: Agregacja danych do raportu

**Files:**
- Create: `src/WifiTester.Core/Reporting/ReportData.cs`
- Test: `tests/WifiTester.Tests/ReportDataTests.cs`

- [ ] **Step 1: Napisz test agregacji**

`tests/WifiTester.Tests/ReportDataTests.cs`:
```csharp
using WifiTester.Core.Models;
using WifiTester.Core.Reporting;
using Xunit;

public class ReportDataTests
{
    [Fact]
    public void Summarizes_defects_and_signal()
    {
        var t = DateTimeOffset.UnixEpoch;
        var wifi = new List<WifiSample> {
            new(t, "Wi-Fi", WifiState.Connected, "S", "ap1", -60, 70, WifiBand.Band5GHz, 36, "ac", 300, 300),
            new(t.AddSeconds(5), "Wi-Fi", WifiState.Connected, "S", "ap1", -70, 50, WifiBand.Band5GHz, 36, "ac", 200, 200),
        };
        var defects = new List<Defect> {
            new(t, t, DefectType.Disconnect, Severity.Critical, 0, 0, "ap1", "d1"),
            new(t, t, DefectType.WeakSignal, Severity.Warning, -78, -75, "ap1", "d2"),
        };
        var r = ReportData.Build(t, t.AddMinutes(1), wifi, defects);
        Assert.Equal(2, r.TotalDefects);
        Assert.Equal(1, r.DefectsByType[DefectType.Disconnect]);
        Assert.Equal(-65, r.AverageRssi);            // (-60 + -70)/2
        Assert.Equal("ap1", r.WorstAp);              // najwięcej defektów
    }
}
```

- [ ] **Step 2: Uruchom — FAIL**

Run: `dotnet test --filter ReportDataTests`
Expected: FAIL (ReportData nie istnieje).

- [ ] **Step 3: Zaimplementuj `ReportData.cs`**

```csharp
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
```

- [ ] **Step 4: Uruchom — PASS**

Run: `dotnet test --filter ReportDataTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add report data aggregation"
```

---

### Task 12: Eksport CSV

**Files:**
- Create: `src/WifiTester.Core/Reporting/CsvExporter.cs`
- Test: `tests/WifiTester.Tests/CsvExporterTests.cs`

- [ ] **Step 1: Napisz test**

`tests/WifiTester.Tests/CsvExporterTests.cs`:
```csharp
using WifiTester.Core.Models;
using WifiTester.Core.Reporting;
using Xunit;

public class CsvExporterTests
{
    [Fact]
    public void Defects_to_csv_has_header_and_row()
    {
        var defects = new List<Defect> {
            new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DefectType.Disconnect,
                Severity.Critical, 0, 0, "ap1", "Rozłączenie")
        };
        var csv = CsvExporter.DefectsToCsv(defects);
        Assert.StartsWith("Start,End,Type,Severity,Value,Threshold,Ap,Description", csv);
        Assert.Contains("Disconnect", csv);
        Assert.Contains("ap1", csv);
    }

    [Fact]
    public void Escapes_commas_in_description()
    {
        var defects = new List<Defect> {
            new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DefectType.WeakSignal,
                Severity.Warning, -78, -75, "ap1", "Słaby, sygnał")
        };
        var csv = CsvExporter.DefectsToCsv(defects);
        Assert.Contains("\"Słaby, sygnał\"", csv);
    }
}
```

- [ ] **Step 2: Uruchom — FAIL**

Run: `dotnet test --filter CsvExporterTests`
Expected: FAIL.

- [ ] **Step 3: Zaimplementuj `CsvExporter.cs`**

```csharp
using System.Globalization;
using System.Text;
using WifiTester.Core.Models;

namespace WifiTester.Core.Reporting;

public static class CsvExporter
{
    public static string DefectsToCsv(IEnumerable<Defect> defects)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Start,End,Type,Severity,Value,Threshold,Ap,Description");
        foreach (var d in defects)
            sb.AppendLine(string.Join(",",
                Esc(d.Start.ToString("o")), Esc(d.End.ToString("o")),
                Esc(d.Type.ToString()), Esc(d.Severity.ToString()),
                Esc(d.MetricValue.ToString(CultureInfo.InvariantCulture)),
                Esc(d.Threshold.ToString(CultureInfo.InvariantCulture)),
                Esc(d.ApBssid ?? ""), Esc(d.Description)));
        return sb.ToString();
    }

    private static string Esc(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
}
```

- [ ] **Step 4: Uruchom — PASS**

Run: `dotnet test --filter CsvExporterTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add CSV exporter for defects"
```

---

### Task 13: Raport HTML

**Files:**
- Create: `src/WifiTester.Core/Reporting/HtmlReportGenerator.cs`
- Test: `tests/WifiTester.Tests/HtmlReportGeneratorTests.cs`

- [ ] **Step 1: Napisz test (zawartość kluczowych pól)**

`tests/WifiTester.Tests/HtmlReportGeneratorTests.cs`:
```csharp
using WifiTester.Core.Models;
using WifiTester.Core.Reporting;
using Xunit;

public class HtmlReportGeneratorTests
{
    [Fact]
    public void Html_contains_summary_and_defect_rows()
    {
        var t = DateTimeOffset.UnixEpoch;
        var data = ReportData.Build(t, t.AddHours(1),
            new List<WifiSample>(),
            new List<Defect> {
                new(t, t, DefectType.Disconnect, Severity.Critical, 0, 0, "ap1", "Rozłączenie z ap1")
            });
        var html = HtmlReportGenerator.Generate(data);
        Assert.Contains("<html", html);
        Assert.Contains("Rozłączenie z ap1", html);
        Assert.Contains("Liczba defektów", html);
    }
}
```

- [ ] **Step 2: Uruchom — FAIL**

Run: `dotnet test --filter HtmlReportGeneratorTests`
Expected: FAIL.

- [ ] **Step 3: Zaimplementuj `HtmlReportGenerator.cs`**

```csharp
using System.Net;
using System.Text;
using WifiTester.Core.Reporting;

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
```

- [ ] **Step 4: Uruchom — PASS**

Run: `dotnet test --filter HtmlReportGeneratorTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add HTML report generator"
```

---

### Task 14: Konsolowy host (pętla monitorująca)

**Files:**
- Create: `src/WifiTester.Host/MonitorLoop.cs`, `src/WifiTester.Host/Program.cs`

> Host nie ma testów jednostkowych (spina realne IO). Weryfikacja: ręczne uruchomienie.

- [ ] **Step 1: Utwórz `MonitorLoop.cs`**

```csharp
using WifiTester.Core.Abstractions;
using WifiTester.Core.Config;
using WifiTester.Core.Detection;
using WifiTester.Core.Models;
using WifiTester.Core.Storage;

namespace WifiTester.Host;

public sealed class MonitorLoop
{
    private readonly MonitorConfig _cfg;
    private readonly IWifiSource _wifi;
    private readonly INetworkProbe _probe;
    private readonly IThroughputTester _throughput;
    private readonly Repository _repo;
    private readonly DefectDetector _detector;
    private DateTimeOffset _lastThroughput = DateTimeOffset.MinValue;

    public MonitorLoop(MonitorConfig cfg, IWifiSource wifi, INetworkProbe probe,
        IThroughputTester throughput, Repository repo, IClock clock)
    {
        _cfg = cfg; _wifi = wifi; _probe = probe; _throughput = throughput; _repo = repo;
        _detector = new DefectDetector(cfg, clock);
        _detector.DefectRaised += (_, d) => { _repo.SaveDefect(d); Console.WriteLine($"[DEFEKT] {d.Type} {d.Severity}: {d.Description}"); };
        _wifi.WifiEventRaised += (_, e) => { _repo.SaveWifiEvent(e); _detector.OnWifiEvent(e); };
    }

    public async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("WifiTester host uruchomiony. Ctrl+C aby zakończyć.");
        while (!ct.IsCancellationRequested)
        {
            var sample = _wifi.Sample();
            _repo.SaveWifiSample(sample);
            _detector.OnWifiSample(sample);

            foreach (var target in _cfg.PingTargets)
            {
                var lat = await _probe.PingAsync(target, ct);
                _repo.SaveLatencySample(lat);
                _detector.OnLatencySample(lat);
            }

            if (_cfg.ThroughputEnabled &&
                DateTimeOffset.Now - _lastThroughput > TimeSpan.FromMinutes(_cfg.ThroughputIntervalMinutes))
            {
                var tp = await _throughput.MeasureAsync(ct);
                _repo.SaveThroughputSample(tp);
                _detector.OnThroughputSample(tp);
                _lastThroughput = DateTimeOffset.Now;
            }

            _repo.Purge(_cfg.RetentionDays);
            try { await Task.Delay(TimeSpan.FromSeconds(_cfg.WifiSampleSeconds), ct); }
            catch (TaskCanceledException) { break; }
        }
    }
}
```

- [ ] **Step 2: Utwórz prowizoryczny `NetshWifiSource` w host (do czasu Planu 2)**

`src/WifiTester.Host/NetshWifiSource.cs`:
```csharp
using System.Diagnostics;
using WifiTester.Core.Abstractions;
using WifiTester.Core.Models;
using WifiTester.Core.Wifi;

namespace WifiTester.Host;

/// Tymczasowe źródło WiFi oparte o `netsh` (Plan 2 zastąpi je ManagedNativeWifi ze zdarzeniami).
public sealed class NetshWifiSource : IWifiSource
{
    public event EventHandler<WifiEvent>? WifiEventRaised;

    public WifiSample Sample()
    {
        var ts = DateTimeOffset.Now;
        try
        {
            var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            return NetshWifiParser.Parse(output, ts);
        }
        catch
        {
            return new WifiSample(ts, "Wi-Fi", WifiState.NoAdapter, null, null, 0, 0,
                WifiBand.Unknown, 0, null, 0, 0);
        }
    }
}
```

- [ ] **Step 3: Utwórz `Program.cs`**

```csharp
using WifiTester.Core.Abstractions;
using WifiTester.Core.Config;
using WifiTester.Core.Probing;
using WifiTester.Core.Storage;
using WifiTester.Host;

var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WifiTester");
Directory.CreateDirectory(dir);
var cfg = MonitorConfig.Load(Path.Combine(dir, "config.json"));
cfg.Save(Path.Combine(dir, "config.json"));

// "gateway" -> wykryj bramę domyślną
cfg.PingTargets = cfg.PingTargets
    .Select(t => t == "gateway" ? (GatewayFinder.Get() ?? "127.0.0.1") : t).ToList();

using var repo = new Repository(Path.Combine(dir, "wifitester.db"));
var loop = new MonitorLoop(cfg, new NetshWifiSource(), new PingNetworkProbe(),
    new HttpThroughputTester(cfg.ThroughputUrl), repo, new SystemClock());

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
await loop.RunAsync(cts.Token);
```

Dodaj `src/WifiTester.Host/GatewayFinder.cs`:
```csharp
using System.Net.NetworkInformation;

namespace WifiTester.Host;

public static class GatewayFinder
{
    public static string? Get() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().GatewayAddresses)
            .Select(g => g.Address?.ToString())
            .FirstOrDefault(a => !string.IsNullOrEmpty(a) && a != "0.0.0.0");
}
```

- [ ] **Step 4: Zbuduj i uruchom ręcznie (weryfikacja)**

Run:
```bash
dotnet build
dotnet run --project src/WifiTester.Host
```
Expected: „WifiTester host uruchomiony", co kilka sekund cisza lub linie `[DEFEKT] ...`. Plik `%LOCALAPPDATA%\WifiTester\wifitester.db` powstaje. Zatrzymaj Ctrl+C.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add console host with monitoring loop and netsh wifi source"
```

---

### Task 15: Komenda raportu w host

**Files:**
- Modify: `src/WifiTester.Host/Program.cs`

- [ ] **Step 1: Dodaj obsługę argumentu `report`**

Na początku `Program.cs` (po utworzeniu `repo`), zamień blok uruchamiania pętli na:
```csharp
if (args.Length > 0 && args[0] == "report")
{
    var to = DateTimeOffset.Now;
    var from = to.AddDays(1 * -1);
    var wifi = repo.GetWifiSamples(from, to);
    var defects = repo.GetDefects(from, to);
    var data = WifiTester.Core.Reporting.ReportData.Build(from, to, wifi, defects);
    var html = WifiTester.Core.Reporting.HtmlReportGenerator.Generate(data);
    var outPath = Path.Combine(dir, $"raport_{to:yyyyMMdd_HHmm}.html");
    File.WriteAllText(outPath, html);
    var csv = WifiTester.Core.Reporting.CsvExporter.DefectsToCsv(defects);
    File.WriteAllText(Path.ChangeExtension(outPath, ".csv"), csv);
    Console.WriteLine($"Raport zapisany: {outPath}");
    return;
}
```

- [ ] **Step 2: Zbuduj i przetestuj ręcznie**

Run:
```bash
dotnet run --project src/WifiTester.Host -- report
```
Expected: „Raport zapisany: ...raport_*.html" + plik HTML i CSV w `%LOCALAPPDATA%\WifiTester`.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: add report command to host"
```

---

### Task 16: Pełny przebieg testów + README

**Files:**
- Create: `README.md`

- [ ] **Step 1: Uruchom wszystkie testy**

Run: `dotnet test`
Expected: wszystkie testy PASS (Detector 7, Parser 2, Repository 2, Config 2, Ping 2, Throughput 1, ReportData 1, Csv 2, Html 1).

- [ ] **Step 2: Napisz `README.md`**

```markdown
# WifiTester — silnik monitorujący (Plan 1)

Headless agent diagnozujący problemy WiFi/internet po stronie klienta Windows.

## Uruchomienie
- Monitoring ciągły: `dotnet run --project src/WifiTester.Host`
- Generowanie raportu (ostatnia doba): `dotnet run --project src/WifiTester.Host -- report`

Dane i konfiguracja: `%LOCALAPPDATA%\WifiTester\`.

## Co wykrywa
Zrywki, roaming storm, słaby sygnał, wysoką latencję, packet loss, spadki przepustowości.

## Dalej
Plan 2 dodaje GUI (WPF dashboard), tray, alerty i implementację WLAN ze zdarzeniami.
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "docs: add README and verify full test suite"
```

---

## Self-Review (wykonany)

**Pokrycie specyfikacji:**
- Metryki: WiFi sample (Task 5/14), latencja (Task 9), przepustowość (Task 10) ✓
- Defekty: zrywki, roaming storm, słaby sygnał, latencja, packet loss, throughput (Task 7–8) ✓; „niska prędkość łącza" (LowLinkRate) jest w modelu/konfiguracji, ale reguła **świadomie odłożona** do Planu 2 (wymaga wiarygodnego tx-rate z WLAN API, nie z `netsh`) — odnotowane jako luka do nadrobienia.
- Storage SQLite + retencja (Task 6) ✓
- Konfiguracja JSON (Task 4) ✓
- Raport HTML + CSV (Task 12–13, 15) ✓
- Abstrakcje `IWifiSource`/`INetworkProbe`/`IThroughputTester` dla testowalności i pod GUI (Task 3) ✓
- Tryb „ciągły w tle" realizuje host (Task 14); „na żądanie/raport" (Task 15). Dashboard live + tray + alerty + autostart + PDF → **Plan 2**.

**Skan placeholderów:** brak TBD/TODO; każdy krok z kodem ma pełny, poprawny kod.

**Spójność typów:** `WifiSample`/`Defect`/`LatencySample`/`ThroughputSample` i sygnatury (`OnWifiSample`, `OnLatencySample`, `OnWifiEvent`, `OnThroughputSample`, `ReportData.Build`, `CsvExporter.DefectsToCsv`, `HtmlReportGenerator.Generate`) używane spójnie między taskami.

**Poza zakresem (Plan 2):** WPF dashboard, tray, alerty, autostart, ManagedNativeWifi ze zdarzeniami WLAN, raport PDF (QuestPDF), reguła LowLinkRate.
