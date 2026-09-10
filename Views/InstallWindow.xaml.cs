using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using ClipVault.Native;
using ClipVault.Services;

namespace ClipVault.Views;

/// <summary>
/// Shown when the exe runs from somewhere other than its install folder. Offers to copy it into the
/// per-user Programs folder (first install) or to replace an older installed copy (update).
/// DialogResult is true when the user chose Install / Update.
/// </summary>
public partial class InstallWindow : Window
{
    public bool StartWithWindows => StartWithWindowsBox.IsChecked == true;
    public bool AddShortcut => ShortcutBox.IsChecked == true;

    public InstallWindow(Version? installedVersion)
    {
        InitializeComponent();

        var here = Path.GetDirectoryName(Installer.CurrentExe) ?? "";
        var current = Trim(Installer.CurrentVersion);
        if (installedVersion is null)
        {
            HeaderText.Text = "Install ClipVault on this PC?";
            BodyText.Text = $"ClipVault is running from {here}. Installing copies it into your user profile so it keeps working " +
                            "if this file is moved or deleted, and lets it start with Windows. No admin rights are needed.";
            InstallButton.Content = "Install";
            SkipButton.Content = "Run from here";
        }
        else
        {
            HeaderText.Text = $"Update ClipVault to {current}?";
            BodyText.Text = $"Version {Trim(installedVersion)} is installed. This copy is {current}. Updating replaces the installed file " +
                            "and keeps your history and settings.";
            InstallButton.Content = "Update";
            SkipButton.Content = "Not now";
            StartWithWindowsBox.IsChecked = StartupRegistration.IsEnabled();
            ShortcutBox.IsChecked = File.Exists(Installer.ShortcutPath);
        }
        LocationText.Text = "Installs to " + Installer.InstallDir;
    }

    private static string Trim(Version v) => v.Build >= 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : v.ToString();

    private void Window_SourceInitialized(object sender, EventArgs e) =>
        NativeMethods.SetDarkTitleBar(new WindowInteropHelper(this).Handle, ThemeManager.IsDark);

    private void Install_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
