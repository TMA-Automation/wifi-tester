using System.Net.Sockets;
using System.Net.NetworkInformation;

namespace WifiTester.Core.Probing;

/// Znajduje bramę domyślną realnego łącza (WiFi/Ethernet), pomijając adaptery
/// VPN/wirtualne (np. Hamachi 25.x, TAP, VMware). Wcześniej brano pierwszą lepszą bramę,
/// przez co cele ping trafiały na wirtualną bramę VPN i raportowały fałszywy packet loss.
public static class GatewayFinder
{
    private static readonly string[] VirtualKeywords =
    {
        "hamachi", "vpn", "tap", "tun", "virtual", "vmware", "virtualbox",
        "hyper-v", "zerotier", "tailscale", "radmin", "loopback", "bluetooth",
        "wireguard", "openvpn", "wan miniport"
    };

    public static string? Get()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .Where(n => n.NetworkInterfaceType is NetworkInterfaceType.Wireless80211
                                              or NetworkInterfaceType.Ethernet
                                              or NetworkInterfaceType.GigabitEthernet)
            .Where(n => !LooksVirtual(n))
            // Najpierw WiFi (to tester WiFi), potem Ethernet.
            .OrderByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);

        foreach (var nic in candidates)
        {
            var gw = nic.GetIPProperties().GatewayAddresses
                .Select(g => g.Address)
                .FirstOrDefault(a => a is not null
                    && a.AddressFamily == AddressFamily.InterNetwork
                    && !a.Equals(System.Net.IPAddress.Any));
            if (gw is not null) return gw.ToString();
        }
        return null;
    }

    private static bool LooksVirtual(NetworkInterface n)
    {
        var text = (n.Description + " " + n.Name).ToLowerInvariant();
        return VirtualKeywords.Any(text.Contains);
    }
}
