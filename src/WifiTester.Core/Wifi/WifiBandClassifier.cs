using WifiTester.Core.Models;

namespace WifiTester.Core.Wifi;

public static class WifiBandClassifier
{
    /// Klasyfikacja po częstotliwości (najpewniejsza — odróżnia 6 GHz).
    public static WifiBand FromFrequencyMHz(int mhz) => mhz switch
    {
        >= 2400 and < 2500 => WifiBand.Band24GHz,
        >= 4900 and < 5925 => WifiBand.Band5GHz,
        >= 5925 and <= 7125 => WifiBand.Band6GHz,
        _ => WifiBand.Unknown
    };

    /// Klasyfikacja po numerze kanału (gdy brak częstotliwości; nie odróżnia 6 GHz).
    public static WifiBand FromChannel(int channel) => channel switch
    {
        >= 1 and <= 14 => WifiBand.Band24GHz,
        >= 32 and <= 177 => WifiBand.Band5GHz,
        _ => WifiBand.Unknown
    };
}
