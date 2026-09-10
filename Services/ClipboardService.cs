using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using ClipVault.Models;

namespace ClipVault.Services;

/// <summary>Reads the Windows clipboard into a <see cref="ClipItem"/> and writes items back to it.</summary>
public static class ClipboardService
{
    // Formats that well-behaved apps (password managers, etc.) set to opt out of history tools.
    private const string ExcludeFormat = "ExcludeClipboardContentFromMonitorProcessing";
    private const string CanIncludeFormat = "CanIncludeInClipboardHistory";

    /// <summary>Captures the current clipboard. Returns null when there is nothing to store.</summary>
    public static ClipItem? Capture(Settings settings, string imagesDir)
    {
        var data = Retry(System.Windows.Clipboard.GetDataObject);
        if (data is null) return null;

        if (IsExcluded(data)) return null;

        // Prefer files, then text, then images. Many apps (Excel, browsers) put text AND a bitmap
        // on the clipboard; text is what the user meant in those cases.
        if (settings.CaptureFiles && data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = Retry(() => data.GetData(DataFormats.FileDrop) as string[]);
            if (files is { Length: > 0 })
                return FromFiles(files);
        }

        if (data.GetDataPresent(DataFormats.UnicodeText))
        {
            var text = Retry(() => data.GetData(DataFormats.UnicodeText) as string);
            if (!string.IsNullOrEmpty(text))
            {
                if (text.Length > settings.MaxTextChars) return null;
                var item = FromText(text);
                item.Rtf = ReadStringFormat(data, DataFormats.Rtf, settings.MaxTextChars * 4);
                item.Html = ReadStringFormat(data, DataFormats.Html, settings.MaxTextChars * 4);
                return item;
            }
        }

        if (settings.CaptureImages && (data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent(DataFormats.Dib) || data.GetDataPresent("PNG")))
        {
            var bmp = Retry(() => System.Windows.Clipboard.ContainsImage() ? System.Windows.Clipboard.GetImage() : null);
            if (bmp is not null) return FromImage(bmp, imagesDir, settings.MaxImageEdge);
        }

        return null;
    }

    /// <summary>
    /// Puts an item back on the clipboard with all the formats we kept, or, when <paramref name="plainText"/>
    /// is set, with only the plain text so the target app cannot pick up RTF/HTML formatting.
    /// </summary>
    public static bool Apply(ClipItem item, bool plainText = false)
    {
        var d = new DataObject();
        switch (item.Kind)
        {
            case ClipKind.Text:
                d.SetData(DataFormats.UnicodeText, item.Text ?? "");
                d.SetData(DataFormats.Text, item.Text ?? "");
                if (!plainText)
                {
                    if (!string.IsNullOrEmpty(item.Rtf)) d.SetData(DataFormats.Rtf, item.Rtf);
                    if (!string.IsNullOrEmpty(item.Html)) d.SetData(DataFormats.Html, item.Html);
                }
                break;

            case ClipKind.Image:
                if (item.ImagePath is null || !File.Exists(item.ImagePath)) return false;
                var bytes = File.ReadAllBytes(item.ImagePath);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.EndInit();
                bmp.Freeze();
                d.SetImage(bmp);
                d.SetData("PNG", new MemoryStream(bytes));
                break;

            case ClipKind.Files:
                var sc = new StringCollection();
                sc.AddRange((item.Files ?? new()).Where(File.Exists).Concat((item.Files ?? new()).Where(Directory.Exists)).Distinct().ToArray());
                if (sc.Count == 0) return false;
                d.SetFileDropList(sc);
                break;
        }

        return Retry(() => { System.Windows.Clipboard.SetDataObject(d, true); return true; });
    }

    // ---- builders ----

    private static ClipItem FromText(string text) => new()
    {
        Kind = ClipKind.Text,
        Text = text,
        Hash = Sha256("T\0" + text),
    };

    private static ClipItem FromFiles(string[] files) => new()
    {
        Kind = ClipKind.Files,
        Files = files.ToList(),
        Hash = Sha256("F\0" + string.Join("\0", files)),
    };

    private static ClipItem FromImage(BitmapSource bmp, string imagesDir, int maxEdge)
    {
        bmp = ShrinkToFit(bmp, maxEdge);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        var png = ms.ToArray();

        var item = new ClipItem
        {
            Kind = ClipKind.Image,
            ImageWidth = bmp.PixelWidth,
            ImageHeight = bmp.PixelHeight,
            Hash = Sha256Bytes(png),
        };
        item.ImageFile = item.Id + ".png";
        item.ImagePath = Path.Combine(imagesDir, item.ImageFile);
        File.WriteAllBytes(item.ImagePath, png);
        return item;
    }

    // ---- helpers ----

    /// <summary>Scales the bitmap down so its longer edge is at most <paramref name="maxEdge"/> pixels (0 = no limit).</summary>
    private static BitmapSource ShrinkToFit(BitmapSource bmp, int maxEdge)
    {
        int longest = Math.Max(bmp.PixelWidth, bmp.PixelHeight);
        if (maxEdge <= 0 || longest <= maxEdge) return bmp;
        double scale = (double)maxEdge / longest;
        var scaled = new TransformedBitmap(bmp, new System.Windows.Media.ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    private static bool IsExcluded(IDataObject data)
    {
        try
        {
            if (data.GetDataPresent(ExcludeFormat)) return true;
            if (data.GetDataPresent(CanIncludeFormat) && data.GetData(CanIncludeFormat) is MemoryStream ms && ms.Length >= 4)
            {
                var b = new byte[4];
                ms.Position = 0;
                ms.ReadExactly(b, 0, 4);
                if (BitConverter.ToInt32(b, 0) == 0) return true;
            }
        }
        catch { }
        return false;
    }

    private static string? ReadStringFormat(IDataObject data, string format, int maxLength)
    {
        try
        {
            if (!data.GetDataPresent(format)) return null;
            var value = data.GetData(format);
            string? s = value switch
            {
                string str => str,
                MemoryStream ms => Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\0'),
                _ => null,
            };
            return s is { Length: > 0 } && s.Length <= maxLength ? s : null;
        }
        catch { return null; }
    }

    private static string Sha256(string s) => Sha256Bytes(Encoding.UTF8.GetBytes(s));

    private static string Sha256Bytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>The clipboard is frequently still locked by the copying app; retry briefly.</summary>
    private static T? Retry<T>(Func<T?> action, int attempts = 8)
    {
        for (int i = 0; ; i++)
        {
            try { return action(); }
            catch (Exception ex) when (ex is COMException or ExternalException or InvalidOperationException)
            {
                if (i >= attempts - 1) { App.Log("Clipboard access failed: " + ex.Message); return default; }
                Thread.Sleep(30 * (i + 1));
            }
        }
    }
}
