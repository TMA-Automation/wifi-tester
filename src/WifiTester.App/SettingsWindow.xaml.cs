using System.Globalization;
using System.Windows;
using WifiTester.Core.Config;

namespace WifiTester.App;

public partial class SettingsWindow : Window
{
    private readonly string _configPath;
    private readonly MonitorConfig _cfg;

    public SettingsWindow(string configPath)
    {
        InitializeComponent();
        _configPath = configPath;
        _cfg = MonitorConfig.Load(configPath);

        PingTargetsBox.Text = string.Join(", ", _cfg.PingTargets);
        LatencyBox.Text = _cfg.HighLatencyMs.ToString(CultureInfo.InvariantCulture);
        LossBox.Text = _cfg.PacketLossPercent.ToString(CultureInfo.InvariantCulture);
        WeakBox.Text = _cfg.WeakSignalWarnDbm.ToString(CultureInfo.InvariantCulture);
        RetentionBox.Text = _cfg.RetentionDays.ToString(CultureInfo.InvariantCulture);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _cfg.PingTargets = PingTargetsBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (double.TryParse(LatencyBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var lat)) _cfg.HighLatencyMs = lat;
        if (double.TryParse(LossBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var loss)) _cfg.PacketLossPercent = loss;
        if (int.TryParse(WeakBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var weak)) _cfg.WeakSignalWarnDbm = weak;
        if (int.TryParse(RetentionBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var ret)) _cfg.RetentionDays = ret;

        _cfg.Save(_configPath);
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
