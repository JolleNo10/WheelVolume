using System;
using System.Drawing;
using System.Windows.Forms;

namespace WheelVolume;

internal class VolumeOsd : Form
{
    private readonly Label _label;
    private readonly ProgressBar _bar;
    private readonly System.Windows.Forms.Timer _hideTimer;
    private TrayApplicationContext.OsdScreenMode _screenMode;

    public VolumeOsd()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(30, 30, 30);
        Opacity = 0.9;
        Size = new Size(260, 80);

        _label = new Label
        {
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 45
        };

        _bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Dock = DockStyle.Bottom,
            Height = 18
        };

        Controls.Add(_label);
        Controls.Add(_bar);

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
