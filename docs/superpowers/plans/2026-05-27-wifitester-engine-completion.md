# WifiTester — Uzupełnienie silnika + natywny WLAN (Plan 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Naprawić błędy danych wykryte w weryfikacji (BSSID, RSSI, UTF-8), dodać wykrywanie roamingu na podstawie BSSID, regułę LowLinkRate, usługę monitorującą emitującą zdarzenia (zamiast `Console.WriteLine`), serwis alertów, natywne źródło WLAN przez ManagedNativeWifi (realny BSSID/RSSI) oraz raport PDF.

**Architecture:** Cała logika pozostaje w testowalnym `WifiTester.Core` (net8.0). Roaming jest wyprowadzany ze strumienia próbek przez `RoamTracker` (działa zarówno dla `netsh`, jak i ManagedNativeWifi), dzięki czemu nie zależymy od natywnych powiadomień WLAN. Kod zależny od Windows (ManagedNativeWifi) trafia do nowego projektu `WifiTester.Platform` (net8.0-windows). Konsolowy host korzysta z nowej `MonitoringService`.

**Tech Stack:** .NET 8, C#, xUnit, ManagedNativeWifi, QuestPDF, ScottPlot, Microsoft.Data.Sqlite.

**Kontekst z weryfikacji Planu 1 (dowody):** Na Windows 11 (Intel AX211) `netsh wlan show interfaces` zwraca etykietę `AP BSSID` (nie `BSSID`) oraz osobne pole `Rssi : -65`. Obecny parser gubił BSSID (zawsze NULL) i zgadywał RSSI z `Signal%`. Konsola gubiła polskie znaki. Te trzy rzeczy naprawiamy w Zadaniach 1–2.

---

## Struktura plików

```
src/WifiTester.Core/
  Wifi/NetshWifiParser.cs          # MODYFIKACJA: "AP BSSID", pole "Rssi"
  Wifi/RoamTracker.cs              # NOWE: próbki -> WifiEvent (connect/disconnect/roam)
  Wifi/WifiBandClassifier.cs       # NOWE: kanał -> WifiBand (testowalne, używane przez natywne źródło)
  Detection/DefectDetector.cs      # MODYFIKACJA: reguła LowLinkRate
  Monitoring/MonitoringService.cs  # NOWE: pętla jako serwis emitujący zdarzenia
  Alerts/Alert.cs                  # NOWE: model alertu
  Alerts/AlertService.cs           # NOWE: defekt -> alert z debounce
  Reporting/PdfReportGenerator.cs  # NOWE: QuestPDF
src/WifiTester.Platform/           # NOWE (net8.0-windows)
  WifiTester.Platform.csproj
  ManagedNativeWifiSource.cs       # IWifiSource z realnym BSSID/RSSI
src/WifiTester.Host/
  Program.cs                       # MODYFIKACJA: UTF-8, użycie MonitoringService + natywnego źródła
tests/WifiTester.Tests/
  NetshWifiParserTests.cs          # MODYFIKACJA: nowe asercje
  RoamTrackerTests.cs              # NOWE
  WifiBandClassifierTests.cs       # NOWE
  DefectDetectorTests.cs           # MODYFIKACJA: test LowLinkRate
  AlertServiceTests.cs             # NOWE
  PdfReportGeneratorTests.cs       # NOWE (smoke)
```

> Uwaga: `WifiTester.Tests` to net8.0 i NIE może referować `WifiTester.Platform` (net8.0-windows). Dlatego `ManagedNativeWifiSource` jest weryfikowany ręcznie, a cała logika testowalna (klasyfikacja pasma, roaming) żyje w Core.

---

### Task 1: Naprawa parsera netsh — BSSID i realny RSSI

**Files:**
- Modify: `src/WifiTester.Core/Wifi/NetshWifiParser.cs`
- Modify: `tests/WifiTester.Tests/NetshWifiParserTests.cs`

- [ ] **Step 1: Dopisz testy odzwierciedlające realne wyjście Windows 11**

W `tests/WifiTester.Tests/NetshWifiParserTests.cs` dodaj nowy `const` i dwa testy do klasy `NetshWifiParserTests`:
```csharp
    private const string Win11Sample = @"
There is 1 interface on the system:

    Name                   : Wi-Fi
    Description            : Intel(R) Wi-Fi 6E AX211 160MHz
    State                  : connected
    SSID                   : TMA
    AP BSSID               : 04:01:a1:24:fb:20
    Band                   : 5 GHz
    Channel                : 153
    Radio type             : 802.11ax
    Receive rate (Mbps)    : 432
    Transmit rate (Mbps)   : 1201
    Signal                 : 88%
    Rssi                   : -65
";

    [Fact]
    public void Parses_ap_bssid_label()
    {
        var s = NetshWifiParser.Parse(Win11Sample, DateTimeOffset.UnixEpoch);
        Assert.Equal("04:01:a1:24:fb:20", s.Bssid);
    }

    [Fact]
    public void Prefers_real_rssi_field_over_signal_percent()
    {
        var s = NetshWifiParser.Parse(Win11Sample, DateTimeOffset.UnixEpoch);
        Assert.Equal(-65, s.RssiDbm);   // realne pole Rssi, nie -100 + 88/2 = -56
        Assert.Equal(88, s.SignalQuality);
    }
```

- [ ] **Step 2: Uruchom — FAIL**

Run: `dotnet test --filter NetshWifiParserTests`
Expected: 2 nowe testy FAIL (Bssid null; RssiDbm -56).

- [ ] **Step 3: Zmodyfikuj parser**

