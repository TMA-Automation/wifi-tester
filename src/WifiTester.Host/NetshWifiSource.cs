using System.Diagnostics;
using WifiTester.Core.Abstractions;
using WifiTester.Core.Models;
using WifiTester.Core.Wifi;

namespace WifiTester.Host;

/// Tymczasowe źródło WiFi oparte o `netsh` (Plan 2 zastąpi je ManagedNativeWifi ze zdarzeniami).
public sealed class NetshWifiSource : IWifiSource
{
#pragma warning disable CS0067 // event never used in this temporary source (Plan 2 adds real WLAN events)
    public event EventHandler<WifiEvent>? WifiEventRaised;
#pragma warning restore CS0067

    public WifiSample Sample()
    {
        var ts = DateTimeOffset.Now;
        try
        {
            var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            return NetshWifiParser.Parse(output, ts);
        }
        catch
        {
            return new WifiSample(ts, "Wi-Fi", WifiState.NoAdapter, null, null, 0, 0,
                WifiBand.Unknown, 0, null, 0, 0);
        }
    }
}
