# WifiTester — Powłoka GUI (Plan 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dostarczyć aplikację WPF z architektury A: działa w zasobniku (tray) z autostartem, na żywo pokazuje stan WiFi i defekty (dashboard), wyświetla alerty w trayu, generuje raporty HTML/CSV/PDF i pakuje się do jednego `.exe`. Po drodze naprawia natywne źródło WLAN (wybór skojarzonego AP) z backlogu Planu 2.

**Architecture:** Nowy projekt `WifiTester.App` (net8.0-windows, WPF + WinForms NotifyIcon, WinExe). Reużywa `WifiTester.Core` (logika, `MonitoringService`, `AlertService`, raporty) i `WifiTester.Platform` (`ManagedNativeWifiSource`). Cała testowalna logika prezentacji (`DashboardState`) żyje w Core (net8.0); WPF/tray/autostart są cienką warstwą weryfikowaną ręcznie. App startuje ukryty do traya, uruchamia `MonitoringService` w tle i marshaluje zdarzenia do UI przez `Dispatcher`.

**Tech Stack:** .NET 8 (net8.0-windows), WPF, System.Windows.Forms.NotifyIcon, ManagedNativeWifi, QuestPDF, xUnit.

**Backlog z Planu 2 (adresowany tu):** `ManagedNativeWifiSource` wybierał najsilniejszy BSS zamiast skojarzonego AP; łapał wszystkie wyjątki jako `NoAdapter`. Zadanie 1 to naprawia, bo źródło wchodzi tu do realnego użycia.

---

## Struktura plików

```
src/WifiTester.Platform/
  ManagedNativeWifiSource.cs       # MODYFIKACJA: skojarzony BSSID + obsługa wyjątków
src/WifiTester.Core/
  Dashboard/DashboardState.cs      # NOWE: agregat stanu do wyświetlenia (testowalny)
src/WifiTester.App/                # NOWE (net8.0-windows, WinExe, WPF+WinForms)
  WifiTester.App.csproj
  app.manifest                     # (opcjonalnie) DPI awareness
  App.xaml / App.xaml.cs           # bootstrap: tło + tray, bez okna startowego
  TrayIcon.cs                      # NotifyIcon + menu kontekstowe
  Autostart.cs                     # przełącznik HKCU\...\Run
  AppPaths.cs                      # ścieżki %LOCALAPPDATA%\WifiTester
  Recording.cs                     # wiązanie MonitoringService -> repozytorium
  DashboardViewModel.cs            # INotifyPropertyChanged nad DashboardState
  DashboardWindow.xaml / .cs       # okno podglądu na żywo
  Reporting.cs                     # generowanie i otwieranie raportów
tests/WifiTester.Tests/
  DashboardStateTests.cs           # NOWE
```

> `WifiTester.App` (net8.0-windows) NIE jest referowane przez `WifiTester.Tests` (net8.0). Testujemy tylko `DashboardState` w Core; WPF/tray/autostart/źródło natywne — ręcznie.

---

### Task 1: Natywne źródło WLAN — skojarzony AP zamiast najsilniejszego BSS

**Files:**
- Modify: `src/WifiTester.Platform/ManagedNativeWifiSource.cs`

> Brak testów jednostkowych (net8.0-windows + sprzęt). Weryfikacja ręczna na żywym adapterze. Cel: `Sample()` zwraca BSSID/kanał AP, z którym jesteśmy SKOJARZENI, nie najsilniejszego widocznego.

- [ ] **Step 1: Potwierdź API odczytu skojarzonego połączenia**

ManagedNativeWifi 2.8.0 udostępnia dane bieżącego połączenia. Potwierdź dokładne nazwy (refleksją na pakiecie lub context7/WebFetch na `emoacht/ManagedNativeWifi`). Szukasz sposobu na odczyt **skojarzonego BSSID** dla interfejsu — np. `NativeWifi.EnumerateInterfaceConnections()` zwracające elementy z `Id` interfejsu i `Bssid`/`Bssid` bieżącego połączenia, albo `NativeWifi.GetCurrentConnection`. Zanotuj potwierdzoną nazwę w komentarzu.

- [ ] **Step 2: Zmodyfikuj `Sample()` — wybór po skojarzonym BSSID**

