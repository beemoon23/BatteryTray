namespace BatteryTray;

public sealed class PanelWindow : Form
{
    public static PanelWindow? Current { get; private set; }
    private readonly FlowLayoutPanel _list;
    private readonly Label _headline;
    private readonly Label _footnote;

    public PanelWindow(IReadOnlyList<DeviceBattery> devices)
    {
        Current = this;
        Text = "BatteryTray"; StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        ClientSize = new Size(440, 500); BackColor = Color.FromArgb(245, 245, 245); Font = new Font("Segoe UI", 10);
        var title = new Label { Text = "Baterias dos seus dispositivos", Font = new Font("Segoe UI Semibold", 16), AutoSize = true, Location = new Point(22, 20) };
        _headline = new Label { Text = "Atualizado automaticamente", ForeColor = Color.DimGray, AutoSize = true, Location = new Point(24, 56) };
        _list = new FlowLayoutPanel { Location = new Point(20, 92), Size = new Size(400, 340), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.Transparent };
        _footnote = new Label { Text = "A leitura depende do suporte oferecido pelo dispositivo e pelo Windows.", ForeColor = Color.DimGray, AutoSize = false, Size = new Size(395, 42), Location = new Point(22, 448) };
        Controls.AddRange([title, _headline, _list, _footnote]); UpdateDevices(devices);
        FormClosed += (_, _) => Current = null;
    }

    public void UpdateDevices(IReadOnlyList<DeviceBattery> devices)
    {
        if (IsDisposed || !IsHandleCreated && Visible) return;
        if (InvokeRequired) { BeginInvoke(() => UpdateDevices(devices)); return; }
        _list.SuspendLayout(); _list.Controls.Clear();
        var readable = devices.Where(x => x.Percent.HasValue).ToList();
        _headline.Text = devices.Count == 0 ? "Nenhum dispositivo com bateria detectável" : $"{readable.Count} de {devices.Count} com nível de bateria disponível";
        if (devices.Count == 0)
            _list.Controls.Add(new Label { Text = "Conecte ou ligue seus periféricos para tentar identificá-los.", AutoSize = true, Padding = new Padding(8) });
        foreach (var device in devices)
        {
            var card = new Panel { Width = 378, Height = 68, BackColor = Color.White, Margin = new Padding(0, 0, 0, 9), Padding = new Padding(10) };
            var name = new Label { Text = device.Name, Font = new Font("Segoe UI Semibold", 10), AutoEllipsis = true, Location = new Point(12, 9), Size = new Size(245, 22) };
            var level = new Label { Text = device.Percent.HasValue ? $"{device.Percent}%" : "—", Font = new Font("Segoe UI Semibold", 12), ForeColor = device.Percent.HasValue && device.Percent.Value <= 15 ? Color.Firebrick : Color.FromArgb(30, 100, 65), Location = new Point(300, 8), Size = new Size(58, 24), TextAlign = ContentAlignment.MiddleRight };
            var source = new Label { Text = device.Percent.HasValue ? device.Source : device.Source + " • nível indisponível", Font = new Font("Segoe UI", 8), ForeColor = Color.DimGray, Location = new Point(13, 36), Size = new Size(340, 18), AutoEllipsis = true };
            card.Controls.AddRange([name, level, source]); _list.Controls.Add(card);
        }
        _list.ResumeLayout();
    }

    protected override void OnFormClosed(FormClosedEventArgs e) { Current = null; base.OnFormClosed(e); }
}
