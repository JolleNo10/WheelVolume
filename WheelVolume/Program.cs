using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WheelVolume;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\WheelVolume";
    private const string ShowExistingInstanceEventName = @"Local\WheelVolume.ShowExistingInstance";

    [STAThread]
    static void Main()
    {
        using var showExistingInstanceEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: ShowExistingInstanceEventName
        );
        using var singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: SingleInstanceMutexName,
            createdNew: out bool isFirstInstance
        );

        if (!isFirstInstance || IsAnotherWheelVolumeProcessRunning())
        {
            TrySignalExistingInstance();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(showExistingInstanceEvent));
    }

    private static void TrySignalExistingInstance()
    {
        try
        {
            using var showExistingInstanceEvent = EventWaitHandle.OpenExisting(
                ShowExistingInstanceEventName
            );
            showExistingInstanceEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsAnotherWheelVolumeProcessRunning()
    {
        using var currentProcess = Process.GetCurrentProcess();

        foreach (var process in Process.GetProcessesByName(currentProcess.ProcessName))
        {
            using (process)
            {
                if (process.Id != currentProcess.Id)
                    return true;
            }
        }

        return false;
    }
}

internal sealed class AboutDialog : Form
{
    private const string FallbackRepositoryUrl = "https://github.com/JolleNo10/WheelVolume";

    public AboutDialog()
    {
        var assembly = typeof(Program).Assembly;
        string productName = GetAssemblyAttribute<AssemblyProductAttribute>(assembly)?.Product
            ?? "WheelVolume";
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? Application.ProductVersion;
        string repositoryUrl = GetRepositoryUrl(assembly);

        Text = $"About {productName}";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = true;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 210);

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = productName
        };

        var versionLabel = new Label
        {
            AutoSize = true,
            Text = $"Version {version}"
        };

        var descriptionLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = "Adjust Windows volume with a modifier key and the mouse wheel. Middle-click with the modifier toggles mute."
        };

        var repositoryLink = new LinkLabel
        {
            AutoSize = true,
            Text = repositoryUrl,
            LinkArea = new LinkArea(0, repositoryUrl.Length)
        };
        repositoryLink.LinkClicked += (_, _) => OpenRepository(repositoryUrl);

        var okButton = new Button
        {
            Anchor = AnchorStyles.Right,
            AutoSize = true,
            DialogResult = DialogResult.OK,
            Text = "OK"
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            RowCount = 5,
            ColumnCount = 1
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(titleLabel, 0, 0);
        layout.Controls.Add(versionLabel, 0, 1);
        layout.Controls.Add(descriptionLabel, 0, 2);
        layout.Controls.Add(repositoryLink, 0, 3);
        layout.Controls.Add(okButton, 0, 4);

        AcceptButton = okButton;
        CancelButton = okButton;
        Controls.Add(layout);
    }

    private static TAttribute? GetAssemblyAttribute<TAttribute>(Assembly assembly)
        where TAttribute : Attribute
    {
        return assembly.GetCustomAttribute<TAttribute>();
    }

    private static string GetRepositoryUrl(Assembly assembly)
    {
        var repositoryUrl = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "RepositoryUrl")
            ?.Value;

        return string.IsNullOrWhiteSpace(repositoryUrl)
            ? FallbackRepositoryUrl
            : repositoryUrl;
    }

    private static void OpenRepository(string repositoryUrl)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo(repositoryUrl)
                {
                    UseShellExecute = true
                }
            );
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                $"Could not open repository link.{Environment.NewLine}{repositoryUrl}",
                "WheelVolume",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }
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
    private readonly EventWaitHandle _showExistingInstanceEvent;
    private readonly RegisteredWaitHandle _showExistingInstanceWaitHandle;

    private static NotifyIcon? _trayIcon;
    private static ContextMenuStrip? _trayMenu;
    private static ToolStripMenuItem? _enabledMenuItem;
    private static ToolStripMenuItem? _startOnStartupMenuItem;
    private static Icon? _appIcon;
    private static AudioController? _audioController;
    private static VolumeOsd? _osd;
    private static readonly object _pendingLock = new();
    private static readonly object _wheelDeltaLock = new();
    private static readonly WheelDeltaAccumulator _wheelDeltaAccumulator = new();
    private static readonly LocalUserSettings _settings = LocalUserSettings.Load(
        LocalUserSettings.DefaultPath
    );
    private static int _pendingWheelSteps;
    private static bool _pendingMuteToggle;
    private static bool _processingQueuedInput;
    private static bool _updatingStartupMenuItem;
    private static bool _enabled = true;
    private static ModifierKey _modifierKey = ModifierKey.LeftAlt;
    private static float _volumeStep = DefaultVolumeStep;
    private static OsdScreenMode _osdScreenMode = OsdScreenMode.Cursor;
    private static int _osdTimeoutMs = DefaultOsdTimeoutMs;
    private static DateTime _lastAudioErrorNotificationUtc = DateTime.MinValue;

    public TrayApplicationContext(EventWaitHandle showExistingInstanceEvent)
    {
        _showExistingInstanceEvent = showExistingInstanceEvent;
        LoadSettings();

        _current = this;
        _dispatcher = new Control();
        _ = _dispatcher.Handle;

        _showExistingInstanceWaitHandle = ThreadPool.RegisterWaitForSingleObject(
            _showExistingInstanceEvent,
            (_, _) => RunOnUiThread(ShowAlreadyRunningNotification),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false
        );

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
        ApplyHookEnabled(_enabled);
    }

    protected override void ExitThreadCore()
    {
        Cleanup();
        base.ExitThreadCore();
    }

    private static void Cleanup()
    {
        _current?._showExistingInstanceWaitHandle.Unregister(null);
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
        _startOnStartupMenuItem = null;

        _audioController?.Dispose();
        _audioController = null;

        _appIcon?.Dispose();
        _appIcon = null;

        _dispatcher?.Dispose();
        _dispatcher = null;

        _current = null;
    }

    private static void ShowAlreadyRunningNotification()
    {
        _trayIcon?.ShowBalloonTip(
            2500,
            "WheelVolume",
            "WheelVolume is already running.",
            ToolTipIcon.Info
        );
    }

    private static void SetHookEnabled(bool enabled)
    {
        _enabled = enabled;
        SaveSettings();
        ApplyHookEnabled(enabled);
    }

    private static void ApplyHookEnabled(bool enabled)
    {
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
        SaveSettings();

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

        lock (_wheelDeltaLock)
        {
            _wheelDeltaAccumulator.Reset();
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

    private static AudioController GetAudioController()
    {
        return _audioController ??= new AudioController(new NAudioEndpointProvider());
    }

    private static void ResetAudioController()
    {
        _audioController?.Dispose();
        _audioController = null;
    }

    private static void ChangeVolume(int wheelSteps)
    {
        if (wheelSteps == 0)
            return;

        ExecuteAudioOperation(
            "ChangeVolume",
            controller => controller.ChangeVolume(wheelSteps, _volumeStep)
        );
    }

    private static void ToggleMute()
    {
        ExecuteAudioOperation("ToggleMute", controller => controller.ToggleMute());
    }

    private static void ExecuteAudioOperation(
        string operation,
        Func<AudioController, AudioState> action
    )
    {
        try
        {
            AudioState state = action(GetAudioController());
            _osd?.ShowVolume(state.VolumePercent, state.Muted);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            HandleAudioFailure(operation, ex);
        }
    }

    private static void HandleAudioFailure(string operation, Exception ex)
    {
        LogAudioFailure(operation, ex);
        ResetAudioController();

        if (ShouldShowAudioErrorNotification())
        {
            _trayIcon?.ShowBalloonTip(
                5000,
                "WheelVolume",
                "No active playback device was found.",
                ToolTipIcon.Warning
            );
        }
    }

    private static void LogAudioFailure(string operation, Exception ex)
    {
        try
        {
            string message =
                $"[{DateTimeOffset.Now:O}] {operation} failed: {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}";

            Trace.WriteLine(message);

            string settingsDirectory =
                Path.GetDirectoryName(LocalUserSettings.DefaultPath)
                ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Directory.CreateDirectory(settingsDirectory);
            File.AppendAllText(Path.Combine(settingsDirectory, "audio-errors.log"), message + Environment.NewLine);
        }
        catch
        {
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
                int wheelSteps = GetWheelSteps(delta);

                if (wheelSteps != 0)
                    QueueInput(wheelSteps, toggleMute: false);

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

    private static int GetWheelSteps(short delta)
    {
        lock (_wheelDeltaLock)
        {
            return _wheelDeltaAccumulator.AddDelta(delta);
        }
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

        _startOnStartupMenuItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = StartupRegistration.IsEnabled(Application.ExecutablePath),
            CheckOnClick = true
        };
        _startOnStartupMenuItem.CheckedChanged += (_, _) =>
        {
            if (!_updatingStartupMenuItem)
                SetStartOnStartup(_startOnStartupMenuItem.Checked);
        };

        var settingsMenu = new ToolStripMenuItem("Settings");
        settingsMenu.DropDownItems.Add(BuildVolumeStepMenu());
        settingsMenu.DropDownItems.Add(BuildOsdTimeoutMenu());
        settingsMenu.DropDownItems.Add(BuildOsdScreenMenu());
        settingsMenu.DropDownItems.Add(BuildModifierKeyMenu());

        menu.Items.Add(_enabledMenuItem);
        menu.Items.Add(_startOnStartupMenuItem);
        menu.Items.Add(settingsMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("About WheelVolume", null, (_, _) => ShowAboutDialog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => _current?.ExitThread());

        return menu;
    }

    private static void ShowAboutDialog()
    {
        using var aboutDialog = new AboutDialog();
        aboutDialog.ShowDialog();
    }

    private static ToolStripMenuItem BuildVolumeStepMenu()
    {
        var menu = new ToolStripMenuItem("Volume Step");

        AddRadioMenuItem(menu, "1%", _volumeStep == 0.01f, () => SetVolumeStep(0.01f));
        AddRadioMenuItem(menu, "2%", _volumeStep == 0.02f, () => SetVolumeStep(0.02f));
        AddRadioMenuItem(menu, "5%", _volumeStep == 0.05f, () => SetVolumeStep(0.05f));

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
            () => SetModifierKey(ModifierKey.LeftAlt)
        );
        AddRadioMenuItem(
            menu,
            "Either Alt",
            _modifierKey == ModifierKey.EitherAlt,
            () => SetModifierKey(ModifierKey.EitherAlt)
        );
        AddRadioMenuItem(
            menu,
            "Ctrl",
            _modifierKey == ModifierKey.Ctrl,
            () => SetModifierKey(ModifierKey.Ctrl)
        );
        AddRadioMenuItem(
            menu,
            "Shift",
            _modifierKey == ModifierKey.Shift,
            () => SetModifierKey(ModifierKey.Shift)
        );
        AddRadioMenuItem(
            menu,
            "Win",
            _modifierKey == ModifierKey.Win,
            () => SetModifierKey(ModifierKey.Win)
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
        SaveSettings();
    }

    private static void SetOsdScreenMode(OsdScreenMode mode)
    {
        _osdScreenMode = mode;
        ApplyOsdSettings();
        SaveSettings();
    }

    private static void SetVolumeStep(float volumeStep)
    {
        _volumeStep = volumeStep;
        SaveSettings();
    }

    private static void SetModifierKey(ModifierKey modifierKey)
    {
        _modifierKey = modifierKey;
        SaveSettings();
    }

    private static void ApplyOsdSettings()
    {
        if (_osd == null)
            return;

        _osd.DisplayDuration = _osdTimeoutMs;
        _osd.ScreenMode = _osdScreenMode;
    }

    private static void LoadSettings()
    {
        _enabled = _settings.Enabled;
        _volumeStep = NormalizeVolumeStep(_settings.VolumeStep);
        _osdTimeoutMs = NormalizeOsdTimeout(_settings.OsdTimeoutMs);
        _osdScreenMode = ParseEnum(_settings.OsdScreenMode, OsdScreenMode.Cursor);
        _modifierKey = ParseEnum(_settings.ModifierKey, ModifierKey.LeftAlt);
    }

    private static void SaveSettings()
    {
        _settings.Enabled = _enabled;
        _settings.VolumeStep = _volumeStep;
        _settings.OsdTimeoutMs = _osdTimeoutMs;
        _settings.OsdScreenMode = _osdScreenMode.ToString();
        _settings.ModifierKey = _modifierKey.ToString();
        _settings.Save(LocalUserSettings.DefaultPath);
    }

    private static float NormalizeVolumeStep(float volumeStep)
    {
        return volumeStep switch
        {
            0.01f or 0.02f or 0.05f => volumeStep,
            _ => DefaultVolumeStep
        };
    }

    private static int NormalizeOsdTimeout(int timeoutMs)
    {
        return timeoutMs switch
        {
            500 or 700 or 1200 or 2000 => timeoutMs,
            _ => DefaultOsdTimeoutMs
        };
    }

    private static TEnum ParseEnum<TEnum>(string value, TEnum fallback)
        where TEnum : struct
    {
        return Enum.TryParse(value, ignoreCase: true, out TEnum parsed) ? parsed : fallback;
    }

    private static void SetStartOnStartup(bool enabled)
    {
        if (StartupRegistration.TrySetEnabled(enabled, Application.ExecutablePath))
            return;

        if (_startOnStartupMenuItem != null)
        {
            _updatingStartupMenuItem = true;
            _startOnStartupMenuItem.Checked = !enabled;
            _updatingStartupMenuItem = false;
        }

        _trayIcon?.ShowBalloonTip(
            5000,
            "WheelVolume",
            "Startup setting could not be changed.",
            ToolTipIcon.Error
        );
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