W `src/WifiTester.Core/Wifi/NetshWifiParser.cs` zamień blok budujący `WifiSample` (gałąź `state == Connected`). Konkretnie zamień fragment od `int signal = ...` do `return new WifiSample(...);` na:
```csharp
        int signal = ParseInt(Get("Signal")?.TrimEnd('%'));
        // Windows 11 podaje realne pole "Rssi"; starsze tylko "Signal %". Użyj realnego, gdy jest.
        var realRssi = Get("Rssi");
        int rssi = realRssi is not null ? ParseInt(realRssi) : -100 + signal / 2;

        return new WifiSample(
            ts,
            Get("Name") ?? "Wi-Fi",
            state,
            Get("SSID"),
            Get("BSSID", "AP BSSID"),
            rssi,
            signal,
            ParseBand(Get("Band")),
            ParseInt(Get("Channel")),
            Get("Radio type"),
            (int)ParseDouble(Get("Transmit rate (Mbps)")),
            (int)ParseDouble(Get("Receive rate (Mbps)")));
```

> `Get` już przyjmuje `params string[]`, więc `Get("BSSID", "AP BSSID")` zadziała: najpierw stara etykieta, potem nowa.

- [ ] **Step 4: Uruchom — PASS (wszystkie testy parsera)**

Run: `dotnet test --filter NetshWifiParserTests`
Expected: PASS (4 testy: 2 stare + 2 nowe). Stary test `Parses_connected_interface` używa `Signal 72%` bez pola `Rssi`, więc nadal oczekuje `-64` z przybliżenia — pozostaje zielony.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "fix: parser netsh czyta AP BSSID i realne pole Rssi"
```

---

### Task 2: UTF-8 w konsoli hosta

**Files:**
- Modify: `src/WifiTester.Host/Program.cs`

> Brak testu jednostkowego (efekt konsolowy). Weryfikacja ręczna.

- [ ] **Step 1: Dodaj ustawienie kodowania na początku Program.cs**

Na samej górze `src/WifiTester.Host/Program.cs` (przed pierwszą instrukcją) dodaj:
```csharp
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
```
(Jeśli `using System.Text;` już istnieje, nie duplikuj — dodaj samą linię `Console.OutputEncoding = Encoding.UTF8;`.)

- [ ] **Step 2: Zbuduj i sprawdź ręcznie**

Run: `dotnet run --project src/WifiTester.Host`
Expected: pierwsza linia to poprawne „WifiTester host uruchomiony. Ctrl+C aby zakończyć." (polskie znaki bez zniekształceń). Zatrzymaj Ctrl+C.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "fix: poprawne polskie znaki w konsoli (UTF-8)"
```

---

### Task 3: RoamTracker — wyprowadzanie zdarzeń roamingu z próbek

**Files:**
- Create: `src/WifiTester.Core/Wifi/RoamTracker.cs`
- Test: `tests/WifiTester.Tests/RoamTrackerTests.cs`

- [ ] **Step 1: Napisz testy**

`tests/WifiTester.Tests/RoamTrackerTests.cs`:
```csharp
using WifiTester.Core.Models;
using WifiTester.Core.Wifi;
using Xunit;

public class RoamTrackerTests
{
    private static WifiSample Connected(string bssid, int sec = 0) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(sec), "Wi-Fi", WifiState.Connected,
            "S", bssid, -60, 60, WifiBand.Band5GHz, 36, "ax", 300, 300);

    private static WifiSample Disconnected(int sec) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(sec), "Wi-Fi", WifiState.Disconnected,
            null, null, 0, 0, WifiBand.Unknown, 0, null, 0, 0);

    [Fact]
    public void First_connection_emits_connected()
    {
        var t = new RoamTracker();
        var ev = t.Track(Connected("ap1"));
        Assert.NotNull(ev);
        Assert.Equal(WifiEventType.Connected, ev!.Type);
        Assert.Equal("ap1", ev.ToBssid);
    }

    [Fact]
    public void Same_bssid_emits_nothing()
    {
        var t = new RoamTracker();
        t.Track(Connected("ap1"));
        Assert.Null(t.Track(Connected("ap1", 5)));
    }

    [Fact]
    public void Bssid_change_emits_roamed_with_both_endpoints()
    {
        var t = new RoamTracker();
        t.Track(Connected("ap1"));
        var ev = t.Track(Connected("ap2", 5));
        Assert.NotNull(ev);
        Assert.Equal(WifiEventType.Roamed, ev!.Type);
        Assert.Equal("ap1", ev.FromBssid);
        Assert.Equal("ap2", ev.ToBssid);
    }

    [Fact]
    public void Transition_to_disconnected_emits_disconnected()
    {
        var t = new RoamTracker();
        t.Track(Connected("ap1"));
        var ev = t.Track(Disconnected(5));
        Assert.NotNull(ev);
        Assert.Equal(WifiEventType.Disconnected, ev!.Type);
        Assert.Equal("ap1", ev.FromBssid);
    }

    [Fact]
    public void Reconnect_after_disconnect_emits_connected()
    {
        var t = new RoamTracker();
        t.Track(Connected("ap1"));
        t.Track(Disconnected(5));
        var ev = t.Track(Connected("ap2", 10));
        Assert.NotNull(ev);
        Assert.Equal(WifiEventType.Connected, ev!.Type);
        Assert.Equal("ap2", ev.ToBssid);
    }

    [Fact]
    public void Null_bssid_while_connected_is_ignored_for_roam()
    {
        // netsh na starszym Windows mógłby nie podać BSSID — brak BSSID nie liczy się jako roaming
        var t = new RoamTracker();
        t.Track(Connected("ap1"));
        var sampleNoBssid = new WifiSample(DateTimeOffset.UnixEpoch.AddSeconds(5), "Wi-Fi",
            WifiState.Connected, "S", null, -60, 60, WifiBand.Band5GHz, 36, "ax", 300, 300);
        Assert.Null(t.Track(sampleNoBssid));
    }
}
```

