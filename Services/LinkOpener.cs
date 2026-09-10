using System;
using System.Diagnostics;
using System.IO;

namespace ClipVault.Services;

/// <summary>Opens URLs, files and folders with whatever Windows associates with them.</summary>
public static class LinkOpener
{
    public static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Log($"Open '{target}' failed: {ex.Message}");
            System.Windows.MessageBox.Show($"Could not open:\n{target}\n\n{ex.Message}", "ClipVault",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>Opens Explorer with the item selected; falls back to the parent folder when the item is gone.</summary>
    public static void ShowInFolder(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else
            {
                var parent = Path.GetDirectoryName(path);
                if (parent is not null && Directory.Exists(parent))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{parent}\"") { UseShellExecute = true });
                else
                    System.Windows.MessageBox.Show($"The file is no longer there:\n{path}", "ClipVault",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
        }
        catch (Exception ex) { App.Log($"Show in folder '{path}' failed: {ex.Message}"); }
    }
}
