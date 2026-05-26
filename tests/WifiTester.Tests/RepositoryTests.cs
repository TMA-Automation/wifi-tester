using WifiTester.Core.Models;
using WifiTester.Core.Storage;
using Xunit;

public class RepositoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"wt_{Guid.NewGuid():N}.db");

    [Fact]
    public void Saves_and_reads_wifi_samples_in_range()
    {
        using var repo = new Repository(_path);
        var t0 = DateTimeOffset.UnixEpoch;
        repo.SaveWifiSample(new WifiSample(t0, "Wi-Fi", WifiState.Connected,
            "S", "bssid1", -60, 80, WifiBand.Band5GHz, 36, "ac", 433, 433));
        var rows = repo.GetWifiSamples(t0.AddMinutes(-1), t0.AddMinutes(1));
        Assert.Single(rows);
        Assert.Equal("bssid1", rows[0].Bssid);
    }

    [Fact]
    public void Purge_removes_old_rows()
    {
        using var repo = new Repository(_path);
        var old = DateTimeOffset.Now.AddDays(-40);
        repo.SaveLatencySample(new LatencySample(old, "8.8.8.8", 10, true));
        repo.SaveLatencySample(new LatencySample(DateTimeOffset.Now, "8.8.8.8", 10, true));
        repo.Purge(retentionDays: 30);
        var rows = repo.GetLatencySamples(DateTimeOffset.Now.AddDays(-60), DateTimeOffset.Now);
        Assert.Single(rows);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
