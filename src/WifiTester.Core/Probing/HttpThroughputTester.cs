using System.Diagnostics;
using WifiTester.Core.Abstractions;
using WifiTester.Core.Models;

namespace WifiTester.Core.Probing;

public sealed class HttpThroughputTester : IThroughputTester
{
    private readonly string _url;
    private readonly HttpClient _http;

    public HttpThroughputTester(string url, HttpClient? http = null)
    {
        _url = url;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<ThroughputSample> MeasureAsync(CancellationToken ct = default)
    {
        var ts = DateTimeOffset.Now;
        try
        {
            var sw = Stopwatch.StartNew();
            var bytes = await _http.GetByteArrayAsync(_url, ct);
            sw.Stop();
            var seconds = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
            var mbps = bytes.Length * 8.0 / 1_000_000.0 / seconds;
            return new ThroughputSample(ts, mbps, 0, _url);
        }
        catch
        {
            return new ThroughputSample(ts, 0, 0, _url);
        }
    }
}