W `src/WifiTester.Platform/ManagedNativeWifiSource.cs` zamień blok wyboru `bss` (z komentarzem „ZNANE OGRANICZENIE" i `OrderByDescending`) na wybór po skojarzonym BSSID z fallbackiem na najsilniejszy. Użyj rzeczywistej nazwy API z kroku 1; poniżej wariant z `EnumerateInterfaceConnections`:
```csharp
            // Skojarzony BSSID bieżącego połączenia (nie najsilniejszy widoczny BSS).
            var connectedBssid = NativeWifi.EnumerateInterfaceConnections()
                .FirstOrDefault(ic => ic.Id == iface.Id)?.Bssid?.ToString();

            var candidates = NativeWifi.EnumerateBssNetworks()
                .Where(b => b.Interface.Id == iface.Id)
                .ToList();

            var bss = (connectedBssid is not null
                    ? candidates.FirstOrDefault(b => b.Bssid.ToString() == connectedBssid)
                    : null)
                ?? candidates.OrderByDescending(b => b.SignalStrength).FirstOrDefault();
```
Jeśli krok 1 wykaże inną nazwę (np. `GetCurrentConnection(iface.Id)?.AssociatedBssid`), użyj jej — kontrakt zwracanego `WifiSample` bez zmian. Zaktualizuj/usuń komentarz „ZNANE OGRANICZENIE".

- [ ] **Step 3: Popraw obsługę wyjątków**

Zamień `catch` na rozróżniający log:
```csharp
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WLAN] błąd odczytu: {ex.Message}");
            return new WifiSample(ts, "Wi-Fi", WifiState.NoAdapter, null, null, 0, 0,
                WifiBand.Unknown, 0, null, 0, 0);
        }
```

- [ ] **Step 4: Zbuduj i zweryfikuj ręcznie na sprzęcie**

Run: `dotnet build src/WifiTester.Platform` (0 błędów).
Weryfikacja: utwórz tymczasowy `.tmp_verify` (jak w Planie 2, krok 10 Zadania 7: `verify.csproj` net8.0-windows z referencją do Platform, `Program.cs` wypisujący `Sample()`), uruchom `dotnet run --project .tmp_verify`. Porównaj zwrócony BSSID/kanał z `netsh wlan show interfaces` (pole `AP BSSID` i `Channel`). **Muszą się zgadzać** (ten sam AP). Zapisz obie wartości w raporcie. Usuń `.tmp_verify`.
Expected: BSSID i kanał z natywnego źródła == z `netsh` (skojarzony AP).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "fix: natywne źródło wybiera skojarzony AP i loguje błędy WLAN"
```

---

### Task 2: DashboardState — agregat stanu do wyświetlenia (Core, TDD)

**Files:**
- Create: `src/WifiTester.Core/Dashboard/DashboardState.cs`
- Test: `tests/WifiTester.Tests/DashboardStateTests.cs`

- [ ] **Step 1: Napisz testy**

`tests/WifiTester.Tests/DashboardStateTests.cs`:
```csharp
using WifiTester.Core.Dashboard;
using WifiTester.Core.Models;
using Xunit;

public class DashboardStateTests
{
    private static WifiSample W(string? bssid, int rssi) =>
        new(DateTimeOffset.UnixEpoch, "Wi-Fi", WifiState.Connected, "TMA", bssid,
            rssi, 70, WifiBand.Band5GHz, 36, "ax", 433, 433);

    [Fact]
    public void Tracks_latest_wifi_sample()
    {
        var s = new DashboardState(recentDefectLimit: 50);
        s.OnWifiSample(W("ap1", -60));
        s.OnWifiSample(W("ap1", -65));
        Assert.Equal(-65, s.LatestWifi!.RssiDbm);
    }

    [Fact]
    public void Tracks_latest_latency_per_target()
    {
        var s = new DashboardState(recentDefectLimit: 50);
        s.OnLatency(new LatencySample(DateTimeOffset.UnixEpoch, "8.8.8.8", 20, true));
        s.OnLatency(new LatencySample(DateTimeOffset.UnixEpoch, "8.8.8.8", 30, true));
        s.OnLatency(new LatencySample(DateTimeOffset.UnixEpoch, "gateway", 2, true));
        Assert.Equal(30, s.LatestLatencyByTarget["8.8.8.8"].RttMs);
        Assert.Equal(2, s.LatestLatencyByTarget["gateway"].RttMs);
    }

    [Fact]
    public void Keeps_recent_defects_newest_first_capped()
    {
        var s = new DashboardState(recentDefectLimit: 2);
        for (int i = 0; i < 3; i++)
            s.OnDefect(new Defect(DateTimeOffset.UnixEpoch.AddSeconds(i), DateTimeOffset.UnixEpoch,
                DefectType.Disconnect, Severity.Critical, 0, 0, "ap1", $"d{i}"));
        Assert.Equal(2, s.RecentDefects.Count);
        Assert.Equal("d2", s.RecentDefects[0].Description);   // najnowszy pierwszy
        Assert.Equal("d1", s.RecentDefects[1].Description);
    }

    [Fact]
    public void Counts_total_defects()
    {
        var s = new DashboardState(recentDefectLimit: 50);
        s.OnDefect(new Defect(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            DefectType.WeakSignal, Severity.Warning, 0, 0, "ap1", "x"));
        s.OnDefect(new Defect(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            DefectType.Disconnect, Severity.Critical, 0, 0, "ap1", "y"));
        Assert.Equal(2, s.TotalDefects);
    }
}
```

- [ ] **Step 2: Uruchom — FAIL**

Run: `dotnet test --filter DashboardStateTests`
Expected: FAIL (DashboardState nie istnieje).

- [ ] **Step 3: Zaimplementuj `DashboardState.cs`**

```csharp
using WifiTester.Core.Models;

namespace WifiTester.Core.Dashboard;

/// Bieżący stan do pokazania w dashboardzie. Aktualizowany zdarzeniami MonitoringService.
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
        _recentDefects.Insert(0, d);                       // najnowszy na początku
        if (_recentDefects.Count > _recentDefectLimit)
            _recentDefects.RemoveAt(_recentDefects.Count - 1);
    }
}
```

- [ ] **Step 4: Uruchom — PASS**

Run: `dotnet test --filter DashboardStateTests`
Expected: PASS (4 testy).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: DashboardState — agregat stanu do dashboardu (testowalny)"
```

