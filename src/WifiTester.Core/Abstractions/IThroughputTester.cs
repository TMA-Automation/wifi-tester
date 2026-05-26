using WifiTester.Core.Models;

namespace WifiTester.Core.Abstractions;

public interface IThroughputTester
{
    Task<ThroughputSample> MeasureAsync(CancellationToken ct = default);
}
