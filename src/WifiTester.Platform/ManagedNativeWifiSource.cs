// ManagedNativeWifi 3.0.2 — API potwierdzone refleksją na zainstalowanym pakiecie.
// (Podniesiono z 2.8.0: wersja 2.x NIE udostępnia skojarzonego BSSID bieżącego połączenia;
//  GetCurrentConnection/CurrentConnectionInfo.Bssid pojawiło się dopiero w 3.x.)
//   NativeWifi.EnumerateInterfaces() -> InterfaceInfo { Guid Id, string Description, InterfaceState State }
//   InterfaceState.Connected — stan połączenia.
//   NativeWifi.EnumerateBssNetworks() -> IEnumerable<BssNetworkPack>
//   BssNetworkPack { InterfaceInfo InterfaceInfo, NetworkIdentifier Ssid, NetworkIdentifier Bssid,
//                    PhyType PhyType, int Rssi (dBm), int LinkQuality (0-100),
//                    int Frequency (kHz), float Band (GHz), int Channel }
// UWAGA vs draft: RSSI to właściwość Rssi; interfejs to InterfaceInfo (Interface jest [Obsolete]);
//   Bssid/Ssid to NetworkIdentifier. Frequency w kHz -> /1000 = MHz dla FromFrequencyMHz.
//   NativeWifi.GetCurrentConnection(Guid interfaceId) -> (ActionResult result, CurrentConnectionInfo value)
//   CurrentConnectionInfo { NetworkIdentifier Bssid, ... } — skojarzony BSSID bieżącego AP.
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

            // Skojarzony BSSID bieżącego połączenia (nie najsilniejszy widoczny BSS).
            // API: NativeWifi.GetCurrentConnection(Guid interfaceId)
            //   -> (ActionResult result, CurrentConnectionInfo value)
            //   CurrentConnectionInfo.Bssid : NetworkIdentifier (skojarzony BSSID bieżącego AP).
            var (connResult, connInfo) = NativeWifi.GetCurrentConnection(iface.Id);
            var connectedBssid = connResult == ActionResult.Success
                ? connInfo.Bssid?.ToString()
                : null;

            var candidates = NativeWifi.EnumerateBssNetworks()
                .Where(b => b.InterfaceInfo.Id == iface.Id)
                .ToList();

            var bss = (connectedBssid is not null
                    ? candidates.FirstOrDefault(b => string.Equals(b.Bssid.ToString(), connectedBssid, StringComparison.OrdinalIgnoreCase))
                    : null)
                ?? candidates.OrderByDescending(b => b.Rssi).FirstOrDefault();

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
                bss.Rssi,                    // RSSI w dBm
                bss.LinkQuality,             // 0-100
                WifiBandClassifier.FromFrequencyMHz(freqMHz),
                bss.Channel,
                bss.PhyType.ToString(),
                0, 0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WLAN] błąd odczytu: {ex.Message}");
            return new WifiSample(ts, "Wi-Fi", WifiState.NoAdapter, null, null, 0, 0,
                WifiBand.Unknown, 0, null, 0, 0);
        }
    }
}
