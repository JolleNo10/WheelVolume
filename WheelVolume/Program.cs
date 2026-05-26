using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NAudio.CoreAudioApi;

namespace WheelVolume;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const float DefaultVolumeStep = 0.02f;
    private const int DefaultOsdTimeoutMs = 700;
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_MBUTTONDOWN = 0x0207;

    private static LowLevelMouseProc _proc = HookCallback;
    private static IntPtr _hookId = IntPtr.Zero;
    private static TrayApplicationContext? _current;
    private static Control? _dispatcher;

    private static NotifyIcon? _trayIcon;
    private static ContextMenuStrip? _trayMenu;
    private static ToolStripMenuItem? _enabledMenuItem;
    private static Icon? _appIcon;
    private static MMDevice? _audioDevice;
    private static VolumeOsd? _osd;
    private static readonly object _pendingLock = new();
    private static int _pendingWheelSteps;
    private static bool _pendingMuteToggle;
    private static bool _processingQueuedInput;
    private static bool _enabled = true;
    private static ModifierKey _modifierKey = ModifierKey.LeftAlt;
    private static float _volumeStep = DefaultVolumeStep;
    private static OsdScreenMode _osdScreenMode = OsdScreenMode.Cursor;
    private static int _osdTimeoutMs = DefaultOsdTimeoutMs;
    private static DateTime _lastAudioErrorNotificationUtc = DateTime.MinValue;

    public TrayApplicationContext()
    {
        _current = this;
        _dispatcher = new Control();
        _ = _dispatcher.Handle;

        _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        _trayMenu = BuildTrayMenu();
        _trayIcon = new NotifyIcon
        {
            Icon = _appIcon ?? SystemIcons.Application,
            Text = "WheelVolume",
            Visible = true,
            ContextMenuStrip = _trayMenu
        };

        _osd = new VolumeOsd();
        ApplyOsdSettings();
        SetHookEnabled(_enabled);
    }

    protected override void ExitThreadCore()
    {
        Cleanup();
        base.ExitThreadCore();
    }

    private static void Cleanup()
    {
        RemoveHook();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _osd?.Dispose();
        _osd = null;

        _trayMenu?.Dispose();
        _trayMenu = null;
        _enabledMenuItem = null;

        _audioDevice?.Dispose();
        _audioDevice = null;

        _appIcon?.Dispose();
        _appIcon = null;

        _dispatcher?.Dispose();
        _dispatcher = null;

        _current = null;
    }

    private static void SetHookEnabled(bool enabled)
    {
        _enabled = enabled;

        if (enabled)
        {
            InstallHook();
        }
        else
        {
            RemoveHook();
            ClearQueuedInput();
        }
    }

    private static void InstallHook()
    {
        if (_hookId != IntPtr.Zero)
            return;

        _hookId = SetHook(_proc);
        if (_hookId != IntPtr.Zero)
            return;

        int error = Marshal.GetLastWin32Error();
        _enabled = false;
        if (_enabledMenuItem != null)
            _enabledMenuItem.Checked = false;

        _trayIcon?.ShowBalloonTip(
            5000,
            "WheelVolume",
            $"Mouse hook could not be installed. Error {error}.",
            ToolTipIcon.Error
        );
    }

    private static void RemoveHook()
    {
        if (_hookId == IntPtr.Zero)
            return;

        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    private static void ClearQueuedInput()
    {
        lock (_pendingLock)
        {
            _pendingWheelSteps = 0;
            _pendingMuteToggle = false;
            _processingQueuedInput = false;
        }
    }

    private static bool RefreshAudioDevice(bool showError)
    {
        try
        {
            _audioDevice?.Dispose();
            using var enumerator = new MMDeviceEnumerator();
            _audioDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _audioDevice = null;

            if (showError && ShouldShowAudioErrorNotification())
            {
                _trayIcon?.ShowBalloonTip(
                    5000,
                    "WheelVolume",
                    "No active playback device was found.",
                    ToolTipIcon.Warning
                );
            }

            return false;
        }
    }

    private static bool ShouldShowAudioErrorNotification()
    {
        var now = DateTime.UtcNow;

        if ((now - _lastAudioErrorNotificationUtc).TotalSeconds < 10)
            return false;

        _lastAudioErrorNotificationUtc = now;
        return true;
    }

    private static void ChangeVolume(int wheelSteps, bool retryOnDeviceError = true)
    {
        if (wheelSteps == 0)
            return;

        if (_audioDevice == null && !RefreshAudioDevice(showError: true))
            return;

        try
        {
            var volume = _audioDevice!.AudioEndpointVolume;

            float newVolume = Math.Clamp(
                volume.MasterVolumeLevelScalar + (wheelSteps * _volumeStep),
                0.0f,
                1.0f
            );

            volume.MasterVolumeLevelScalar = newVolume;

            int percent = (int)Math.Round(newVolume * 100);
            _osd?.ShowVolume(percent, volume.Mute);
        }
        catch (COMException)
        {
            if (retryOnDeviceError && RefreshAudioDevice(showError: true))
                ChangeVolume(wheelSteps, retryOnDeviceError: false);
        }
    }

    private static void ToggleMute(bool retryOnDeviceError = true)
    {
        if (_audioDevice == null && !RefreshAudioDevice(showError: true))
            return;

        try
        {
            var volume = _audioDevice!.AudioEndpointVolume;
            volume.Mute = !volume.Mute;

            int percent = (int)Math.Round(volume.MasterVolumeLevelScalar * 100);
            _osd?.ShowVolume(percent, volume.Mute);
        }
        catch (COMException)
        {
            if (retryOnDeviceError && RefreshAudioDevice(showError: true))
                ToggleMute(retryOnDeviceError: false);
        }
    }

    private static void QueueInput(int wheelSteps, bool toggleMute)
    {
        lock (_pendingLock)
        {
            _pendingWheelSteps += wheelSteps;
            _pendingMuteToggle ^= toggleMute;

            if (_processingQueuedInput)
                return;

            _processingQueuedInput = true;
        }

        if (_current?.RunOnUiThread(ProcessQueuedInput) != true)
        {
            lock (_pendingLock)
            {
                _processingQueuedInput = false;
            }
        }
    }

    private bool RunOnUiThread(Action action)
    {
        if (_dispatcher is not { IsHandleCreated: true } dispatcher || dispatcher.IsDisposed)
            return false;

        dispatcher.BeginInvoke(action);
        return true;
    }

    private static void ProcessQueuedInput()
    {
        int wheelSteps;
        bool toggleMute;

        lock (_pendingLock)
        {
            wheelSteps = _pendingWheelSteps;
            toggleMute = _pendingMuteToggle;
            _pendingWheelSteps = 0;
            _pendingMuteToggle = false;
            _processingQueuedInput = false;
        }

        ChangeVolume(wheelSteps);

        if (toggleMute)
            ToggleMute();
    }

    private static IntPtr SetHook(LowLevelMouseProc proc)
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;

        return SetWindowsHookEx(
            WH_MOUSE_LL,
            proc,
            GetModuleHandle(curModule.ModuleName),
            0
        );
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !_enabled)
            return CallNextHookEx(_hookId, nCode, wParam, lParam);

        if (wParam != WM_MOUSEWHEEL && wParam != WM_MBUTTONDOWN)
            return CallNextHookEx(_hookId, nCode, wParam, lParam);

        if (IsConfiguredModifierHeld())
        {
            if (wParam == WM_MOUSEWHEEL)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                short delta = (short)((hookStruct.mouseData >> 16) & 0xffff);

                QueueInput(delta > 0 ? 1 : -1, toggleMute: false);

                return (IntPtr)1;
            }

            if (wParam == WM_MBUTTONDOWN)
            {
                QueueInput(wheelSteps: 0, toggleMute: true);

                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();

        _enabledMenuItem = new ToolStripMenuItem("Enabled")
        {
            Checked = _enabled,
            CheckOnClick = true
        };
        _enabledMenuItem.CheckedChanged += (_, _) => SetHookEnabled(_enabledMenuItem.Checked);

        var settingsMenu = new ToolStripMenuItem("Settings");
        settingsMenu.DropDownItems.Add(BuildVolumeStepMenu());
        settingsMenu.DropDownItems.Add(BuildOsdTimeoutMenu());
        settingsMenu.DropDownItems.Add(BuildOsdScreenMenu());
        settingsMenu.DropDownItems.Add(BuildModifierKeyMenu());

        menu.Items.Add(_enabledMenuItem);
        menu.Items.Add(settingsMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => _current?.ExitThread());

        return menu;
    }

    private static ToolStripMenuItem BuildVolumeStepMenu()
    {
        var menu = new ToolStripMenuItem("Volume Step");

        AddRadioMenuItem(menu, "1%", _volumeStep == 0.01f, () => _volumeStep = 0.01f);
        AddRadioMenuItem(menu, "2%", _volumeStep == 0.02f, () => _volumeStep = 0.02f);
        AddRadioMenuItem(menu, "5%", _volumeStep == 0.05f, () => _volumeStep = 0.05f);

        return menu;
    }

    private static ToolStripMenuItem BuildOsdTimeoutMenu()
    {
        var menu = new ToolStripMenuItem("OSD Timeout");

        AddRadioMenuItem(menu, "500 ms", _osdTimeoutMs == 500, () => SetOsdTimeout(500));
        AddRadioMenuItem(menu, "700 ms", _osdTimeoutMs == 700, () => SetOsdTimeout(700));
        AddRadioMenuItem(menu, "1.2 s", _osdTimeoutMs == 1200, () => SetOsdTimeout(1200));
        AddRadioMenuItem(menu, "2 s", _osdTimeoutMs == 2000, () => SetOsdTimeout(2000));

        return menu;
    }

    private static ToolStripMenuItem BuildOsdScreenMenu()
    {
        var menu = new ToolStripMenuItem("OSD Screen");

        AddRadioMenuItem(
            menu,
            "Cursor Monitor",
            _osdScreenMode == OsdScreenMode.Cursor,
            () => SetOsdScreenMode(OsdScreenMode.Cursor)
        );
        AddRadioMenuItem(
            menu,
            "Primary Monitor",
            _osdScreenMode == OsdScreenMode.Primary,
            () => SetOsdScreenMode(OsdScreenMode.Primary)
        );

        return menu;
    }

    private static ToolStripMenuItem BuildModifierKeyMenu()
    {
        var menu = new ToolStripMenuItem("Modifier Key");

        AddRadioMenuItem(
            menu,
            "Left Alt",
            _modifierKey == ModifierKey.LeftAlt,
            () => _modifierKey = ModifierKey.LeftAlt
        );
        AddRadioMenuItem(
            menu,
            "Either Alt",
            _modifierKey == ModifierKey.EitherAlt,
            () => _modifierKey = ModifierKey.EitherAlt
        );
        AddRadioMenuItem(
            menu,
            "Ctrl",
            _modifierKey == ModifierKey.Ctrl,
            () => _modifierKey = ModifierKey.Ctrl
        );
        AddRadioMenuItem(
            menu,
            "Shift",
            _modifierKey == ModifierKey.Shift,
            () => _modifierKey = ModifierKey.Shift
        );
        AddRadioMenuItem(
            menu,
            "Win",
            _modifierKey == ModifierKey.Win,
            () => _modifierKey = ModifierKey.Win
        );

        return menu;
    }

    private static void AddRadioMenuItem(
        ToolStripMenuItem parent,
        string text,
        bool isChecked,
        Action onClick
    )
    {
        var item = new ToolStripMenuItem(text)
        {
            Checked = isChecked
        };

        item.Click += (_, _) =>
        {
            foreach (ToolStripMenuItem sibling in parent.DropDownItems)
                sibling.Checked = false;

            item.Checked = true;
            onClick();
        };

        parent.DropDownItems.Add(item);
    }

    private static void SetOsdTimeout(int timeoutMs)
    {
        _osdTimeoutMs = timeoutMs;
        ApplyOsdSettings();
    }

    private static void SetOsdScreenMode(OsdScreenMode mode)
    {
        _osdScreenMode = mode;
        ApplyOsdSettings();
    }

    private static void ApplyOsdSettings()
    {
        if (_osd == null)
            return;

        _osd.DisplayDuration = _osdTimeoutMs;
        _osd.ScreenMode = _osdScreenMode;
    }

    private static bool IsConfiguredModifierHeld()
    {
        return _modifierKey switch
        {
            ModifierKey.LeftAlt => IsKeyHeld(Keys.LMenu),
            ModifierKey.EitherAlt => IsKeyHeld(Keys.LMenu) || IsKeyHeld(Keys.RMenu),
            ModifierKey.Ctrl => IsKeyHeld(Keys.ControlKey),
            ModifierKey.Shift => IsKeyHeld(Keys.ShiftKey),
            ModifierKey.Win => IsKeyHeld(Keys.LWin) || IsKeyHeld(Keys.RWin),
            _ => IsKeyHeld(Keys.LMenu)
        };
    }

    private static bool IsKeyHeld(Keys key)
    {
        return (GetAsyncKeyState(key) & 0x8000) != 0;
    }

    public enum OsdScreenMode
    {
        Cursor,
        Primary
    }

    private enum ModifierKey
    {
        LeftAlt,
        EitherAlt,
        Ctrl,
        Shift,
        Win
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public int mouseData;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelMouseProc lpfn,
        IntPtr hMod,
        uint dwThreadId
    );

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(
        IntPtr hhk,
        int nCode,
        IntPtr wParam,
        IntPtr lParam
    );

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(Keys vKey);
}