- [ ] **Step 2: Uruchom — FAIL**

Run: `dotnet test --filter RoamTrackerTests`
Expected: FAIL (RoamTracker nie istnieje).

- [ ] **Step 3: Zaimplementuj `RoamTracker.cs`**

```csharp
using WifiTester.Core.Models;

namespace WifiTester.Core.Wifi;

/// Wyprowadza zdarzenia WiFi (connect/disconnect/roam) z kolejnych próbek stanu.
/// Działa niezależnie od źródła (netsh lub ManagedNativeWifi).
public sealed class RoamTracker
{
    private bool _wasConnected;
    private string? _lastBssid;

    /// Zwraca zdarzenie wynikające z przejścia stanu lub null, gdy nic się nie zmieniło.
    public WifiEvent? Track(WifiSample s)
    {
        var connected = s.State == WifiState.Connected;

        if (connected && !_wasConnected)
        {
            _wasConnected = true;
            _lastBssid = s.Bssid;
            return new WifiEvent(s.Timestamp, WifiEventType.Connected, null, s.Bssid, null);
        }

        if (!connected && _wasConnected)
        {
            var from = _lastBssid;
            _wasConnected = false;
            _lastBssid = null;
            return new WifiEvent(s.Timestamp, WifiEventType.Disconnected, from, null, null);
        }

        if (connected && _wasConnected)
        {
            // Roaming tylko gdy znamy oba BSSID i się różnią.
            if (s.Bssid is not null && _lastBssid is not null && s.Bssid != _lastBssid)
            {
                var from = _lastBssid;
                _lastBssid = s.Bssid;
                return new WifiEvent(s.Timestamp, WifiEventType.Roamed, from, s.Bssid, null);
            }
            if (s.Bssid is not null) _lastBssid = s.Bssid;
        }

        return null;
    }
}
```

- [ ] **Step 4: Uruchom — PASS**

Run: `dotnet test --filter RoamTrackerTests`
Expected: PASS (6 testów).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: RoamTracker wyprowadza zdarzenia roamingu z próbek"
```

---

### Task 4: Reguła defektu LowLinkRate

**Files:**
- Modify: `src/WifiTester.Core/Detection/DefectDetector.cs`
- Modify: `tests/WifiTester.Tests/DefectDetectorTests.cs`

- [ ] **Step 1: Dopisz testy**

Dodaj do klasy `DefectDetectorTests`:
```csharp
    [Fact]
    public void Low_link_rate_with_good_signal_raises()
    {
        var (d, defects, _) = Make();
        // tx 12 Mbps przy dobrym RSSI -55 i progu LowLinkRateMbps=24
        d.OnWifiSample(new WifiSample(DateTimeOffset.UnixEpoch, "Wi-Fi", WifiState.Connected,
            "S", "ap1", -55, 80, WifiBand.Band5GHz, 36, "ax", 12, 12));
        Assert.Contains(defects, x => x.Type == DefectType.LowLinkRate);
    }

    [Fact]
    public void Low_link_rate_with_weak_signal_does_not_raise()
    {
        var (d, defects, _) = Make();
        // niska prędkość przy słabym sygnale to nie defekt łącza — to słaby sygnał
        d.OnWifiSample(new WifiSample(DateTimeOffset.UnixEpoch, "Wi-Fi", WifiState.Connected,
            "S", "ap1", -80, 30, WifiBand.Band5GHz, 36, "ax", 12, 12));
        Assert.DoesNotContain(defects, x => x.Type == DefectType.LowLinkRate);
    }

    [Fact]
    public void Healthy_link_rate_does_not_raise()
    {
        var (d, defects, _) = Make();
        d.OnWifiSample(new WifiSample(DateTimeOffset.UnixEpoch, "Wi-Fi", WifiState.Connected,
            "S", "ap1", -55, 80, WifiBand.Band5GHz, 36, "ax", 300, 300));
        Assert.DoesNotContain(defects, x => x.Type == DefectType.LowLinkRate);
    }
```

- [ ] **Step 2: Uruchom — FAIL**

Run: `dotnet test --filter DefectDetectorTests`
Expected: 3 nowe testy FAIL.

- [ ] **Step 3: Dodaj regułę do `DefectDetector.cs`**

W metodzie `OnWifiSample`, po wywołaniu `EvaluateWeakSignal(s);`, dodaj `EvaluateLowLinkRate(s);`:
```csharp
    public void OnWifiSample(WifiSample s)
    {
        if (s.State != WifiState.Connected) { _weakSince = null; _weakReportedSeverity = null; return; }
        EvaluateWeakSignal(s);
        EvaluateLowLinkRate(s);
    }
```
Dodaj nową metodę prywatną (np. pod `EvaluateWeakSignal`):
```csharp
    private void EvaluateLowLinkRate(WifiSample s)
    {
        // Niska wynegocjowana prędkość TX mimo dobrego sygnału (RSSI lepszy niż próg ostrzeżenia).
        if (s.RssiDbm > _cfg.WeakSignalWarnDbm && s.TxRateMbps > 0 && s.TxRateMbps < _cfg.LowLinkRateMbps)
            Raise(new Defect(s.Timestamp, s.Timestamp, DefectType.LowLinkRate, Severity.Warning,
                s.TxRateMbps, _cfg.LowLinkRateMbps, s.Bssid,
                $"Niska prędkość łącza {s.TxRateMbps} Mbps przy dobrym sygnale {s.RssiDbm} dBm na {s.Bssid}"));
    }
```

- [ ] **Step 4: Uruchom — PASS**

Run: `dotnet test --filter DefectDetectorTests`
Expected: PASS (wszystkie poprzednie + 3 nowe).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: reguła defektu LowLinkRate (niska prędkość mimo dobrego sygnału)"
```

