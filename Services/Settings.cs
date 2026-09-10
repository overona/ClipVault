using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using ClipVault.Native;

namespace ClipVault.Services;

public enum AppTheme { System, Light, Dark }

public sealed class Settings
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipVault");

    private static string FilePath => Path.Combine(DataDir, "settings.json");

    public ModifierKeys HotkeyModifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Shift;
    public Key HotkeyKey { get; set; } = Key.V;
    public int MaxItems { get; set; } = 500;
    public bool AutoPaste { get; set; } = true;
    public bool CaptureImages { get; set; } = true;
    public bool CaptureFiles { get; set; } = true;
    public int MaxTextChars { get; set; } = 500_000;
    public bool FirstRunShown { get; set; }

    /// <summary>Full path of an exe for which the user chose "Run from here" / "Not now" in the install prompt.</summary>
    public string? InstallPromptDismissedFor { get; set; }

    /// <summary>Light / dark palette, or follow the Windows "app mode" setting.</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Images whose longer edge exceeds this many pixels are shrunk on capture. 0 keeps the original size.</summary>
    public int MaxImageEdge { get; set; } = 4096;

    /// <summary>Process names (with or without ".exe") whose copies are never recorded, e.g. a password manager.</summary>
    public List<string> ExcludedApps { get; set; } = new();

    [JsonIgnore]
    public string HotkeyText => FormatHotkey(HotkeyModifiers, HotkeyKey);

    public bool IsExcludedApp(string? processName)
    {
        if (string.IsNullOrEmpty(processName)) return false;
        foreach (var raw in ExcludedApps)
        {
            var name = raw.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            if (name.Length > 0 && string.Equals(name, processName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static string FormatHotkey(ModifierKeys mods, Key key)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    [JsonIgnore]
    public uint NativeModifiers
    {
        get
        {
            uint m = NativeMethods.MOD_NOREPEAT;
            if (HotkeyModifiers.HasFlag(ModifierKeys.Control)) m |= NativeMethods.MOD_CONTROL;
            if (HotkeyModifiers.HasFlag(ModifierKeys.Shift)) m |= NativeMethods.MOD_SHIFT;
            if (HotkeyModifiers.HasFlag(ModifierKeys.Alt)) m |= NativeMethods.MOD_ALT;
            if (HotkeyModifiers.HasFlag(ModifierKeys.Windows)) m |= NativeMethods.MOD_WIN;
            return m;
        }
    }

    [JsonIgnore]
    public uint NativeKey => (uint)KeyInterop.VirtualKeyFromKey(HotkeyKey);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions) ?? new Settings();
                s.ExcludedApps ??= new();
                s.ExcludedApps = s.ExcludedApps.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList();
                return s;
            }
        }
        catch { /* fall through to defaults */ }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}

/// <summary>Toggles the HKCU Run entry so the app starts at login.</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipVault";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string;
        }
        catch { return false; }
    }

    /// <param name="exe">Exe to register; defaults to the running one.</param>
    public static void SetEnabled(bool enabled, string? exe = null)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (key is null) return;
        if (enabled)
        {
            exe ??= Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "ClipVault.exe");
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Path currently registered, or null.</summary>
    public static string? RegisteredExe()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string s ? s.Trim().Trim('"') : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// If start-with-Windows points at an exe that no longer exists (the file was moved), re-point it at the
    /// running exe so the setting keeps working without the user noticing.
    /// </summary>
    public static void RepairIfStale()
    {
        try
        {
            var registered = RegisteredExe();
            if (registered is null || System.IO.File.Exists(registered)) return;
            SetEnabled(true);
            App.Log($"Start-with-Windows entry pointed at missing '{registered}'; re-pointed at '{Environment.ProcessPath}'.");
        }
        catch (Exception ex) { App.Log("Startup repair failed: " + ex.Message); }
    }
}
