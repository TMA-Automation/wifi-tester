# WifiTester — diagnostyka WiFi/internetu (TMA Automation)

Aplikacja dla Windows, która **w tle monitoruje połączenie WiFi i internet** i wykrywa
problemy typowe dla sieci z punktami dostępowymi Fortinet: zrywki, roaming, słaby sygnał,
wysoką latencję, utratę pakietów i spadki przepustowości. Działa w zasobniku systemowym
(obok zegara), pokazuje stan na żywo i potrafi wygenerować raport (HTML/CSV/PDF).

---

## Dla użytkownika końcowego (najprościej)

1. Pobierz plik **`WifiTester.App.exe`** (jeden plik, nie wymaga instalacji ani .NET).
2. Kliknij go dwukrotnie. Aplikacja **nie otwiera okna** — pojawia się **ikona WiFi w zasobniku**
   (prawy dolny róg, przy zegarze; może być pod strzałką „pokaż ukryte ikony" ˄).
3. Co możesz zrobić:
   - **Dwuklik w ikonę** → okno „podgląd na żywo": bieżący punkt dostępowy, siła sygnału,
     latencja i lista wykrytych defektów.
   - **Prawy przycisk na ikonie** → menu:
     - *Otwórz dashboard*
     - *Generuj raport* — tworzy raport za ostatnią dobę i otwiera go w przeglądarce
     - *Uruchamiaj przy starcie* — włącza/wyłącza autostart z Windows
     - *Zakończ*

Dane, konfiguracja i raporty zapisują się w `%LOCALAPPDATA%\WifiTester\`.

> **Uwaga o SmartScreen:** przy pierwszym uruchomieniu Windows może pokazać
> „Windows ochronił Twój komputer" (bo plik nie jest podpisany cyfrowo).
> Kliknij **Więcej informacji → Uruchom mimo to**.

### Kiedy zgłosić problem z siecią
Otwórz dashboard, poczekaj aż pojawią się defekty (albo wygeneruj raport) i prześlij raport
HTML/PDF z `%LOCALAPPDATA%\WifiTester\` administratorowi sieci.

---

## Co aplikacja wykrywa
Zrywki, roaming (z BSSID), roaming storm, słaby sygnał (z eskalacją), wysoką latencję,
utratę pakietów, spadki przepustowości oraz niską prędkość łącza mimo dobrego sygnału.

## Źródła danych
- Natywne Native WiFi API (ManagedNativeWifi) — realny BSSID i RSSI skojarzonego AP.
- Fallback `netsh wlan show interfaces`.

---

## Dla programisty

Wymagania: .NET 8 SDK, Windows.

```
dotnet run --project src/WifiTester.App     # aplikacja GUI (tray + dashboard)
dotnet run --project src/WifiTester.Host     # tryb konsolowy/headless (CI)
dotnet run --project src/WifiTester.Host -- report   # raport z konsoli
dotnet test                                  # testy
```

### Zbudowanie pojedynczego pliku .exe do udostępnienia

```
dotnet publish src/WifiTester.App -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

Gotowy plik: **`dist\WifiTester.App.exe`** — to jego wysyłasz innym osobom (jeden plik,
~200 MB, zawiera w sobie środowisko .NET, więc działa na czystym Windowsie bez instalacji).

> Pełna ścieżka domyślna `bin\Release\net8.0-windows\win-x64\publish\` jest tak głęboka,
> bo .NET układa wyniki wg: konfiguracja (`Release`) / framework (`net8.0-windows`) /
> platforma docelowa (`win-x64`) / `publish`. Parametr `-o dist` spłaszcza to do jednego
> folderu, łatwiejszego do skopiowania.

### Generowanie ikony
`powershell -File tools/make-icon.ps1` — odtwarza `src/WifiTester.App/wifitester.ico`.

## Status
Pełna aplikacja: tray + autostart + dashboard na żywo + alerty + raporty (HTML/CSV/PDF),
natywne źródło WLAN (realny BSSID/RSSI skojarzonego AP).