---

### Task 5: MonitoringService — pętla jako serwis emitujący zdarzenia

**Files:**
- Create: `src/WifiTester.Core/Monitoring/MonitoringService.cs`
- Modify: `src/WifiTester.Host/Program.cs`
- Delete: `src/WifiTester.Host/MonitorLoop.cs`
- Test: `tests/WifiTester.Tests/MonitoringServiceTests.cs`

> Cel: przenieść pętlę do Core, zastąpić `Console.WriteLine` zdarzeniami (żeby GUI w Planie 3 mogło je konsumować) oraz wpiąć `RoamTracker`, dzięki czemu roaming jest wykrywany także przy próbkowaniu `netsh`. Zapis do repozytorium i log konsolowy zostają po stronie hosta jako subskrybenci.

- [ ] **Step 1: Napisz test z fake'ami źródeł**

`tests/WifiTester.Tests/MonitoringServiceTests.cs`:
```csharp
using WifiTester.Core.Abstractions;
using WifiTester.Core.Config;
using WifiTester.Core.Models;
using WifiTester.Core.Monitoring;
using WifiTester.Tests.Fakes;
using Xunit;

public class MonitoringServiceTests
{
    private sealed class FixedWifi : IWifiSource
    {
        private readonly Queue<WifiSample> _samples;
        public FixedWifi(IEnumerable<WifiSample> s) => _samples = new(s);
        public event EventHandler<WifiEvent>? WifiEventRaised;  // nieużywane (zdarzenia z RoamTracker)
        public WifiSample Sample() => _samples.Count > 1 ? _samples.Dequeue() : _samples.Peek();
    }
    private sealed class NoProbe : INetworkProbe
    {
        public Task<LatencySample> PingAsync(string target, CancellationToken ct = default)
            => Task.FromResult(new LatencySample(DateTimeOffset.UnixEpoch, target, 5, true));
    }
    private sealed class NoThroughput : IThroughputTester
    {
        public Task<ThroughputSample> MeasureAsync(CancellationToken ct = default)
            => Task.FromResult(new ThroughputSample(DateTimeOffset.UnixEpoch, 100, 0, "fake"));
    }

    private static WifiSample W(string bssid) =>
        new(DateTimeOffset.UnixEpoch, "Wi-Fi", WifiState.Connected, "S", bssid,
            -60, 60, WifiBand.Band5GHz, 36, "ax", 300, 300);

    [Fact]
    public async Task Runs_one_tick_and_emits_sample_and_latency()
    {
        var cfg = MonitorConfig.Default();
        cfg.PingTargets = new() { "8.8.8.8" };
        cfg.ThroughputEnabled = false;
        var svc = new MonitoringService(cfg, new FixedWifi(new[] { W("ap1") }),
            new NoProbe(), new NoThroughput(), new FakeClock());
        var samples = new List<WifiSample>();
        var lats = new List<LatencySample>();
        svc.WifiSampleCollected += (_, s) => samples.Add(s);
        svc.LatencyCollected += (_, l) => lats.Add(l);

        await svc.RunOnceAsync(CancellationToken.None);

        Assert.Single(samples);
        Assert.Single(lats);
        Assert.Equal("ap1", samples[0].Bssid);
    }

    [Fact]
    public async Task Emits_roam_event_and_defect_on_bssid_change()
    {
        var cfg = MonitorConfig.Default();
        cfg.PingTargets = new();          // bez pingów
        cfg.ThroughputEnabled = false;
        var wifi = new FixedWifi(new[] { W("ap1"), W("ap2") });
        var svc = new MonitoringService(cfg, wifi, new NoProbe(), new NoThroughput(), new FakeClock());
        var events = new List<WifiEvent>();
        svc.WifiEventDetected += (_, e) => events.Add(e);

        await svc.RunOnceAsync(CancellationToken.None);   // ap1 -> Connected
        await svc.RunOnceAsync(CancellationToken.None);   // ap2 -> Roamed

        Assert.Contains(events, e => e.Type == WifiEventType.Connected);
        Assert.Contains(events, e => e.Type == WifiEventType.Roamed && e.ToBssid == "ap2");
    }
}
```

- [ ] **Step 2: Uruchom — FAIL**

Run: `dotnet test --filter MonitoringServiceTests`
Expected: FAIL (MonitoringService nie istnieje).

- [ ] **Step 3: Zaimplementuj `MonitoringService.cs`**

