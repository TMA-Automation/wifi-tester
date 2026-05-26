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
