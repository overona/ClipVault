using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace ClipVault.Models;

public enum ClipKind { Text, Image, Files }

public sealed class ClipItem : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ClipKind Kind { get; set; }
    public string Hash { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string? SourceApp { get; set; }

    private DateTime _lastUsedUtc = DateTime.UtcNow;
    public DateTime LastUsedUtc
    {
        get => _lastUsedUtc;
        set { if (_lastUsedUtc != value) { _lastUsedUtc = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimeAgo)); } }
    }

    private bool _pinned;
    public bool Pinned
    {
        get => _pinned;
        set { if (_pinned != value) { _pinned = value; OnPropertyChanged(); } }
    }

    private int _useCount;
    public int UseCount
    {
        get => _useCount;
        set { if (_useCount != value) { _useCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(UsageText)); } }
    }

    // Text
    public string? Text { get; set; }
    public string? Rtf { get; set; }
    public string? Html { get; set; }

    // Image (stored as PNG under the images folder; ImageFile is the file name only)
    public string? ImageFile { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }

    // Files
    public List<string>? Files { get; set; }

    // ---- Display helpers (not persisted) ----

    /// <summary>Absolute path of the PNG on disk, resolved at load time.</summary>
    [JsonIgnore] public string? ImagePath { get; set; }

    [JsonIgnore]
    public string Preview
    {
        get
        {
            switch (Kind)
            {
                case ClipKind.Text:
                    var t = (Text ?? "").TrimStart('\r', '\n');
                    return t.Length > 400 ? t[..400] : t;
                case ClipKind.Image:
                    return $"Image  {ImageWidth} x {ImageHeight}";
                default:
                    return string.Join("\n", (Files ?? new()).Select(Path.GetFileName));
            }
        }
    }

    [JsonIgnore] public int CharCount => Text?.Length ?? 0;
    [JsonIgnore] public int LineCount => string.IsNullOrEmpty(Text) ? 0 : Text.Count(c => c == '\n') + 1;

    [JsonIgnore]
    public string Details => Kind switch
    {
        ClipKind.Text => $"{CharCount:N0} chars, {LineCount:N0} line{(LineCount == 1 ? "" : "s")}",
        ClipKind.Image => $"{ImageWidth} x {ImageHeight} px",
        _ => $"{Files?.Count ?? 0} file{((Files?.Count ?? 0) == 1 ? "" : "s")}",
    };

    [JsonIgnore]
    public string UsageText => UseCount == 0 ? "never reused" : $"reused {UseCount}x";

    [JsonIgnore]
    public string KindLabel => Kind switch { ClipKind.Text => "Text", ClipKind.Image => "Image", _ => "Files" };

    /// <summary>Segoe MDL2 Assets glyph for the kind.</summary>
    [JsonIgnore]
    public string Glyph => Kind switch { ClipKind.Text => "", ClipKind.Image => "", _ => "" };

    [JsonIgnore]
    public string TimeAgo
    {
        get
        {
            var span = DateTime.UtcNow - LastUsedUtc;
            if (span.TotalSeconds < 45) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays} d ago";
            return LastUsedUtc.ToLocalTime().ToString("MMM d");
        }
    }

    [JsonIgnore] public string CopiedAtLocal => CreatedUtc.ToLocalTime().ToString("g");

    private BitmapSource? _thumbnail;
    [JsonIgnore]
    public BitmapSource? Thumbnail => _thumbnail ??= LoadImage(240);

    private BitmapSource? _fullImage;
    [JsonIgnore]
    public BitmapSource? FullImage => _fullImage ??= LoadImage(0);

    private BitmapSource? LoadImage(int decodeWidth)
    {
        if (Kind != ClipKind.Image || ImagePath is null || !File.Exists(ImagePath)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(ImagePath);
            if (decodeWidth > 0 && ImageWidth > decodeWidth) bmp.DecodePixelWidth = decodeWidth;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    public void RefreshTimeAgo() => OnPropertyChanged(nameof(TimeAgo));

    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var cmp = StringComparison.OrdinalIgnoreCase;
        bool source = SourceApp?.Contains(query, cmp) ?? false;
        return source || Kind switch
        {
            ClipKind.Text => Text?.Contains(query, cmp) == true,
            ClipKind.Image => "image".Contains(query, cmp),
            _ => Files?.Any(f => f.Contains(query, cmp)) == true,
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
