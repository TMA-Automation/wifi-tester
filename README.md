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

## Dalej
Plan 3 dodaje powłokę GUI: WPF dashboard na żywo, ikona w trayu, alerty wizualne,
autostart i pakowanie do jednego .exe.
