using System.Globalization;
using System.Text.RegularExpressions;
using WifiTester.Core.Models;

namespace WifiTester.Core.Wifi;

public static class NetshWifiParser
{
    public static WifiSample Parse(string netshOutput, DateTimeOffset ts)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in netshOutput.Split('\n'))
        {
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            var val = line[(idx + 1)..].Trim();
            if (key.Length > 0 && !fields.ContainsKey(key)) fields[key] = val;
        }

        string? Get(params string[] keys)
        {
            foreach (var k in keys) if (fields.TryGetValue(k, out var v)) return v;
            return null;
        }

        var stateText = Get("State") ?? "disconnected";
        var state = stateText.Equals("connected", StringComparison.OrdinalIgnoreCase)
            ? WifiState.Connected : WifiState.Disconnected;

        if (state != WifiState.Connected)
            return new WifiSample(ts, Get("Name") ?? "Wi-Fi", state,
                null, null, 0, 0, WifiBand.Unknown, 0, null, 0, 0);

        int signal = ParseInt(Get("Signal")?.TrimEnd('%'));
        // Windows 11 podaje realne pole "Rssi"; starsze tylko "Signal %". Użyj realnego, gdy jest.
        var realRssi = Get("Rssi");
        int rssi = realRssi is not null ? ParseInt(realRssi) : -100 + signal / 2;

        return new WifiSample(
            ts,
            Get("Name") ?? "Wi-Fi",
            state,
            Get("SSID"),
            Get("BSSID", "AP BSSID"),
            rssi,
            signal,
            ParseBand(Get("Band")),
            ParseInt(Get("Channel")),
            Get("Radio type"),
            (int)ParseDouble(Get("Transmit rate (Mbps)")),
            (int)ParseDouble(Get("Receive rate (Mbps)")));
    }

    private static WifiBand ParseBand(string? b) => b switch
    {
        not null when b.StartsWith("2.4") => WifiBand.Band24GHz,
        not null when b.StartsWith("5") => WifiBand.Band5GHz,
        not null when b.StartsWith("6") => WifiBand.Band6GHz,
        _ => WifiBand.Unknown
    };

    private static int ParseInt(string? s) =>
        int.TryParse(Regex.Match(s ?? "", @"-?\d+").Value, out var v) ? v : 0;

    private static double ParseDouble(string? s) =>
        double.TryParse(Regex.Match(s ?? "", @"-?\d+(\.\d+)?").Value,
            NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
