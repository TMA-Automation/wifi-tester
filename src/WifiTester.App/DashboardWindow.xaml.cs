using System.Windows;
using WifiTester.Core.Monitoring;

namespace WifiTester.App;

public partial class DashboardWindow : Window
{
    public DashboardWindow(MonitoringService svc)
    {
        InitializeComponent();
        DataContext = new DashboardViewModel(svc);
    }
}
