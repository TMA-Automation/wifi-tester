using WifiTester.Core.Abstractions;

namespace WifiTester.Tests.Fakes;

public sealed class FakeClock : IClock
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
    public void Advance(TimeSpan d) => Now += d;
}
