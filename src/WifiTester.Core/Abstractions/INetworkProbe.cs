using WifiTester.Core.Models;

namespace WifiTester.Core.Abstractions;

public interface INetworkProbe
{
    /// Pojedynczy ping do celu.
    Task<LatencySample> PingAsync(string target, CancellationToken ct = default);
}
