using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace ClipVault.Services;

/// <summary>
/// ClipVault ships as one exe that people pass around. This copies that exe into a stable per-user folder
/// (no admin rights), wires up the Start menu / start-with-Windows entries, and can undo all of it.
/// </summary>
public static class Installer
{
    public static string InstallDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ClipVault");

    public static string InstalledExe => Path.Combine(InstallDir, "ClipVault.exe");

    public static string CurrentExe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ClipVault.exe");

    public static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ClipVault.lnk");

    public static bool IsInstalledCopy => PathEquals(CurrentExe, InstalledExe);

    public static Version CurrentVersion => typeof(Installer).Assembly.GetName().Version ?? new Version(0, 0);

    /// <summary>Version of the exe in <see cref="InstallDir"/>, or null when nothing is installed.</summary>
    public static Version? InstalledVersion
    {
        get
        {
            try
            {
                if (!File.Exists(InstalledExe)) return null;
                var info = FileVersionInfo.GetVersionInfo(InstalledExe);
                return Version.TryParse(info.FileVersion, out var v) ? v : new Version(0, 0);
            }
            catch { return null; }
        }
    }

    /// <summary>True when this exe is not the installed one and is either the first copy or newer than the installed copy.</summary>
    public static bool ShouldOffer(Settings settings)
    {
        if (IsInstalledCopy) return false;
        if (PathEquals(settings.InstallPromptDismissedFor, CurrentExe)) return false;
        var installed = InstalledVersion;
        return installed is null || installed < CurrentVersion;
    }

    /// <summary>Copies the running exe into place and sets up the requested integration. Other instances must already be stopped.</summary>
    public static void Install(bool startWithWindows, bool startMenuShortcut)
    {
        Directory.CreateDirectory(InstallDir);
        CopyWithRetry(CurrentExe, InstalledExe);
        StartupRegistration.SetEnabled(startWithWindows, InstalledExe);
        if (startMenuShortcut) CreateShortcut();
        else TryDelete(ShortcutPath);
    }

    public static void Launch(string exe) =>
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });

    /// <summary>Removes the shortcut, the startup entry, optionally the data folder, then deletes the exe after this process exits.</summary>
    public static void Uninstall(bool deleteData)
    {
        StartupRegistration.SetEnabled(false);
        TryDelete(ShortcutPath);
        if (deleteData)
        {
            try { Directory.Delete(Settings.DataDir, recursive: true); } catch (Exception ex) { App.Log("Delete data failed: " + ex.Message); }
        }
        // The running exe cannot delete itself; hand that to a detached cmd that waits for us to exit.
        var script = $"ping 127.0.0.1 -n 3 >nul & del /f /q \"{InstalledExe}\" & rmdir \"{InstallDir}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", "/c " + script) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
    }

    public static void CreateShortcut()
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic link = shell.CreateShortcut(ShortcutPath);
            link.TargetPath = InstalledExe;
            link.WorkingDirectory = InstallDir;
            link.IconLocation = InstalledExe + ",0";
            link.Description = "Clipboard history";
            link.Save();
        }
        catch (Exception ex) { App.Log("Shortcut failed: " + ex.Message); }
    }

    /// <summary>Waits for every other ClipVault process to go away (they were asked to exit via the named event).</summary>
    public static void WaitForOtherInstancesToExit(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!Others().Any()) return;
            Thread.Sleep(100);
        }
        foreach (var p in Others())
        {
            try { p.Kill(); p.WaitForExit(2000); } catch { }
        }

        static Process[] Others() =>
            Process.GetProcessesByName("ClipVault").Where(p => p.Id != Environment.ProcessId).ToArray();
    }

    private static void CopyWithRetry(string from, string to)
    {
        for (int i = 0; ; i++)
        {
            try { File.Copy(from, to, overwrite: true); return; }
            catch (IOException) when (i < 20) { Thread.Sleep(250); }
        }
    }

    private static bool PathEquals(string? a, string? b)
    {
        if (a is null || b is null) return false;
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
