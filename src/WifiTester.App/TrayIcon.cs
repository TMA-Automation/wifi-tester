using System.Drawing;
using System.Windows.Forms;

namespace WifiTester.App;

internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _autostartItem;

    public event Action? OpenDashboardRequested;
    public event Action? GenerateReportRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        _autostartItem = new ToolStripMenuItem("Uruchamiaj przy starcie", null, (_, _) => ToggleAutostart())
        { Checked = Autostart.IsEnabled() };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Otwórz dashboard", null, (_, _) => OpenDashboardRequested?.Invoke()));
        menu.Items.Add(new ToolStripMenuItem("Generuj raport", null, (_, _) => GenerateReportRequested?.Invoke()));
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Zakończ", null, (_, _) => ExitRequested?.Invoke()));

        _icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = "WifiTester",
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => OpenDashboardRequested?.Invoke();
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/wifitester.ico");
            using var stream = System.Windows.Application.GetResourceStream(uri)!.Stream;
            return new Icon(stream, new Size(16, 16));   // rozmiar pasujący do zasobnika
        }
        catch
        {
            return SystemIcons.Information;   // awaryjnie, gdyby zasób był niedostępny
        }
    }

    private void ToggleAutostart()
    {
        if (Autostart.IsEnabled()) { Autostart.Disable(); _autostartItem.Checked = false; }
        else { Autostart.Enable(); _autostartItem.Checked = true; }
    }

    public void ShowAlert(string title, string message, ToolTipIcon icon)
        => _icon.ShowBalloonTip(5000, title, message, icon);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
