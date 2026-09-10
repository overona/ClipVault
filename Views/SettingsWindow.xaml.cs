using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ClipVault.Native;
using ClipVault.Services;

namespace ClipVault.Views;

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private ModifierKeys _mods;
    private Key _key;

    public SettingsWindow(Settings settings)
    {
        _settings = settings;
        InitializeComponent();

        _mods = settings.HotkeyModifiers;
        _key = settings.HotkeyKey;
        HotkeyBox.Text = Settings.FormatHotkey(_mods, _key);
        MaxItemsBox.Text = settings.MaxItems.ToString();
        MaxImageEdgeBox.Text = settings.MaxImageEdge.ToString();
        AutoPasteBox.IsChecked = settings.AutoPaste;
        CaptureImagesBox.IsChecked = settings.CaptureImages;
        CaptureFilesBox.IsChecked = settings.CaptureFiles;
        RunAtLoginBox.IsChecked = StartupRegistration.IsEnabled();
        ExcludedAppsBox.Text = string.Join(Environment.NewLine, settings.ExcludedApps);
        (settings.Theme switch
        {
            AppTheme.Light => ThemeLightBox,
            AppTheme.Dark => ThemeDarkBox,
            _ => ThemeSystemBox,
        }).IsChecked = true;
        DataLocationText.Text = "History is stored in " + Settings.DataDir;
    }

    private void Window_SourceInitialized(object sender, EventArgs e) =>
        NativeMethods.SetDarkTitleBar(new WindowInteropHelper(this).Handle, ThemeManager.IsDark);

    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    private void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e) => HotkeyBox.Text = "Press a key combination...";

    private void HotkeyBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => HotkeyBox.Text = Settings.FormatHotkey(_mods, _key);

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (key)
        {
            case Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                 or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None:
                return; // wait for the non-modifier key
            case Key.Tab when Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = false; // allow normal focus navigation
                return;
            case Key.Escape when Keyboard.Modifiers == ModifierKeys.None:
                HotkeyBox.Text = Settings.FormatHotkey(_mods, _key);
                return;
        }

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None)
        {
            HotkeyBox.Text = "Add Ctrl, Alt, Shift or Win";
            return;
        }
        _mods = mods;
        _key = key;
        HotkeyBox.Text = Settings.FormatHotkey(_mods, _key);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MaxItemsBox.Text.Trim(), out int max) || max < 10 || max > 10000)
        {
            MessageBox.Show(this, "History size must be a number between 10 and 10000.", "ClipVault", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(MaxImageEdgeBox.Text.Trim(), out int maxEdge) || maxEdge < 0 || (maxEdge > 0 && maxEdge < 256))
        {
            MessageBox.Show(this, "The image size limit must be 0 (no limit) or at least 256 pixels.", "ClipVault", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.HotkeyModifiers = _mods;
        _settings.HotkeyKey = _key;
        _settings.MaxItems = max;
        _settings.MaxImageEdge = maxEdge;
        _settings.AutoPaste = AutoPasteBox.IsChecked == true;
        _settings.CaptureImages = CaptureImagesBox.IsChecked == true;
        _settings.CaptureFiles = CaptureFilesBox.IsChecked == true;
        _settings.Theme = ThemeDarkBox.IsChecked == true ? AppTheme.Dark
                        : ThemeLightBox.IsChecked == true ? AppTheme.Light
                        : AppTheme.System;
        _settings.ExcludedApps = ExcludedAppsBox.Text
            .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(a => a.Trim())
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        try { StartupRegistration.SetEnabled(RunAtLoginBox.IsChecked == true); }
        catch (Exception ex) { App.Log("Startup registration failed: " + ex); }

        DialogResult = true;
    }
}
