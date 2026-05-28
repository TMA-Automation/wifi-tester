using WifiTester.Core.Models;
using WifiTester.Core.Wifi;
using Xunit;

public class WifiBandClassifierTests
{
    [Theory]
    [InlineData(1, WifiBand.Band24GHz)]
    [InlineData(11, WifiBand.Band24GHz)]
    [InlineData(14, WifiBand.Band24GHz)]
    [InlineData(36, WifiBand.Band5GHz)]
    [InlineData(153, WifiBand.Band5GHz)]
    [InlineData(165, WifiBand.Band5GHz)]
    [InlineData(200, WifiBand.Unknown)]
    public void Classifies_channel_to_band(int channel, WifiBand expected)
    {
        Assert.Equal(expected, WifiBandClassifier.FromChannel(channel));
    }

    [Theory]
    [InlineData(2412, WifiBand.Band24GHz)]
    [InlineData(5180, WifiBand.Band5GHz)]
    [InlineData(5955, WifiBand.Band6GHz)]
    [InlineData(0, WifiBand.Unknown)]
    public void Classifies_frequency_to_band(int mhz, WifiBand expected)
    {
        Assert.Equal(expected, WifiBandClassifier.FromFrequencyMHz(mhz));
    }
}
