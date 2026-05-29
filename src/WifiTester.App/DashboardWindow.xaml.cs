using System.Windows;
using WifiTester.Core.Monitoring;
using WifiTester.Core.Updates;

namespace WifiTester.App;

public partial class DashboardWindow : Window
{
    private readonly DashboardViewModel _vm;

    public event Action? SettingsRequested;
    public event Action? UpdateRequested;

    public DashboardWindow(MonitoringService svc)
    {
        InitializeComponent();
        _vm = new DashboardViewModel(svc);
        DataContext = _vm;
    }

    public void ApplyUpdate(UpdateCheck? u) => _vm.SetUpdate(u);

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void OnUpdateClick(object sender, RoutedEventArgs e) => UpdateRequested?.Invoke();
}
