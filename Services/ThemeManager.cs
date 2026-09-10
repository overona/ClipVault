using System;
using System.Windows;
using System.Windows.Media;

namespace ClipVault.Services;

/// <summary>
/// Swaps the palette brushes in <c>Application.Resources</c> between the light and dark sets.
/// Every XAML reference to a palette key uses <c>DynamicResource</c>, so a swap re-renders live.
/// </summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    /// <summary>Raised on the UI thread after the palette changes.</summary>
    public static event Action? Changed;

    private static readonly (string Key, string Light, string Dark)[] Palette =
    {
        ("WindowBg",     "#F7F8FA", "#1B1D21"),
        ("PanelBg",      "#FFFFFF", "#24272C"),
        ("Border",       "#D9DEE5", "#3A3F47"),
        ("Text",         "#1F2937", "#E6E8EB"),
        ("Muted",        "#6B7280", "#9AA1AB"),
        ("Accent",       "#2563EB", "#3B82F6"),
        ("AccentHover",  "#1D4ED8", "#2563EB"),
        ("AccentSoft",   "#DBEAFE", "#1E3A5F"),
        ("AccentBorder", "#BFDBFE", "#2F5A9E"),
        ("Hover",        "#EEF2F7", "#2E3238"),
        ("Danger",       "#DC2626", "#F87171"),
        ("ScrollThumb",  "#C4CAD3", "#4B515B"),
        ("CodeBg",       "#F3F4F6", "#1A1C20"),
    };

    public static void Apply(AppTheme theme)
    {
        bool dark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => SystemUsesDarkApps(),
        };

        var res = Application.Current.Resources;
        foreach (var (key, light, darkColor) in Palette)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? darkColor : light));
            brush.Freeze();
            res[key] = brush;
        }
        IsDark = dark;
        Changed?.Invoke();
    }

    /// <summary>Reads the Windows "Choose your default app mode" setting.</summary>
    public static bool SystemUsesDarkApps()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", writable: false);
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }
}
