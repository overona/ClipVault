using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using ClipVault.Services;
using ClipVault.Views;
using WinForms = System.Windows.Forms;

namespace ClipVault;

public partial class App : Application
{
    private const string MutexName = @"Local\ClipVault.SingleInstance";
    private const string ShowEventName = @"Local\ClipVault.Show";
    private const string ExitEventName = @"Local\ClipVault.Exit";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private EventWaitHandle? _exitEvent;
    private WinForms.NotifyIcon? _tray;
    private MainWindow? _main;

    public Settings Settings { get; private set; } = new();
    public HistoryStore Store { get; private set; } = null!;

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Log("Unhandled: " + args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log("Fatal: " + args.ExceptionObject);

        Settings = Settings.Load();
        ThemeManager.Apply(Settings.Theme);
        bool portable = e.Args.Any(a => string.Equals(a, "--portable", StringComparison.OrdinalIgnoreCase));

        _mutex = new Mutex(true, MutexName, out bool createdNew);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);

        if (!createdNew)
        {
            // Another instance is running. A newer exe launched from elsewhere (a downloaded update) gets to
            // offer replacing the installed copy; otherwise just ask the running instance to show its window.
            if (!portable && Installer.ShouldOffer(Settings) && OfferInstall())
                return;

            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("ClipVault"))
                    using (p) if (p.Id != Environment.ProcessId) ClipVault.Native.NativeMethods.AllowSetForegroundWindow((uint)p.Id);
            }
            catch { }
            _showEvent.Set();
            Shutdown();
            return;
        }

        if (!portable && Installer.ShouldOffer(Settings) && OfferInstall())
            return;

        StartupRegistration.RepairIfStale();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        Store = new HistoryStore(Settings.DataDir) { MaxItems = Settings.MaxItems };
        Store.Load();

        _main = new MainWindow(Store, Settings);
        _main.InitializeHidden();

        SetupTray();

        // Wake up when a second instance is launched, or exit when an updater asks us to.
        var handles = new WaitHandle[] { _showEvent, _exitEvent };
        var waiter = new Thread(() =>
        {
            while (true)
            {
                int signaled = WaitHandle.WaitAny(handles);
                if (signaled == 0) Dispatcher.BeginInvoke(() => _main?.ShowPopup());
                else { Dispatcher.BeginInvoke(ExitApp); return; }
            }
        }) { IsBackground = true, Name = "ClipVault.SignalWaiter" };
        waiter.Start();

        if (!Settings.FirstRunShown)
        {
            Settings.FirstRunShown = true;
            Settings.Save();
            _tray?.ShowBalloonTip(5000, "ClipVault is running",
                $"Press {Settings.HotkeyText} anywhere to open your clipboard history.", WinForms.ToolTipIcon.Info);
        }
    }

    /// <summary>
    /// Shows the install / update dialog. Returns true when the exe was installed and the installed copy launched
    /// (this process is shutting down); false when the user chose to keep running this copy.
    /// </summary>
    private bool OfferInstall()
    {
        var dlg = new InstallWindow(Installer.InstalledVersion);
        if (dlg.ShowDialog() != true)
        {
            Settings.InstallPromptDismissedFor = Installer.CurrentExe;
            Settings.Save();
            return false;
        }

        try
        {
            // Ask any running instance to exit (it saves its history first), then replace the file.
            _exitEvent?.Set();
            Installer.WaitForOtherInstancesToExit(TimeSpan.FromSeconds(6));
            Installer.Install(dlg.StartWithWindows, dlg.AddShortcut);
        }
        catch (Exception ex)
        {
            Log("Install failed: " + ex);
            MessageBox.Show("ClipVault could not be installed:\n\n" + ex.Message + "\n\nIt will run from its current location instead.",
                "ClipVault", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // Release the single-instance objects before starting the installed copy so it becomes the primary instance.
        ReleaseInstanceHandles();
        Installer.Launch(Installer.InstalledExe);
        Shutdown();
        return true;
    }

    private void ReleaseInstanceHandles()
    {
        try { _mutex?.Dispose(); } catch { }
        try { _showEvent?.Dispose(); } catch { }
        try { _exitEvent?.Dispose(); } catch { }
        _mutex = null; _showEvent = null; _exitEvent = null;
    }

    private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        // Windows raises this (category General) when the user flips light/dark app mode.
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.General && Settings.Theme == AppTheme.System)
            Dispatcher.BeginInvoke(() => ThemeManager.Apply(AppTheme.System));
    }

    private void SetupTray()
    {
        var menu = new WinForms.ContextMenuStrip();
        var open = menu.Items.Add("Open ClipVault", null, (_, _) => _main?.ShowPopup());
        open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add("Settings...", null, (_, _) => _main?.OpenSettings());
        menu.Items.Add("About ClipVault...", null, (_, _) => new AboutWindow().ShowDialog());
        var runAtLogin = new WinForms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        runAtLogin.Click += (_, _) =>
        {
            try { StartupRegistration.SetEnabled(runAtLogin.Checked); }
            catch (Exception ex) { Log("Startup registration failed: " + ex); }
        };
        menu.Items.Add(runAtLogin);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        if (Installer.IsInstalledCopy)
            menu.Items.Add("Uninstall ClipVault...", null, (_, _) => UninstallFromTray());
        else
            menu.Items.Add("Install ClipVault...", null, (_, _) => OfferInstall());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        menu.Opening += (_, _) => runAtLogin.Checked = StartupRegistration.IsEnabled();

        _tray = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "ClipVault  (" + Settings.HotkeyText + ")",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.MouseClick += (_, args) => { if (args.Button == WinForms.MouseButtons.Left) _main?.ShowPopup(); };
    }

    private void UninstallFromTray()
    {
        var answer = MessageBox.Show(
            "Remove ClipVault from this PC?\n\nThis deletes the program file, the Start menu shortcut and the start-with-Windows entry.",
            "Uninstall ClipVault", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        var data = MessageBox.Show("Also delete your clipboard history and settings?", "Uninstall ClipVault",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        Store?.SaveNow();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        _main?.Shutdown();
        try { Installer.Uninstall(deleteData: data == MessageBoxResult.Yes); }
        catch (Exception ex) { Log("Uninstall failed: " + ex); }
        Shutdown();
    }

    public void UpdateTrayText() { if (_tray is not null) _tray.Text = "ClipVault  (" + Settings.HotkeyText + ")"; }

    private static System.Drawing.Icon LoadIcon()
    {
        var res = GetResourceStream(new Uri("pack://application:,,,/Assets/ClipVault.ico"));
        return res is not null ? new System.Drawing.Icon(res.Stream) : System.Drawing.SystemIcons.Application;
    }

    public void ExitApp()
    {
        Store?.SaveNow();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        _main?.Shutdown();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        Store?.SaveNow();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        ReleaseInstanceHandles();
        base.OnExit(e);
    }

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Settings.DataDir);
            File.AppendAllText(Path.Combine(Settings.DataDir, "clipvault.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
