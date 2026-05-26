using System.Net;
using WifiTester.Core.Probing;
using Xunit;

public class HttpThroughputTesterTests
{
    [Fact]
    public async Task Computes_positive_download_mbps_from_fake_handler()
    {
        var payload = new byte[1_000_000];
        var handler = new FakeHandler(payload);
        var tester = new HttpThroughputTester("http://test/down", new HttpClient(handler));
        var s = await tester.MeasureAsync();
        Assert.True(s.DownMbps > 0);
        Assert.Equal("http://test/down", s.Server);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly byte[] _data;
        public FakeHandler(byte[] data) => _data = data;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
               { Content = new ByteArrayContent(_data) });
    }
}
