# WifiTester — silnik monitorujący (Plan 1)

Headless agent diagnozujący problemy WiFi/internet po stronie klienta Windows.

## Uruchomienie
- Monitoring ciągły: `dotnet run --project src/WifiTester.Host`
- Generowanie raportu (ostatnia doba): `dotnet run --project src/WifiTester.Host -- report`

Dane i konfiguracja: `%LOCALAPPDATA%\WifiTester\`.

## Co wykrywa
Zrywki, roaming (z BSSID), roaming storm, słaby sygnał (z eskalacją), wysoką latencję,
packet loss, spadki przepustowości, niską prędkość łącza mimo dobrego sygnału.

## Źródła danych
- Natywne Native WiFi API (ManagedNativeWifi) — realny BSSID i RSSI.
- Fallback `netsh wlan show interfaces` (czyta `AP BSSID` i pole `Rssi`).

## Raporty
HTML, CSV i PDF: `dotnet run --project src/WifiTester.Host -- report`.

## Aplikacja GUI (zalecane uruchomienie)
`dotnet run --project src/WifiTester.App` — działa w zasobniku (tray), dashboard na żywo
(dwuklik w ikonę), alerty w trayu, „Generuj raport", przełącznik autostartu.

Pojedynczy plik wykonywalny:
`dotnet publish src/WifiTester.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`
→ `...\publish\WifiTester.App.exe`.

Konsolowy host (headless/CI) pozostaje dostępny: `dotnet run --project src/WifiTester.Host`.

## Status
Pełna aplikacja z architektury A: tray + autostart + dashboard + alerty + raporty (HTML/CSV/PDF),
natywne źródło WLAN (realny BSSID/RSSI skojarzonego AP).