```csharp
using WifiTester.Core.Abstractions;
using WifiTester.Core.Config;
using WifiTester.Core.Detection;
using WifiTester.Core.Models;
using WifiTester.Core.Wifi;

namespace WifiTester.Core.Monitoring;

/// Pętla monitorująca jako serwis. Emituje zdarzenia; subskrybenci (host, GUI) decydują
/// co zapisać/pokazać. Roaming jest wyprowadzany ze strumienia próbek przez RoamTracker.
public sealed class MonitoringService
{
    private readonly MonitorConfig _cfg;
    private readonly IWifiSource _wifi;
    private readonly INetworkProbe _probe;
    private readonly IThroughputTester _throughput;
    private readonly DefectDetector _detector;
    private readonly RoamTracker _roam = new();
    private DateTimeOffset _lastThroughput = DateTimeOffset.MinValue;

    public event EventHandler<WifiSample>? WifiSampleCollected;
    public event EventHandler<WifiEvent>? WifiEventDetected;
    public event EventHandler<LatencySample>? LatencyCollected;
    public event EventHandler<ThroughputSample>? ThroughputCollected;
    public event EventHandler<Defect>? DefectDetected;

    public MonitoringService(MonitorConfig cfg, IWifiSource wifi, INetworkProbe probe,
        IThroughputTester throughput, IClock clock)
    {
        _cfg = cfg; _wifi = wifi; _probe = probe; _throughput = throughput;
        _detector = new DefectDetector(cfg, clock);
        _detector.DefectRaised += (_, d) => DefectDetected?.Invoke(this, d);
    }

    /// Jeden cykl pomiarowy (bez opóźnienia). Wygodne do testów i do osadzenia w pętli/timerze.
    public async Task RunOnceAsync(CancellationToken ct)
    {
        var sample = _wifi.Sample();
        WifiSampleCollected?.Invoke(this, sample);
        _detector.OnWifiSample(sample);

        var ev = _roam.Track(sample);
        if (ev is not null)
        {
            WifiEventDetected?.Invoke(this, ev);
            _detector.OnWifiEvent(ev);
        }

        foreach (var target in _cfg.PingTargets)
        {
            var lat = await _probe.PingAsync(target, ct);
            LatencyCollected?.Invoke(this, lat);
            _detector.OnLatencySample(lat);
        }

        if (_cfg.ThroughputEnabled &&
            DateTimeOffset.Now - _lastThroughput > TimeSpan.FromMinutes(_cfg.ThroughputIntervalMinutes))
        {
            var tp = await _throughput.MeasureAsync(ct);
            ThroughputCollected?.Invoke(this, tp);
            _detector.OnThroughputSample(tp);
            _lastThroughput = DateTimeOffset.Now;
        }
    }

    /// Ciągła pętla odporna na wyjątki (jeden zły cykl nie ubija agenta 24/7).
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Console.Error.WriteLine($"[BŁĄD pętli] {ex.Message}"); }

            try { await Task.Delay(TimeSpan.FromSeconds(_cfg.WifiSampleSeconds), ct); }
            catch (TaskCanceledException) { break; }
        }
    }
}
```

- [ ] **Step 4: Uruchom — PASS**

Run: `dotnet test --filter MonitoringServiceTests`
Expected: PASS (2 testy).

- [ ] **Step 5: Przepnij host na MonitoringService i usuń MonitorLoop**

Usuń plik `src/WifiTester.Host/MonitorLoop.cs`. W `src/WifiTester.Host/Program.cs` zamień blok tworzący i uruchamiający `MonitorLoop` (od `var loop = new MonitorLoop(...)` do `await loop.RunAsync(cts.Token);`) na:
```csharp
var svc = new MonitoringService(cfg, new NetshWifiSource(), new PingNetworkProbe(),
    new HttpThroughputTester(cfg.ThroughputUrl), new SystemClock());

var lastPurge = DateTimeOffset.MinValue;
svc.WifiSampleCollected += (_, s) => repo.SaveWifiSample(s);
svc.WifiEventDetected += (_, e) => repo.SaveWifiEvent(e);
svc.LatencyCollected += (_, l) => repo.SaveLatencySample(l);
svc.ThroughputCollected += (_, t) => repo.SaveThroughputSample(t);
svc.DefectDetected += (_, d) =>
{
    repo.SaveDefect(d);
    Console.WriteLine($"[DEFEKT] {d.Type} {d.Severity}: {d.Description}");
};
svc.WifiSampleCollected += (_, _) =>
{
    if (DateTimeOffset.Now - lastPurge > TimeSpan.FromHours(1))
    {
        repo.Purge(cfg.RetentionDays);
        lastPurge = DateTimeOffset.Now;
    }
};

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
await svc.RunAsync(cts.Token);
```
Dodaj na górze pliku `using WifiTester.Core.Monitoring;` (obok pozostałych `using`).

- [ ] **Step 6: Zbuduj, przetestuj, uruchom host ręcznie**

Run: `dotnet build` (0 błędów, 0 ostrzeżeń) i `dotnet test` (wszystkie zielone).
Run smoke (PowerShell, z twardym limitem czasu, by nie zawisnąć):
```
$p = Start-Process dotnet -ArgumentList 'run','--project','src/WifiTester.Host' -PassThru -NoNewWindow; Start-Sleep 12; Stop-Process -Id $p.Id -Force
```
Expected: host startuje, zapisuje próbki; brak crasha.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor: pętla jako MonitoringService w Core z RoamTrackerem; host konsumuje zdarzenia"
```

---

### Task 6: AlertService — defekt na alert z debounce

**Files:**
- Create: `src/WifiTester.Core/Alerts/Alert.cs`, `src/WifiTester.Core/Alerts/AlertService.cs`
- Test: `tests/WifiTester.Tests/AlertServiceTests.cs`

> Cel: pojedyncze, nie zalewające użytkownika powiadomienia. GUI (Plan 3) podłączy się pod `AlertRaised` i pokaże dymek w trayu. Debounce: ten sam typ defektu nie generuje alertu częściej niż co `CooldownSeconds`.

- [ ] **Step 1: Utwórz `Alert.cs`**

```csharp
using WifiTester.Core.Models;

namespace WifiTester.Core.Alerts;

public record Alert(DateTimeOffset Timestamp, Severity Severity, string Title, string Message);
```

- [ ] **Step 2: Napisz testy**

`tests/WifiTester.Tests/AlertServiceTests.cs`:
```csharp
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
```

- [ ] **Step 3: Uruchom — FAIL**

Run: `dotnet test --filter AlertServiceTests`
Expected: FAIL (AlertService nie istnieje).

- [ ] **Step 4: Zaimplementuj `AlertService.cs`**

```csharp
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
            (d.Timestamp - last).TotalSeconds < _cooldownSeconds)
            return;

        _lastAlert[d.Type] = d.Timestamp;
        AlertRaised?.Invoke(this, new Alert(d.Timestamp, d.Severity,
            $"WifiTester: {d.Type}", d.Description));
    }
}
```

- [ ] **Step 5: Uruchom — PASS**

Run: `dotnet test --filter AlertServiceTests`
Expected: PASS (4 testy).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: AlertService z debounce per typ defektu"
```