---

### Task 3: Projekt WifiTester.App + ścieżki + nagrywanie do repozytorium

**Files:**
- Create: `src/WifiTester.App/WifiTester.App.csproj`, `src/WifiTester.App/AppPaths.cs`, `src/WifiTester.App/Recording.cs`

> Brak testów (WPF/IO). Weryfikacja: build.

- [ ] **Step 1: Utwórz projekt WPF i referencje**

Run:
```bash
dotnet new wpf -n WifiTester.App -o src/WifiTester.App -f net8.0-windows
dotnet sln add src/WifiTester.App
dotnet add src/WifiTester.App reference src/WifiTester.Core src/WifiTester.Platform
```
W `src/WifiTester.App/WifiTester.App.csproj` w `<PropertyGroup>` dodaj `<UseWindowsForms>true</UseWindowsForms>` (obok istniejącego `<UseWPF>true</UseWPF>`) — potrzebne dla `NotifyIcon`. Usuń wygenerowane `MainWindow.xaml` i `MainWindow.xaml.cs` (zastąpimy własnym oknem; App nie ma okna startowego).

- [ ] **Step 2: Utwórz `AppPaths.cs`**

```csharp
namespace WifiTester.App;

internal static class AppPaths
{
    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WifiTester");

    public static string Config => Path.Combine(Dir, "config.json");
    public static string Database => Path.Combine(Dir, "wifitester.db");

    public static void Ensure() => Directory.CreateDirectory(Dir);
}
```

- [ ] **Step 3: Utwórz `Recording.cs` (wiązanie serwisu z repozytorium)**

```csharp
using WifiTester.Core.Monitoring;
using WifiTester.Core.Storage;

namespace WifiTester.App;

/// Zapisuje strumienie z MonitoringService do repozytorium + okresowy purge.
/// Logika identyczna jak w konsolowym hoście, w jednym miejscu dla App.
internal sealed class Recording
{
    private readonly Repository _repo;
    private DateTimeOffset _lastPurge = DateTimeOffset.MinValue;
    private readonly int _retentionDays;

    public Recording(Repository repo, int retentionDays)
    {
        _repo = repo;
        _retentionDays = retentionDays;
    }

    public void Attach(MonitoringService svc)
    {
        svc.WifiSampleCollected += (_, s) => _repo.SaveWifiSample(s);
        svc.WifiEventDetected += (_, e) => _repo.SaveWifiEvent(e);
        svc.LatencyCollected += (_, l) => _repo.SaveLatencySample(l);
        svc.ThroughputCollected += (_, t) => _repo.SaveThroughputSample(t);
        svc.DefectDetected += (_, d) => _repo.SaveDefect(d);
        svc.WifiSampleCollected += (_, _) =>
        {
            if (DateTimeOffset.Now - _lastPurge > TimeSpan.FromHours(1))
            {
                _repo.Purge(_retentionDays);
                _lastPurge = DateTimeOffset.Now;
            }
        };
    }
}
```

