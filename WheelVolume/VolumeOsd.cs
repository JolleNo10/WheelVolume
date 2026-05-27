using System;
using System.Drawing;
using System.Windows.Forms;

namespace WheelVolume;

internal class VolumeOsd : Form
{
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private readonly Label _label;
    private readonly ProgressBar _bar;
    private readonly System.Windows.Forms.Timer _hideTimer;
    private TrayApplicationContext.OsdScreenMode _screenMode;

    public VolumeOsd()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(30, 30, 30);
        Opacity = 0.9;
        MinimumSize = new Size(260, 80);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14, 10, 14, 12);

        _label = new Label
        {
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = false,
            Size = new Size(232, 38),
            Margin = new Padding(0, 0, 0, 8)
        };

        _bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Size = new Size(232, 18),
            Margin = new Padding(0)
        };

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        layout.Controls.Add(_label, 0, 0);
        layout.Controls.Add(_bar, 0, 1);
        Controls.Add(layout);

        _hideTimer = new System.Windows.Forms.Timer
        {
            Interval = 700
        };

        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var createParams = base.CreateParams;
            createParams.ExStyle |= WS_EX_NOACTIVATE;
            return createParams;
        }
    }

    public int DisplayDuration
    {
        get => _hideTimer.Interval;
        set => _hideTimer.Interval = Math.Clamp(value, 100, 10000);
    }

    public TrayApplicationContext.OsdScreenMode ScreenMode
    {
        get => _screenMode;
        set => _screenMode = value;
    }

    public void ShowVolume(int volumePercent, bool muted)
    {
        _label.Text = muted ? "Muted" : $"Volume {volumePercent}%";
        _bar.Value = Math.Clamp(volumePercent, 0, 100);

        var screen = GetTargetScreen().WorkingArea;

        Location = new Point(
            screen.Left + (screen.Width - Width) / 2,
            screen.Bottom - Height - 80
        );

        if (!Visible)
            Show();

        BringToFront();

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private Screen GetTargetScreen()
    {
        return _screenMode switch
        {
            TrayApplicationContext.OsdScreenMode.Primary => Screen.PrimaryScreen!,
            _ => Screen.FromPoint(Cursor.Position)
        };
    }
}
