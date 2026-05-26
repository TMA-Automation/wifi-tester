using WifiTester.Core.Models;

namespace WifiTester.Core.Abstractions;

public interface IWifiSource
{
    /// Odczyt bieżącego stanu połączenia WiFi (jednorazowy).
    WifiSample Sample();

    /// Zdarzenia WLAN (connect/disconnect/roam). Implementacja headless może nie emitować.
    event EventHandler<WifiEvent>? WifiEventRaised;
}