- [ ] **Step 4: Zbuduj**

Run: `dotnet build src/WifiTester.App`
Expected: 0 błędów (App jeszcze bez własnego App.xaml.cs logiki — domyślny szablon WPF startuje; to naprawimy w Zadaniu 4/5). Jeśli usunięcie MainWindow psuje `StartupUri`, przejdź od razu do Zadania 4 (App.xaml) zanim zbudujesz — wtedy build wykonaj na końcu Zadania 4.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: projekt WifiTester.App + ścieżki + Recording (wiązanie do repo)"
```

---

### Task 4: Bootstrap aplikacji + ikona w zasobniku + autostart

**Files:**
- Modify: `src/WifiTester.App/App.xaml`, `src/WifiTester.App/App.xaml.cs`
- Create: `src/WifiTester.App/TrayIcon.cs`, `src/WifiTester.App/Autostart.cs`

> Weryfikacja ręczna: aplikacja startuje do traya, menu działa, autostart wpisuje/usuwa klucz rejestru.

- [ ] **Step 1: Utwórz `Autostart.cs`**

```csharp
using Microsoft.Win32;

namespace WifiTester.App;

internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WifiTester";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is not null;
    }

    public static void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
```

- [ ] **Step 2: Utwórz `TrayIcon.cs`**

```csharp
using System.Drawing;
using System.Windows.Forms;

namespace WifiTester.App;

/// Ikona w zasobniku z menu kontekstowym. Zdarzenia wystawia jako akcje.
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _autostartItem;

    public event Action? OpenDashboardRequested;
    public event Action? GenerateReportRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        _autostartItem = new ToolStripMenuItem("Uruchamiaj przy starcie", null, (_, _) => ToggleAutostart())
        { Checked = Autostart.IsEnabled() };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Otwórz dashboard", null, (_, _) => OpenDashboardRequested?.Invoke()));
        menu.Items.Add(new ToolStripMenuItem("Generuj raport", null, (_, _) => GenerateReportRequested?.Invoke()));
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Zakończ", null, (_, _) => ExitRequested?.Invoke()));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Information,   // TODO Plan 3+: własna ikona .ico
            Visible = true,
            Text = "WifiTester",
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => OpenDashboardRequested?.Invoke();
    }

    private void ToggleAutostart()
    {
        if (Autostart.IsEnabled()) { Autostart.Disable(); _autostartItem.Checked = false; }
        else { Autostart.Enable(); _autostartItem.Checked = true; }
    }

    public void ShowAlert(string title, string message, ToolTipIcon icon)
        => _icon.ShowBalloonTip(5000, title, message, icon);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
```

- [ ] **Step 3: Zastąp `App.xaml`**

```xml
<Application x:Class="WifiTester.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
    <Application.Resources/>
</Application>
```
(Brak `StartupUri` — start do traya, zamknięcie tylko z menu.)

- [ ] **Step 4: Zastąp `App.xaml.cs` (bootstrap)**

```csharp
using System.Windows;
using System.Windows.Forms;
using WifiTester.Core.Abstractions;
using WifiTester.Core.Alerts;
using WifiTester.Core.Config;
using WifiTester.Core.Monitoring;
using WifiTester.Core.Probing;
using WifiTester.Core.Storage;
using WifiTester.Platform;

namespace WifiTester.App;

public partial class App : System.Windows.Application
{
    private TrayIcon? _tray;
    private Repository? _repo;
    private MonitoringService? _svc;
    private AlertService? _alerts;
    private DashboardWindow? _dashboard;
    private readonly CancellationTokenSource _cts = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.Ensure();

        var cfg = MonitorConfig.Load(AppPaths.Config);
        cfg.Save(AppPaths.Config);
        cfg.PingTargets = cfg.PingTargets
            .Select(t => t == "gateway" ? (GatewayFinder.Get() ?? "127.0.0.1") : t).ToList();