---

### Task 7: Natywne źródło WLAN (ManagedNativeWifi)

**Files:**
- Create: `src/WifiTester.Platform/WifiTester.Platform.csproj`, `src/WifiTester.Platform/ManagedNativeWifiSource.cs`
- Create: `src/WifiTester.Core/Wifi/WifiBandClassifier.cs`
- Test: `tests/WifiTester.Tests/WifiBandClassifierTests.cs`

> `ManagedNativeWifi` daje realny BSSID, RSSI (dBm), kanał, częstotliwość i PHY — bez zgadywania jak w `netsh`. Część logiki (klasyfikacja pasma z kanału) jest czysta i testowalna w Core; sam dostęp do sprzętu weryfikujemy ręcznie.

- [ ] **Step 1: Napisz testy klasyfikatora pasma**

`tests/WifiTester.Tests/WifiBandClassifierTests.cs`:
```csharp
using WifiTester.Core.Models;
using WifiTester.Core.Wifi;
using Xunit;

public class WifiBandClassifierTests
{
    [Theory]
    [InlineData(1, WifiBand.Band24GHz)]
    [InlineData(11, WifiBand.Band24GHz)]
    [InlineData(14, WifiBand.Band24GHz)]
    [InlineData(36, WifiBand.Band5GHz)]
    [InlineData(153, WifiBand.Band5GHz)]
    [InlineData(165, WifiBand.Band5GHz)]
    [InlineData(1, WifiBand.Band24GHz)]
    public void Classifies_channel_to_band(int channel, WifiBand expected)
    {
        Assert.Equal(expected, WifiBandClassifier.FromChannel(channel));
    }

    [Theory]
    [InlineData(1, WifiBand.Band6GHz)]    // 6 GHz: kanały 1..233, ale rozróżniane po częstotliwości
    public void Six_ghz_by_frequency(int channel, WifiBand expected)
    {
        // częstotliwość 5955 MHz = 6 GHz kanał 1
        Assert.Equal(expected, WifiBandClassifier.FromFrequencyMHz(5955));
    }

    [Theory]
    [InlineData(2412, WifiBand.Band24GHz)]
    [InlineData(5180, WifiBand.Band5GHz)]
    [InlineData(5955, WifiBand.Band6GHz)]
    [InlineData(0, WifiBand.Unknown)]
    public void Classifies_frequency_to_band(int mhz, WifiBand expected)
    {
        Assert.Equal(expected, WifiBandClassifier.FromFrequencyMHz(mhz));
    }
}
```

- [ ] **Step 2: Uruchom — FAIL**

Run: `dotnet test --filter WifiBandClassifierTests`
Expected: FAIL (WifiBandClassifier nie istnieje).

- [ ] **Step 3: Zaimplementuj `WifiBandClassifier.cs`**

```csharp
using WifiTester.Core.Models;

namespace WifiTester.Core.Wifi;

public static class WifiBandClassifier
{
    /// Klasyfikacja po częstotliwości (najpewniejsza — odróżnia 6 GHz).
    public static WifiBand FromFrequencyMHz(int mhz) => mhz switch
    {
        >= 2400 and < 2500 => WifiBand.Band24GHz,
        >= 4900 and < 5900 => WifiBand.Band5GHz,
        >= 5925 and <= 7125 => WifiBand.Band6GHz,
        _ => WifiBand.Unknown
    };

    /// Klasyfikacja po numerze kanału (gdy brak częstotliwości; nie odróżnia 6 GHz).
    public static WifiBand FromChannel(int channel) => channel switch
    {
        >= 1 and <= 14 => WifiBand.Band24GHz,
        >= 32 and <= 177 => WifiBand.Band5GHz,
        _ => WifiBand.Unknown
    };
}
```

- [ ] **Step 4: Uruchom — PASS**

Run: `dotnet test --filter WifiBandClassifierTests`
Expected: PASS.

- [ ] **Step 5: Commit klasyfikatora**

```bash
git add -A
git commit -m "feat: WifiBandClassifier (kanał/częstotliwość -> pasmo)"
```

- [ ] **Step 6: Utwórz projekt Platform i dodaj pakiet**

Run:
```bash
dotnet new classlib -n WifiTester.Platform -o src/WifiTester.Platform -f net8.0
dotnet sln add src/WifiTester.Platform
dotnet add src/WifiTester.Platform reference src/WifiTester.Core
dotnet add src/WifiTester.Platform package ManagedNativeWifi --version 2.*
```
Następnie ustaw TFM na Windows: w `src/WifiTester.Platform/WifiTester.Platform.csproj` zmień `<TargetFramework>net8.0</TargetFramework>` na `<TargetFramework>net8.0-windows</TargetFramework>`. Usuń `src/WifiTester.Platform/Class1.cs`.

- [ ] **Step 7: Sprawdź aktualne API ManagedNativeWifi**

Przed pisaniem źródła pobierz dokumentację (nazwy typów/metod mogły się zmienić między wersjami): użyj narzędzia context7 (`resolve-library-id` -> `query-docs` dla "ManagedNativeWifi") lub WebFetch na repozytorium NuGet/GitHub `emoacht/ManagedNativeWifi`. Potwierdź nazwy: enumeracja interfejsów, stan połączenia oraz pobranie listy BSS z polami: BSSID, RSSI (dBm), częstotliwość/kanał, PHY. Zanotuj rzeczywiste nazwy w komentarzu w pliku.

- [ ] **Step 8: Zaimplementuj `ManagedNativeWifiSource.cs`**

