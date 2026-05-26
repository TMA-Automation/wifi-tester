using System.Net.NetworkInformation;

namespace WifiTester.Host;

public static class GatewayFinder
{
    public static string? Get() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().GatewayAddresses)
            .Select(g => g.Address?.ToString())
            .FirstOrDefault(a => !string.IsNullOrEmpty(a) && a != "0.0.0.0");
}
