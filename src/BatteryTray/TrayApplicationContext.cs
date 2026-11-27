using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace BatteryTray;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly BatteryService _service = new();
    private readonly ToolStripMenuItem _summary;
    private readonly ToolStripMenuItem _refresh;
    private readonly ToolStripMenuItem _startup;
    private readonly ToolStripMenuItem _open;
    private readonly ToolStripMenuItem _exit;
    private readonly List<DeviceBattery> _devices = new();
    private bool _scanning;
    private bool _disposed;
    private int _lastAlertBand = 101;

    public TrayApplicationContext()
    {
        _menu = new ContextMenuStrip();
        _summary = new ToolStripMenuItem("Procurando dispositivos...") { Enabled = false };
        _open = new ToolStripMenuItem("Abrir BatteryTray", null, (_, _) => ShowPanel());
        _refresh = new ToolStripMenuItem("Atualizar agora", null, async (_, _) => await RefreshAsync());
        _startup = new ToolStripMenuItem("Iniciar com o Windows", null, (_, _) => ToggleStartup()) { CheckOnClick = true, Checked = IsStartupEnabled() };
        _exit = new ToolStripMenuItem("Sair", null, (_, _) => ExitThread());
        _menu.Items.AddRange([_summary, new ToolStripSeparator(), _open, _refresh, _startup, new ToolStripSeparator(), _exit]);
        _tray = new NotifyIcon { Visible = true, Text = "BatteryTray — monitor de bateria", Icon = CreateBatteryIcon(null), ContextMenuStrip = _menu };
        _tray.DoubleClick += (_, _) => ShowPanel();
        _timer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_scanning || _disposed) return;
        _scanning = true;
        _refresh.Enabled = false;
        try
        {
            var found = await _service.ScanAsync();
            _devices.Clear();
            _devices.AddRange(found);
            var readable = _devices.Where(x => x.Percent.HasValue).ToList();
            var minimum = readable.Count == 0 ? (int?)null : readable.Min(x => x.Percent!.Value);
            _tray.Icon = CreateBatteryIcon(minimum);
            _tray.Text = minimum.HasValue ? $"BatteryTray — menor bateria: {minimum}%" : "BatteryTray — sem níveis disponíveis";
            _summary.Text = _devices.Count == 0 ? "Nenhum dispositivo com bateria detectável" : $"{_devices.Count} dispositivo(s) detectado(s)";
            _menu.Items.OfType<ToolStripMenuItem>().Where(x => x.Tag?.ToString() == "device").ToList().ForEach(x => _menu.Items.Remove(x));
            var insertAt = _menu.Items.IndexOf(_open);
            foreach (var device in _devices.Take(12).Reverse())
            {
                var label = device.Percent.HasValue ? $"{device.Name}: {device.Percent}%" : $"{device.Name}: nível indisponível";
                _menu.Items.Insert(insertAt + 1, new ToolStripMenuItem(label) { Enabled = false, Tag = "device" });
            }
            if (minimum.HasValue) CheckLowBattery(minimum.Value);
            PanelWindow.Current?.UpdateDevices(_devices);
        }
        catch (Exception ex)
        {
            _summary.Text = "Falha ao consultar dispositivos";
            _tray.Text = "BatteryTray — erro na leitura";
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally { _refresh.Enabled = true; _scanning = false; }
    }

    private void CheckLowBattery(int percent)
    {
        var band = percent <= 5 ? 5 : percent <= 10 ? 10 : percent <= 20 ? 20 : 101;
        if (band < _lastAlertBand && band <= 20)
            _tray.ShowBalloonTip(5000, "BatteryTray — bateria baixa", $"Um ou mais dispositivos estão com bateria em {percent}% ou menos.", ToolTipIcon.Warning);
        _lastAlertBand = band;
    }

    private void ShowPanel()
    {
        if (PanelWindow.Current is null || PanelWindow.Current.IsDisposed)
            new PanelWindow(_devices).Show();
        else { PanelWindow.Current.Show(); PanelWindow.Current.Activate(); }
    }

    private void ToggleStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (key is null) return;
        if (_startup.Checked) key.SetValue("BatteryTray", $"\"{Application.ExecutablePath}\"");
        else key.DeleteValue("BatteryTray", false);
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("BatteryTray") is not null;
    }

    private static Icon CreateBatteryIcon(int? percent)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var pen = new Pen(Color.FromArgb(235, 235, 235), 2.2f);
            using var fill = new SolidBrush(percent is null ? Color.Gray : percent <= 15 ? Color.FromArgb(220, 65, 65) : percent <= 30 ? Color.FromArgb(235, 170, 55) : Color.FromArgb(80, 190, 120));
            g.DrawRoundedRectangle(pen, 4, 8, 22, 16, 3);
            g.FillRectangle(fill, 6, 10, Math.Max(2, (int)(18 * (percent ?? 50) / 100.0)), 12);
            g.FillRectangle(Brushes.Gainsboro, 27, 13, 3, 6);
            if (percent is null) { using var f = new Font("Segoe UI", 11, FontStyle.Bold); g.DrawString("?", f, Brushes.White, 8, 7); }
        }
        var hIcon = bitmap.GetHicon();
        return (Icon)Icon.FromHandle(hIcon).Clone();
    }

    protected override void ExitThreadCore()
    {
        _disposed = true;
        _timer.Stop(); _timer.Dispose();
        _tray.Visible = false; _tray.Dispose(); _menu.Dispose();
        PanelWindow.Current?.Close();
        base.ExitThreadCore();
    }
}

internal static class GraphicsExtensions
{
    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, int x, int y, int width, int height, int radius)
    {
        using var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(x, y, d, d, 180, 90); path.AddArc(x + width - d, y, d, d, 270, 90);
        path.AddArc(x + width - d, y + height - d, d, d, 0, 90); path.AddArc(x, y + height - d, d, d, 90, 90);
        path.CloseFigure(); graphics.DrawPath(pen, path);
    }
}