Implementacja oparta o `ManagedNativeWifi`. Uwaga: jeśli krok 7 wykaże inne nazwy metod, dostosuj wywołania — zachowaj zwracany kształt `WifiSample` i logikę (realny BSSID/RSSI, pasmo z częstotliwości). Poniższy kod używa publicznego API wersji 2.x:
```csharp
using ManagedNativeWifi;
using WifiTester.Core.Abstractions;
using WifiTester.Core.Models;
using WifiTester.Core.Wifi;

namespace WifiTester.Platform;

/// Źródło WiFi oparte o Native WiFi API (realny BSSID, RSSI w dBm, pasmo z częstotliwości).
/// Zdarzenia roamingu wyprowadza nadrzędny MonitoringService przez RoamTracker,
/// więc to źródło nie musi emitować WifiEventRaised.
public sealed class ManagedNativeWifiSource : IWifiSource
{
#pragma warning disable CS0067
    public event EventHandler<WifiEvent>? WifiEventRaised;
#pragma warning restore CS0067

    public WifiSample Sample()
    {
        var ts = DateTimeOffset.Now;
        try
        {
            // Interfejs w stanie połączonym.
            var iface = NativeWifi.EnumerateInterfaces()
                .FirstOrDefault(i => i.State == InterfaceState.Connected);
            if (iface is null)
                return new WifiSample(ts, "Wi-Fi", WifiState.Disconnected,
                    null, null, 0, 0, WifiBand.Unknown, 0, null, 0, 0);

            // Połączona sieć BSS z realnymi parametrami radiowymi.
            var bss = NativeWifi.EnumerateBssNetworks()
                .Where(b => b.Interface.Id == iface.Id)
                .OrderByDescending(b => b.SignalStrength)   // najsilniejszy = bieżący AP
                .FirstOrDefault();

            if (bss is null)
                return new WifiSample(ts, iface.Description ?? "Wi-Fi", WifiState.Connected,
                    null, null, 0, 0, WifiBand.Unknown, 0, null, 0, 0);

            var freqMHz = (int)(bss.Frequency / 1000);   // Frequency w kHz
            return new WifiSample(
                ts,
                iface.Description ?? "Wi-Fi",
                WifiState.Connected,
                bss.Ssid.ToString(),
                bss.Bssid.ToString(),
                bss.SignalStrength,            // RSSI w dBm
                bss.LinkQuality,               // 0-100
                WifiBandClassifier.FromFrequencyMHz(freqMHz),
                bss.Channel,
                bss.PhyType.ToString(),
                0, 0);                          // tx/rx rate: uzupełni netsh/Plan 3 (Native WiFi nie podaje wprost)
        }
        catch
        {
            return new WifiSample(ts, "Wi-Fi", WifiState.NoAdapter, null, null, 0, 0,
                WifiBand.Unknown, 0, null, 0, 0);
        }
    }
}
```

- [ ] **Step 9: Zbuduj i zweryfikuj ręcznie na realnym sprzęcie**

Run: `dotnet build src/WifiTester.Platform` (0 błędów).
Weryfikacja ręczna — napisz tymczasowy 5-liniowy program lub użyj testowego hosta, który tworzy `new ManagedNativeWifiSource().Sample()` i wypisuje wynik. Potwierdź, że `Bssid` NIE jest null i `RssiDbm` jest realne (ujemne, np. −65), na maszynie podłączonej do WiFi. Usuń tymczasowy kod po weryfikacji.
Expected: `Bssid` = MAC AP, `RssiDbm` ≈ realna wartość, `Band` poprawne.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: natywne źródło WLAN przez ManagedNativeWifi (realny BSSID/RSSI)"
```

---

### Task 8: Raport PDF (QuestPDF)

**Files:**
- Create: `src/WifiTester.Core/Reporting/PdfReportGenerator.cs`
- Test: `tests/WifiTester.Tests/PdfReportGeneratorTests.cs`

- [ ] **Step 1: Dodaj pakiet QuestPDF do Core**

Run: `dotnet add src/WifiTester.Core package QuestPDF --version 2024.*`

- [ ] **Step 2: Napisz test (smoke — produkuje niepusty PDF)**

`tests/WifiTester.Tests/PdfReportGeneratorTests.cs`:
```csharp
using WifiTester.Core.Models;
using WifiTester.Core.Reporting;
using Xunit;

public class PdfReportGeneratorTests
{
    [Fact]
    public void Generates_nonempty_pdf_with_pdf_header()
    {
        var t = DateTimeOffset.UnixEpoch;
        var data = ReportData.Build(t, t.AddHours(1),
            new List<WifiSample>(),
            new List<Defect> {
                new(t, t, DefectType.Disconnect, Severity.Critical, 0, 0, "ap1", "Rozłączenie z ap1")
            });
        var bytes = PdfReportGenerator.Generate(data);
        Assert.True(bytes.Length > 500);
        // Nagłówek pliku PDF to "%PDF"
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, bytes[..4]);
    }
}
```

- [ ] **Step 3: Uruchom — FAIL**

Run: `dotnet test --filter PdfReportGeneratorTests`
Expected: FAIL (PdfReportGenerator nie istnieje).

- [ ] **Step 4: Zaimplementuj `PdfReportGenerator.cs`**

```csharp
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
```

- [ ] **Step 5: Uruchom — PASS**

Run: `dotnet test --filter PdfReportGeneratorTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: raport PDF (QuestPDF)"
```

---

### Task 9: Komenda PDF w host + README + pełny przebieg

**Files:**
- Modify: `src/WifiTester.Host/Program.cs`
- Modify: `README.md`

- [ ] **Step 1: Dodaj zapis PDF do gałęzi `report`**

W `src/WifiTester.Host/Program.cs`, w bloku `if (args.Length > 0 && args[0] == "report")`, po zapisie CSV (`File.WriteAllText(Path.ChangeExtension(outPath, ".csv"), ...)`) dodaj:
```csharp
    File.WriteAllBytes(Path.ChangeExtension(outPath, ".pdf"), PdfReportGenerator.Generate(data));
