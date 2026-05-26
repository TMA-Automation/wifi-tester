# WifiTester — specyfikacja projektu

**Data:** 2026-05-26
**Status:** zaakceptowana (do utworzenia planu implementacji)

## 1. Cel i kontekst

W firmie działa wiele punktów dostępowych (AP) firmy Fortinet (FortiAP). Istnieje
podejrzenie, że sieć WiFi **zrywa połączenia** i okresowo **muli** (spadki
przepustowości / wysoka latencja). Celem aplikacji jest **udowodnienie i
zlokalizowanie** tych defektów z perspektywy komputera użytkownika: ciągłe
zbieranie danych o jakości WiFi i połączenia internetowego oraz generowanie
czytelnych wyników i raportów defektów.

Aplikacja działa **po stronie klienta Windows** (bez integracji z FortiGate na
tym etapie). Uzupełnia logi FortiGate, dodając perspektywę realnej sesji WiFi
użytkownika i korelację w czasie (np. „o 14:32 laptop stracił połączenie z
FortiAP-3 przy RSSI −78 dBm i przeskoczył na FortiAP-1").

### Analiza rozwiązań gotowych (wykonana)
- **FortiGate** ma wbudowany WiFi Client Monitor i log WiFi Events (zrywki,
  roaming, rogue AP) — to pierwsze źródło prawdy po stronie infrastruktury.
- **PingPlotter** — latencja/jitter/packet loss w czasie; **NetSpot/Acrylic** —
  analiza eteru; **SolarWinds NPM / ManageEngine OpManager** — centralny
  monitoring AP (enterprise, kosztowne).
- Żadne nie robi w jednym miejscu **korelacji** obserwacji klienta (zrywka,
  spadek prędkości, zmiana AP) z osią czasu i raportem defektów — stąd decyzja
  o budowie własnego, lekkiego agenta.

## 2. Decyzje (ustalone z użytkownikiem)

| Obszar | Decyzja |
|---|---|
| Budować własne czy gotowe | **Własna aplikacja** (bez integracji z FortiGate na tym etapie) |
| Tryb pracy | **Oba** — ciągły monitoring w tle + podgląd na żywo i raporty na żądanie |
| Metryki / defekty | Zrywki/roaming, jakość sygnału radiowego, latencja/packet loss, przepustowość |
| Technologia | **C# / .NET** |
| Architektura | **A — aplikacja w zasobniku (tray) + autostart przy logowaniu** |
| Prezentacja wyników | Dashboard na żywo, raport HTML/PDF, eksport CSV, alerty na żywo (tray) |

### Uzasadnienie architektury A
Dla diagnozy zrywek odczuwanych przez użytkowników najważniejsza jest
perspektywa ich realnej sesji WiFi — widoczna tylko z procesu działającego w
kontekście użytkownika. Jeden `.exe` z autostartem, bez uprawnień admina,
prostota wdrożenia na wiele maszyn. Brak danych przy wylogowanym użytkowniku
jest akceptowalny (wtedy nikt nie odczuwa problemu).

## 3. Stos technologiczny

- **.NET 8 (LTS)**, C#
- **WPF** (dashboard, raporty) + ikona w zasobniku (Hardcodet.NotifyIcon.Wpf
  lub WinForms `NotifyIcon`)
- **ManagedNativeWifi** (NuGet) — opakowanie Native WiFi API: BSSID, RSSI,
  jakość sygnału, pasmo, kanał, PHY, prędkość łącza oraz zdarzenia
  połączenia/rozłączenia/roamingu w czasie rzeczywistym; `netsh wlan show
  interfaces` jako fallback dla brakujących pól
- **System.Net.NetworkInformation.Ping** — latencja/packet loss
- **HttpClient** — test przepustowości
- **SQLite** (Microsoft.Data.Sqlite + Dapper) — magazyn szeregów czasowych
- **ScottPlot** — wykresy (na żywo i w raportach)
- **QuestPDF** — raporty PDF; raport HTML generowany jako szablon

## 4. Struktura rozwiązania

Solution `WifiTester`, trzy projekty (rozdzielenie kodu zależnego od platformy
od logiki testowalnej):

- **`WifiTester.Core`** — class library: modele, interfejsy źródeł
  (`IWifiSource`, `INetworkProbe`, `IThroughputTester`), `DefectDetector`,
  `Repository`, `ReportGenerator`, `Config`. Czysty C#, w pełni testowalny.
- **`WifiTester.Windows`** — aplikacja WPF: tray agent, dashboard, okno raportów,
  ustawienia, implementacja `IWifiSource` przez ManagedNativeWifi/netsh,
  autostart.
- **`WifiTester.Tests`** — xUnit.

## 5. Moduły i przepływ danych

```
[WiFi Sampler] ─┐
[Network Probe] ─┼─► [Event Bus] ─► [Defect Detector] ─► [Storage: SQLite]
[Throughput]   ─┘                                              │
                                          [Dashboard live] ◄───┤
                                          [Reporting]      ◄───┘
```

- **WiFi Sampler** — cyklicznie (domyślnie co 5 s) odczytuje stan bieżącego
  połączenia oraz nasłuchuje zdarzeń WLAN (connect/disconnect/roam). Emituje
  `WifiSample` i `WifiEvent`. Ukryty za `IWifiSource`.
- **Network Probe** — pinguje konfigurowalne cele: brama, FortiGate/DNS,
  internet (np. 8.8.8.8/1.1.1.1). Liczy RTT, jitter, packet loss w oknie
  kroczącym. Emituje `LatencySample` per cel.
- **Throughput Tester** — test download/upload rzadko (domyślnie co 60 min) oraz
  na żądanie, by nie zajmować pasma. Cel konfigurowalny (internet lub serwer
  lokalny). Emituje `ThroughputSample`.
- **Defect Detector** — silnik reguł oceniający próbki/zdarzenia względem
  progów; tworzy rekordy `Defect`. Progi konfigurowalne.
- **Storage (Repository)** — SQLite; retencja domyślnie 30 dni (czyszczenie
  starszych rekordów).
- **Tray Agent** — hostuje pętlę w tle, ikonę zasobnika, powiadomienia o
  alertach, menu (Otwórz dashboard, Generuj raport, Wstrzymaj, Zamknij).
  Autostart przez klucz rejestru `HKCU\...\Run` (bez admina).
- **Dashboard (WPF)** — podgląd na żywo: bieżące AP/BSSID, RSSI, pasmo/kanał,
  prędkość łącza, wykres latencji, lista ostatnich defektów.
- **Reporting** — raport HTML/PDF za okres (podsumowanie defektów, wykresy,
  statystyki per AP) + eksport CSV.

Komunikacja między modułami przez wewnętrzną szynę zdarzeń (np. `Channel<T>` /
zdarzenia .NET). Samplery sterowane timerem oraz zdarzeniami WLAN.

## 6. Model danych (tabele SQLite)

- `wifi_samples`(ts, interface, ssid, bssid, rssi_dbm, signal_quality, band,
  channel, phy_type, tx_rate_mbps, rx_rate_mbps, state)
- `wifi_events`(ts, type[connected|disconnected|roamed|signal_change],
  from_bssid, to_bssid, reason, detail)
- `latency_samples`(ts, target, rtt_ms, success)
- `throughput_samples`(ts, down_mbps, up_mbps, server)
- `defects`(ts_start, ts_end, type, severity, metric_value, threshold, ap_bssid,
  description)

## 7. Reguły defektów (domyślne progi, konfigurowalne)

| Defekt | Domyślny próg |
|---|---|
| Rozłączenie | każde zdarzenie disconnect (czas trwania = do reconnect) |
| Roaming storm (latanie między AP) | > 3 roamingi w 5 min |
| Słaby sygnał | RSSI ≤ −75 dBm przez ≥ 30 s (ostrzeżenie); ≤ −82 dBm (krytyczny) |
| Wysoka latencja | RTT do bramy > 100 ms utrzymane |
| Packet loss | > 5% w oknie kroczącym |
| Spadek przepustowości | download < próg z konfiguracji |
| Niska prędkość łącza | tx rate spada poniżej progu mimo dobrego RSSI |

## 8. Konfiguracja

Plik JSON w `%LOCALAPPDATA%\WifiTester\config.json`: cele ping, interwały
próbkowania, progi defektów, ustawienia testu przepustowości (cel, częstość,
włącz/wyłącz), retencja danych, autostart on/off.

## 9. Obsługa błędów

- Brak adaptera WiFi / połączenie kablowe → tryb bez WiFi (nadal latencja i
  przepustowość), czytelny komunikat.
- Cel ping nieosiągalny → zapis jako strata pakietu, bez crasha.
- Błąd WLAN API → log + retry, degradacja do parsowania `netsh`.
- Błąd zapisu SQLite → bufor w pamięci + ponowna próba; UI nigdy się nie wywala.
- Błąd testu przepustowości → pominięcie pomiaru (zapis null).

## 10. Strategia testów

- **Testy jednostkowe `DefectDetector`** — syntetyczne ciągi próbek/zdarzeń →
  asercje powstałych defektów (najwyższa wartość).
- **Testy parsera `netsh`** — zapisane przykładowe wyjścia `netsh wlan show
  interfaces` → model.
- **Testy `Repository`** — na tymczasowej bazie SQLite (zapis/odczyt/retencja).
- **Izolacja sprzętu** — WiFi za `IWifiSource`; detektor i UI testowalne na
  fake'ach. Realna implementacja WLAN testowana manualnie.
- **Smoke-testy raportów** — generowanie HTML/PDF/CSV z danych przykładowych.

## 11. Pakowanie i wdrożenie

- `dotnet publish -c Release -r win-x64 --self-contained` → pojedynczy `.exe`.
- Dane i konfiguracja w `%LOCALAPPDATA%\WifiTester\`.
- Autostart przełączany w ustawieniach (klucz rejestru `HKCU\...\Run`, bez
  uprawnień administratora).

## 12. Poza zakresem (YAGNI na tym etapie)

- Integracja z FortiGate API / SNMP / syslog (możliwe rozszerzenie później).
- Centralna agregacja danych z wielu maszyn / serwer raportowy.
- Usługa Windows działająca przed logowaniem (architektura B).
- Profesjonalny site survey / mapy cieplne.
