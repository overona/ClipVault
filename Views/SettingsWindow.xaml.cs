using System;
using System.Windows;
using System.Windows.Input;
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
        AutoPasteBox.IsChecked = settings.AutoPaste;
        CaptureImagesBox.IsChecked = settings.CaptureImages;
        CaptureFilesBox.IsChecked = settings.CaptureFiles;
        RunAtLoginBox.IsChecked = StartupRegistration.IsEnabled();
        DataLocationText.Text = "History is stored in " + Settings.DataDir;
    }

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

        _settings.HotkeyModifiers = _mods;
        _settings.HotkeyKey = _key;
        _settings.MaxItems = max;
        _settings.AutoPaste = AutoPasteBox.IsChecked == true;
        _settings.CaptureImages = CaptureImagesBox.IsChecked == true;
        _settings.CaptureFiles = CaptureFilesBox.IsChecked == true;

        try { StartupRegistration.SetEnabled(RunAtLoginBox.IsChecked == true); }
        catch (Exception ex) { App.Log("Startup registration failed: " + ex); }

        DialogResult = true;
    }
}
