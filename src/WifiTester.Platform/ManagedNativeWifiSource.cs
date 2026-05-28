// ManagedNativeWifi 2.8.0 — API potwierdzone refleksją na zainstalowanym pakiecie:
//   NativeWifi.EnumerateInterfaces() -> InterfaceInfo { Guid Id, string Description, InterfaceState State }
//   InterfaceState.Connected — stan połączenia.
//   NativeWifi.EnumerateBssNetworks() -> IEnumerable<BssNetworkPack>
//   BssNetworkPack { InterfaceInfo Interface, NetworkIdentifier Ssid, NetworkIdentifier Bssid,
//                    PhyType PhyType, int SignalStrength (dBm), int LinkQuality (0-100),
//                    int Frequency (kHz), float Band (GHz), int Channel }
// UWAGA vs draft: RSSI to właściwość SignalStrength (nie "Rssi"); Bssid/Ssid to NetworkIdentifier.
//   Frequency jest w kHz -> /1000 = MHz dla WifiBandClassifier.FromFrequencyMHz.
using ManagedNativeWifi;
using WifiTester.Core.Abstractions;
using WifiTester.Core.Models;
using WifiTester.Core.Wifi;

namespace WifiTester.Platform;

/// Źródło WiFi oparte o Native WiFi API (realny BSSID, RSSI w dBm, pasmo z częstotliwości).
/// Zdarzenia roamingu wyprowadza nadrzędny MonitoringService przez RoamTracker.
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
            var iface = NativeWifi.EnumerateInterfaces()
                .FirstOrDefault(i => i.State == InterfaceState.Connected);
            if (iface is null)
                return new WifiSample(ts, "Wi-Fi", WifiState.Disconnected,
                    null, null, 0, 0, WifiBand.Unknown, 0, null, 0, 0);

            // ZNANE OGRANICZENIE (do naprawy w Planie 3, przed wpięciem tego źródła do hosta/GUI):
            // wybieramy najsilniejszy widoczny BSS na interfejsie, co NIE musi być AP, z którym
            // jesteśmy skojarzeni — przy wielu AP tej samej sieci może zwrócić inny BSSID/kanał.
            // Docelowo: odczytać skojarzony BSSID z WLAN_CONNECTION_ATTRIBUTES i dopasować po Bssid,
            // a najsilniejszy traktować tylko jako fallback.
            var bss = NativeWifi.EnumerateBssNetworks()
                .Where(b => b.Interface.Id == iface.Id)
                .OrderByDescending(b => b.SignalStrength)
                .FirstOrDefault();

            if (bss is null)
                return new WifiSample(ts, iface.Description ?? "Wi-Fi", WifiState.Connected,
                    null, null, 0, 0, WifiBand.Unknown, 0, null, 0, 0);

            var freqMHz = bss.Frequency / 1000; // Frequency w kHz -> MHz
            return new WifiSample(
                ts,
                iface.Description ?? "Wi-Fi",
                WifiState.Connected,
                bss.Ssid.ToString(),
                bss.Bssid.ToString(),
                bss.SignalStrength,          // RSSI w dBm
                bss.LinkQuality,             // 0-100
                WifiBandClassifier.FromFrequencyMHz(freqMHz),
                bss.Channel,
                bss.PhyType.ToString(),
                0, 0);
        }
        catch
        {
            return new WifiSample(ts, "Wi-Fi", WifiState.NoAdapter, null, null, 0, 0,
                WifiBand.Unknown, 0, null, 0, 0);
        }
    }
}
