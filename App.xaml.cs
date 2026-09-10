using System;
using System.IO;
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

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private WinForms.NotifyIcon? _tray;
    private MainWindow? _main;

    public Settings Settings { get; private set; } = new();
    public HistoryStore Store { get; private set; } = null!;

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out bool createdNew);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!createdNew)
        {
            // Another instance is running: let it take the foreground, ask it to show its window, then quit.
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

        DispatcherUnhandledException += (_, args) =>
        {
            Log("Unhandled: " + args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log("Fatal: " + args.ExceptionObject);

        Settings = Settings.Load();
        Store = new HistoryStore(Settings.DataDir) { MaxItems = Settings.MaxItems };
        Store.Load();

        _main = new MainWindow(Store, Settings);
        _main.InitializeHidden();

        SetupTray();

        // Wake up when a second instance is launched.
        var waiter = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(() => _main?.ShowPopup());
        }) { IsBackground = true, Name = "ClipVault.ShowWaiter" };
        waiter.Start();

        if (!Settings.FirstRunShown)
        {
            Settings.FirstRunShown = true;
            Settings.Save();
            _tray?.ShowBalloonTip(5000, "ClipVault is running",
                $"Press {Settings.HotkeyText} anywhere to open your clipboard history.", WinForms.ToolTipIcon.Info);
        }
    }

    private void SetupTray()
    {
        var menu = new WinForms.ContextMenuStrip();
        var open = menu.Items.Add("Open ClipVault", null, (_, _) => _main?.ShowPopup());
        open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add("Settings...", null, (_, _) => _main?.OpenSettings());
        var runAtLogin = new WinForms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        runAtLogin.Click += (_, _) =>
        {
            try { StartupRegistration.SetEnabled(runAtLogin.Checked); }
            catch (Exception ex) { Log("Startup registration failed: " + ex); }
        };
        menu.Items.Add(runAtLogin);
        menu.Items.Add(new WinForms.ToolStripSeparator());
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
        Store?.SaveNow();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _mutex?.Dispose();
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