```
Upewnij się, że na górze jest `using WifiTester.Core.Reporting;` (powinno już być).

- [ ] **Step 2: Zbuduj i przetestuj ręcznie**

Run: `dotnet run --project src/WifiTester.Host -- report`
Expected: powstają pliki `raport_*.html`, `.csv` ORAZ `.pdf` w `%LOCALAPPDATA%\WifiTester`. Otwórz PDF — zawiera podsumowanie i (jeśli są) defekty.

- [ ] **Step 3: Zaktualizuj README**

W `README.md` zamień sekcję „## Co wykrywa" oraz „## Dalej" na:
```markdown
## Co wykrywa
Zrywki, roaming (z BSSID), roaming storm, słaby sygnał (z eskalacją), wysoką latencję,
packet loss, spadki przepustowości, niską prędkość łącza mimo dobrego sygnału.

## Źródła danych
- Natywne Native WiFi API (ManagedNativeWifi) — realny BSSID i RSSI.
- Fallback `netsh wlan show interfaces` (czyta `AP BSSID` i pole `Rssi`).

## Raporty
HTML, CSV i PDF: `dotnet run --project src/WifiTester.Host -- report`.

## Dalej
Plan 3 dodaje powłokę GUI: WPF dashboard na żywo, ikona w trayu, alerty wizualne,
autostart i pakowanie do jednego .exe.
```

- [ ] **Step 4: Pełny przebieg testów**

Run: `dotnet test`
Expected: wszystkie testy zielone (Plan 1: 25 + nowe z Planu 2: parser +2, RoamTracker 6, LowLinkRate 3, MonitoringService 2, AlertService 4, WifiBandClassifier ~9, PDF 1).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: raport PDF w host; aktualizacja README; pełny zielony zestaw testów"
```

---

## Self-Review (wykonany)

**Pokrycie ustaleń z weryfikacji Planu 1:**
- BSSID `AP BSSID` → Zadanie 1 ✓
- Realny RSSI z pola `Rssi` → Zadanie 1 ✓
- UTF-8 w konsoli → Zadanie 2 ✓

**Pokrycie zaległości z Planu 1 (sekcja „Poza zakresem"):**
- Reguła LowLinkRate → Zadanie 4 ✓
- Natywny WLAN (ManagedNativeWifi) z realnymi danymi i zdarzeniami → Zadania 3 (zdarzenia z próbek przez RoamTracker, działa też dla netsh) + 7 (natywne źródło) ✓
- Raport PDF → Zadania 8–9 ✓
- WPF dashboard, tray, alerty wizualne, autostart, pakowanie → **świadomie w Planie 3**; AlertService (Zadanie 6) i MonitoringService z eventami (Zadanie 5) są fundamentem, który GUI tylko konsumuje.

**Skan placeholderów:** brak TBD/TODO; każdy krok z kodem ma pełny kod. Jedyny punkt zależny od zewnętrznej biblioteki (Zadanie 7, ManagedNativeWifi) ma jawny krok weryfikacji API (krok 7) i ręcznej weryfikacji na sprzęcie (krok 9), z zachowaniem stałego kontraktu `WifiSample`.

**Spójność typów:** `MonitoringService` używa istniejących `IWifiSource`/`INetworkProbe`/`IThroughputTester`/`IClock`, `DefectDetector` (z metodami `OnWifiSample`/`OnWifiEvent`/`OnLatencySample`/`OnThroughputSample`) i `RoamTracker.Track`. `AlertService.OnDefect` przyjmuje `Defect`. `PdfReportGenerator.Generate` przyjmuje `ReportData` (jak `HtmlReportGenerator`). Sygnatury spójne między zadaniami.

## Backlog do Planu 3 (z finalnego przeglądu kodu)

- **[Ważne] `ManagedNativeWifiSource` wybiera najsilniejszy BSS, nie skojarzony AP.** Przed wpięciem natywnego źródła do hosta/GUI należy odczytać skojarzony BSSID z `WLAN_CONNECTION_ATTRIBUTES` i dopasować `BssNetworkPack` po `Bssid` (najsilniejszy tylko jako fallback). To wyjaśnia rozjazd kanał 112 (native) vs 153 (netsh) w weryfikacji. Źródło NIE jest jeszcze używane przez hosta, więc nie wysyła błędnych danych użytkownikom.
- **[Ważne] `ManagedNativeWifiSource` łapie wszystkie wyjątki jako `NoAdapter`.** Rozróżnić „API rzuciło" od „brak adaptera"; logować do `Console.Error`.
- **[Drobne] Throughput w `MonitoringService` używa `DateTimeOffset.Now`, nie `IClock`** — gałąź interwału przepustowości jest przez to nietestowalna `FakeClock`.

**Zależności TFM:** `WifiTester.Platform` to net8.0-windows i NIE jest referowane przez net8.0-owy `WifiTester.Tests` — stąd `ManagedNativeWifiSource` tylko ręcznie; logika testowalna (`WifiBandClassifier`, `RoamTracker`) jest w Core. Host (net8.0) referuje Platform tylko jeśli ma używać natywnego źródła; jeśli Host pozostaje net8.0 (nie -windows), użycie `ManagedNativeWifiSource` wymaga zmiany TFM host na net8.0-windows — **odnotowane**: w Zadaniu 5 host nadal używa `NetshWifiSource` (działa na net8.0), a `ManagedNativeWifiSource` wejdzie do użycia w GUI (Plan 3, net8.0-windows). Host pozostaje na netsh do Planu 3.
