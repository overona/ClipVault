using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using ClipVault.Native;
using ClipVault.Services;

namespace ClipVault.Views;

public partial class AboutWindow : Window
{
    public const string Author = "Ovidio Verona";

    private const string Disclaimer =
        "ClipVault is provided free of charge, \"as is\", without warranty of any kind, express or implied, including but not " +
        "limited to the warranties of merchantability, fitness for a particular purpose and non-infringement. In no event shall " +
        "the author be liable for any claim, damages or other liability, whether in an action of contract, tort or otherwise, " +
        "arising from, out of or in connection with the software or the use of it.\n\n" +
        "ClipVault records what you copy. Anything an application does not mark as private, including passwords or card numbers " +
        "copied from ordinary windows, is stored in your history like any other item. Review and delete items as needed, and add " +
        "your password manager to the excluded apps in Settings. You are responsible for the content kept on this computer.";

    public AboutWindow()
    {
        InitializeComponent();
        var v = Installer.CurrentVersion;
        VersionText.Text = $"Version {v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}" + (Installer.IsInstalledCopy ? "  ·  installed" : "  ·  portable");
        AuthorText.Text = $"Created by {Author}  ·  Copyright © {DateTime.Now.Year} {Author}";
        DisclaimerText.Text = Disclaimer;
    }

    private void Window_SourceInitialized(object sender, EventArgs e) =>
        NativeMethods.SetDarkTitleBar(new WindowInteropHelper(this).Handle, ThemeManager.IsDark);

    private void DataFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Settings.DataDir}\"") { UseShellExecute = true }); }
        catch (Exception ex) { App.Log("Open data folder failed: " + ex.Message); }
    }
}
