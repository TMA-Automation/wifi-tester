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
