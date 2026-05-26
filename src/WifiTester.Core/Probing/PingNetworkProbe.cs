using System.Net.NetworkInformation;
using WifiTester.Core.Abstractions;
using WifiTester.Core.Models;

namespace WifiTester.Core.Probing;

public sealed class PingNetworkProbe : INetworkProbe
{
    public async Task<LatencySample> PingAsync(string target, CancellationToken ct = default)
    {
        var ts = DateTimeOffset.Now;
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target, 2000);
            return reply.Status == IPStatus.Success
                ? new LatencySample(ts, target, reply.RoundtripTime, true)
                : new LatencySample(ts, target, 0, false);
        }
        catch
        {
            return new LatencySample(ts, target, 0, false);
        }
    }
}