        _repo = new Repository(AppPaths.Database);
        var recording = new Recording(_repo, cfg.RetentionDays);

        _svc = new MonitoringService(cfg, new ManagedNativeWifiSource(),
            new PingNetworkProbe(), new HttpThroughputTester(cfg.ThroughputUrl), new SystemClock());
        recording.Attach(_svc);

        _alerts = new AlertService(cooldownSeconds: 120, new SystemClock());
        _svc.DefectDetected += (_, d) => _alerts.OnDefect(d);

        _tray = new TrayIcon();
        _tray.OpenDashboardRequested += ShowDashboard;
        _tray.GenerateReportRequested += () => Reporting.GenerateAndOpen(_repo!);
        _tray.ExitRequested += () => Shutdown();
        _alerts.AlertRaised += (_, a) => Dispatcher.Invoke(() =>
            _tray!.ShowAlert(a.Title, a.Message,
                a.Severity == Core.Models.Severity.Critical ? ToolTipIcon.Error : ToolTipIcon.Warning));

        _ = _svc.RunAsync(_cts.Token);   // pętla w tle
    }

    private void ShowDashboard()
    {
        Dispatcher.Invoke(() =>
        {
            if (_dashboard is null)
            {
                _dashboard = new DashboardWindow(_svc!);
                _dashboard.Closed += (_, _) => _dashboard = null;
            }
            _dashboard.Show();
            _dashboard.Activate();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _cts.Cancel();
        _tray?.Dispose();
        _repo?.Dispose();
        base.OnExit(e);
    }
}
```

> `GatewayFinder` jest w `WifiTester.Host`. Aby użyć go w App bez referencji host→app, **przenieś** `GatewayFinder` do Core: w Zadaniu 3 lub teraz utwórz `src/WifiTester.Core/Probing/GatewayFinder.cs` z tą samą treścią i usuń kopię z hosta (host już ma referencję do Core; zaktualizuj `using` w `Program.cs` hosta, jeśli był potrzebny — `GatewayFinder` był w namespace `WifiTester.Host`, więc po przeniesieniu do `WifiTester.Core.Probing` dodaj `using WifiTester.Core.Probing;` w `Program.cs` jeśli kompilator zgłosi brak). Zrób to teraz jako część tego kroku:
> 1. Utwórz `src/WifiTester.Core/Probing/GatewayFinder.cs` z `namespace WifiTester.Core.Probing;` i ciałem klasy `GatewayFinder` (kod jak w hoście).
> 2. Usuń `src/WifiTester.Host/GatewayFinder.cs`.
> 3. W `src/WifiTester.Host/Program.cs` upewnij się, że jest `using WifiTester.Core.Probing;` (zbuduj host, popraw `using` jeśli trzeba).

- [ ] **Step 5: Zbuduj (oczekiwane błędy o DashboardWindow/Reporting — powstaną w Zad. 5/7)**

Run: `dotnet build src/WifiTester.App`
Expected: błędy „nie znaleziono DashboardWindow / Reporting" — to OK na tym etapie; powstaną w kolejnych zadaniach. NIE commituj jeszcze, jeśli nie buduje. Jeśli chcesz mieć zielony commit teraz, tymczasowo zaślepień nie dodawaj — zamiast tego wykonaj Zadania 5 i 7, a commit Zadania 4 zrób łącznie po Zad. 5–7. (Patrz krok Commit poniżej.)

- [ ] **Step 6: Commit (po uzyskaniu zielonego builda — patrz uwaga w kroku 5)**

Gdy App buduje się po dodaniu okna (Zad. 5) i raportów (Zad. 7):
```bash
git add -A
git commit -m "feat: bootstrap App (tray, autostart, alerty, MonitoringService w tle); GatewayFinder do Core"
```

---

### Task 5: Okno dashboardu + ViewModel

**Files:**
- Create: `src/WifiTester.App/DashboardViewModel.cs`, `src/WifiTester.App/DashboardWindow.xaml`, `src/WifiTester.App/DashboardWindow.xaml.cs`

> Weryfikacja ręczna: okno pokazuje bieżące AP/RSSI/pasmo/kanał, latencje i listę ostatnich defektów, aktualizowane na żywo.

- [ ] **Step 1: Utwórz `DashboardViewModel.cs`**

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using WifiTester.Core.Dashboard;
using WifiTester.Core.Models;
using WifiTester.Core.Monitoring;

namespace WifiTester.App;

/// Cienki adapter DashboardState -> powiadomienia WPF. Aktualizowany z wątku pętli przez Dispatcher.
public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private readonly DashboardState _state = new(recentDefectLimit: 100);

    public ObservableCollection<string> Defects { get; } = new();
    public ObservableCollection<string> Latencies { get; } = new();

    public string Status { get; private set; } = "—";
    public string Ap { get; private set; } = "—";
    public string Signal { get; private set; } = "—";
    public string LinkRate { get; private set; } = "—";
    public int DefectCount { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DashboardViewModel(MonitoringService svc)
    {
        svc.WifiSampleCollected += (_, s) => OnUi(() => { _state.OnWifiSample(s); RefreshWifi(); });
        svc.LatencyCollected += (_, l) => OnUi(() => { _state.OnLatency(l); RefreshLatency(); });
        svc.DefectDetected += (_, d) => OnUi(() =>
        {
            _state.OnDefect(d);
            Defects.Insert(0, $"{d.Start:HH:mm:ss} [{d.Severity}] {d.Type}: {d.Description}");
            while (Defects.Count > 100) Defects.RemoveAt(Defects.Count - 1);
            DefectCount = _state.TotalDefects; Raise(nameof(DefectCount));
        });
    }

    private void RefreshWifi()
    {
        var w = _state.LatestWifi;
        Status = w is null ? "—" : w.State.ToString();
        Ap = w is { State: WifiState.Connected } ? $"{w.Ssid} / {w.Bssid} (kan. {w.Channel}, {w.Band})" : "—";
        Signal = w is { State: WifiState.Connected } ? $"{w.RssiDbm} dBm ({w.SignalQuality}%)" : "—";
        LinkRate = w is { State: WifiState.Connected } ? $"tx {w.TxRateMbps} / rx {w.RxRateMbps} Mbps" : "—";
        Raise(nameof(Status)); Raise(nameof(Ap)); Raise(nameof(Signal)); Raise(nameof(LinkRate));
    }

    private void RefreshLatency()
    {
        Latencies.Clear();
        foreach (var kv in _state.LatestLatencyByTarget)
            Latencies.Add(kv.Value.Success ? $"{kv.Key}: {kv.Value.RttMs:F0} ms" : $"{kv.Key}: brak odp.");
    }

    private static void OnUi(Action a) => System.Windows.Application.Current.Dispatcher.Invoke(a);
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
```

- [ ] **Step 2: Utwórz `DashboardWindow.xaml`**

```xml
<Window x:Class="WifiTester.App.DashboardWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="WifiTester — podgląd" Height="480" Width="640"
        FontFamily="Segoe UI" FontSize="13">
    <Grid Margin="14">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>
        <StackPanel Grid.Row="0">
            <TextBlock Text="Stan WiFi" FontSize="16" FontWeight="Bold" Margin="0,0,0,6"/>
            <TextBlock><Run Text="Status: "/><Run Text="{Binding Status, Mode=OneWay}"/></TextBlock>
            <TextBlock><Run Text="AP: "/><Run Text="{Binding Ap, Mode=OneWay}"/></TextBlock>
            <TextBlock><Run Text="Sygnał: "/><Run Text="{Binding Signal, Mode=OneWay}"/></TextBlock>
            <TextBlock><Run Text="Prędkość: "/><Run Text="{Binding LinkRate, Mode=OneWay}"/></TextBlock>
        </StackPanel>
        <StackPanel Grid.Row="1" Margin="0,12,0,0">
            <TextBlock Text="Latencja" FontSize="16" FontWeight="Bold" Margin="0,0,0,6"/>
            <ItemsControl ItemsSource="{Binding Latencies}"/>
        </StackPanel>
        <DockPanel Grid.Row="2" Margin="0,12,0,0">
            <TextBlock DockPanel.Dock="Top" FontSize="16" FontWeight="Bold" Margin="0,0,0,6">
                <Run Text="Defekty ("/><Run Text="{Binding DefectCount, Mode=OneWay}"/><Run Text=")"/>
            </TextBlock>
            <ListBox ItemsSource="{Binding Defects}"/>
        </DockPanel>
    </Grid>
</Window>
```

- [ ] **Step 3: Utwórz `DashboardWindow.xaml.cs`**

```csharp
using System.Windows;
using WifiTester.Core.Monitoring;

namespace WifiTester.App;

public partial class DashboardWindow : Window
{
    public DashboardWindow(MonitoringService svc)
    {
        InitializeComponent();
        DataContext = new DashboardViewModel(svc);
    }
}
```

- [ ] **Step 4: Zbuduj**

Run: `dotnet build src/WifiTester.App`
Expected: pozostaje błąd tylko o `Reporting` (Zad. 7). Reszta (DashboardWindow) się kompiluje.

- [ ] **Step 5: (commit łączony — patrz Zadanie 4 krok 6 / Zadanie 7)**

Nie commituj osobno; commit nastąpi po Zadaniu 7, gdy App buduje się w całości.

---

### Task 6: (zawarte w Zadaniu 4) Alerty wizualne

> Alerty są już podłączone w `App.xaml.cs` (Zadanie 4, krok 4): `AlertService.AlertRaised` → `_tray.ShowAlert(...)` z `ToolTipIcon.Error/Warning` przez `Dispatcher`. Osobne zadanie zbędne (YAGNI). Weryfikacja alertów następuje w ręcznym teście końcowym (Zadanie 8).

---

### Task 7: Generowanie i otwieranie raportów z traya

**Files:**
- Create: `src/WifiTester.App/Reporting.cs`

- [ ] **Step 1: Utwórz `Reporting.cs`**

```csharp
using System.Diagnostics;
using WifiTester.Core.Reporting;
using WifiTester.Core.Storage;

namespace WifiTester.App;

internal static class Reporting
{
    /// Generuje HTML+CSV+PDF za ostatnią dobę i otwiera HTML w domyślnej przeglądarce.
    public static void GenerateAndOpen(Repository repo)
    {
        var to = DateTimeOffset.Now;
        var from = to.AddDays(-1);
        var data = ReportData.Build(from, to, repo.GetWifiSamples(from, to), repo.GetDefects(from, to));

        var htmlPath = Path.Combine(AppPaths.Dir, $"raport_{to:yyyyMMdd_HHmm}.html");
        File.WriteAllText(htmlPath, HtmlReportGenerator.Generate(data));
        File.WriteAllText(Path.ChangeExtension(htmlPath, ".csv"),
            CsvExporter.DefectsToCsv(data.Defects));
        File.WriteAllBytes(Path.ChangeExtension(htmlPath, ".pdf"),
            PdfReportGenerator.Generate(data));

        Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
    }
}
```

- [ ] **Step 2: Zbuduj całą aplikację**

Run: `dotnet build`
Expected: cała solucja, w tym `WifiTester.App`, buduje się z 0 błędami. (Ostrzeżenia: dążymy do 0; jeśli pojawi się ostrzeżenie WPF o nieużywanej zmiennej, popraw.)

- [ ] **Step 3: Commit (łączny dla Zad. 4–7)**

```bash
git add -A
git commit -m "feat: dashboard na żywo, tray, alerty wizualne i raporty w aplikacji WPF"
```

---

### Task 8: Ręczna weryfikacja end-to-end + pakowanie + README

**Files:**
- Modify: `README.md`

> Weryfikacja całej aplikacji na żywo (architektura A) i zbudowanie pojedynczego `.exe`.

- [ ] **Step 1: Uruchom aplikację i sprawdź zachowanie**

Run (z twardym limitem, by nie zawisła w razie problemu — PowerShell):
```
$p = Start-Process dotnet -ArgumentList 'run','--project','src/WifiTester.App' -PassThru; Start-Sleep 20; Stop-Process -Id $p.Id -Force
```
W trakcie 20 s: sprawdź, że pojawia się ikona w zasobniku; kliknij dwukrotnie → otwiera się dashboard z bieżącym AP/RSSI/latencją; menu prawym przyciskiem ma pozycje (Otwórz dashboard, Generuj raport, Uruchamiaj przy starcie, Zakończ). Zanotuj obserwacje. (Jeśli interaktywna obserwacja niemożliwa w środowisku agenta, uruchom bez limitu w sesji użytkownika i opisz, co należy zobaczyć.)
Expected: ikona w trayu, dashboard pokazuje realny BSSID/RSSI (z ManagedNativeWifi), defekty dopisują się na żywo.

- [ ] **Step 2: Sprawdź autostart**

W menu traya kliknij „Uruchamiaj przy starcie" → sprawdź klucz:
```
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name WifiTester
```
Powinien wskazywać ścieżkę exe. Kliknij ponownie → wpis znika. Zweryfikuj oba stany.

- [ ] **Step 3: Sprawdź raport z traya**

Menu → „Generuj raport" → otwiera się HTML; w `%LOCALAPPDATA%\WifiTester` są `raport_*.html/.csv/.pdf`.

- [ ] **Step 4: Zbuduj pojedynczy plik exe**

Run:
```bash
dotnet publish src/WifiTester.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
Expected: powstaje `src/WifiTester.App/bin/Release/net8.0-windows/win-x64/publish/WifiTester.App.exe`. Uruchom ten exe (z limitem czasu jak w kroku 1) i potwierdź, że startuje do traya. Zanotuj rozmiar exe.

- [ ] **Step 5: Zaktualizuj README**

W `README.md` dodaj sekcję po „## Raporty":
```markdown
## Aplikacja GUI (zalecane uruchomienie)
`dotnet run --project src/WifiTester.App` — działa w zasobniku (tray), dashboard na żywo
(dwuklik w ikonę), alerty w trayu, „Generuj raport", przełącznik autostartu.

Pojedynczy plik wykonywalny:
`dotnet publish src/WifiTester.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`
→ `...\publish\WifiTester.App.exe`.

Konsolowy host (headless/CI) pozostaje dostępny: `dotnet run --project src/WifiTester.Host`.
```
Zmień też sekcję „## Dalej" na:
```markdown
## Status
Pełna aplikacja z architektury A: tray + autostart + dashboard + alerty + raporty (HTML/CSV/PDF),
natywne źródło WLAN (realny BSSID/RSSI skojarzonego AP).
```

- [ ] **Step 6: Pełny przebieg testów + commit**

Run: `dotnet test` (wszystkie zielone — App nie ma testów) i `dotnet build -c Release` (0 ostrzeżeń).
```bash
git add -A
git commit -m "docs: instrukcja GUI i pakowania; weryfikacja end-to-end aplikacji"
```

---

## Self-Review (wykonany)

**Pokrycie architektury A (spec) i zaległości:**
- Tray + autostart → Zadania 3–4 ✓
- Dashboard na żywo → Zadanie 5 ✓
- Alerty wizualne → wpięte w Zadaniu 4 (AlertService → tray balloon) ✓
- Raporty (HTML/CSV/PDF) z GUI → Zadanie 7 ✓
- Pakowanie do jednego `.exe` → Zadanie 8 ✓
- Natywne źródło w realnym użyciu + naprawa wyboru skojarzonego AP (backlog Planu 2) → Zadanie 1 ✓
- Testowalna logika prezentacji w Core (`DashboardState`) → Zadanie 2 ✓

**Skan placeholderów:** brak TBD/TODO blokujących; jedyny `TODO` w kodzie to kosmetyczna własna ikona traya (funkcjonalnie działa `SystemIcons.Information`). Zadania zależne od API (Zad. 1, ManagedNativeWifi connection) mają jawny krok potwierdzenia nazw + ręczną weryfikację na sprzęcie.

**Spójność typów:** `DashboardState` (OnWifiSample/OnLatency/OnDefect, LatestWifi, LatestLatencyByTarget, RecentDefects, TotalDefects) używany przez `DashboardViewModel`. `MonitoringService` eventy (WifiSampleCollected/LatencyCollected/DefectDetected) zgodne z Planem 2. `AlertService(int, IClock)` + `AlertRaised`/`Alert(Timestamp,Severity,Title,Message)` zgodne. `Repository.Get*`/`ReportData.Build`/`HtmlReportGenerator`/`CsvExporter`/`PdfReportGenerator` zgodne. `GatewayFinder` przeniesiony do `WifiTester.Core.Probing` (Zad. 4) i używany przez App i host.

**Kolejność i build:** App nie buduje się w całości aż do Zadania 7 (DashboardWindow + Reporting), dlatego commit dla Zadań 4–7 jest łączony (jawnie odnotowane w krokach). Zadania 1 i 2 są niezależne i commitowane osobno; Zadanie 8 to weryfikacja + pakowanie + README.

**TFM:** `WifiTester.App` i `WifiTester.Platform` to net8.0-windows; `WifiTester.Tests` (net8.0) nie referuje żadnego z nich — testy obejmują tylko `DashboardState` w Core.
