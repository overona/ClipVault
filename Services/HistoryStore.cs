using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using ClipVault.Models;

namespace ClipVault.Services;

/// <summary>
/// In-memory history backed by a JSON file. Images live as PNG files next to it.
/// Newest / most recently used items are kept at the front of <see cref="Items"/>.
/// </summary>
public sealed class HistoryStore
{
    private readonly string _historyFile;
    private readonly DispatcherTimer _saveTimer;
    private bool _dirty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ObservableCollection<ClipItem> Items { get; } = new();
    public string ImagesDir { get; }
    public int MaxItems { get; set; } = 500;

    public HistoryStore(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        ImagesDir = Path.Combine(dataDir, "images");
        Directory.CreateDirectory(ImagesDir);
        _historyFile = Path.Combine(dataDir, "history.json");

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
    }

    public void Load()
    {
        Items.Clear();
        if (!File.Exists(_historyFile)) return;
        List<ClipItem>? list = null;
        try
        {
            list = JsonSerializer.Deserialize<List<ClipItem>>(File.ReadAllText(_historyFile), JsonOptions);
        }
        catch (Exception ex)
        {
            App.Log("Failed to read history: " + ex);
            try { File.Copy(_historyFile, _historyFile + ".corrupt", overwrite: true); } catch { }
        }
        if (list is null) return;

        foreach (var item in list.OrderByDescending(i => i.Pinned).ThenByDescending(i => i.LastUsedUtc))
        {
            if (item.Kind == ClipKind.Image)
            {
                if (item.ImageFile is null) continue;
                item.ImagePath = Path.Combine(ImagesDir, item.ImageFile);
                if (!File.Exists(item.ImagePath)) continue;
            }
            Items.Add(item);
        }
        CleanupOrphanImages();
    }

    /// <summary>Adds a freshly captured item, or if identical content already exists, moves that to the top.</summary>
    public ClipItem AddOrTouch(ClipItem incoming)
    {
        var existing = FindByHash(incoming.Hash);
        if (existing is not null)
        {
            if (incoming.Kind == ClipKind.Image && incoming.ImagePath is not null && incoming.ImagePath != existing.ImagePath)
                TryDelete(incoming.ImagePath);
            // Keep richer formats if the new copy has them.
            if (incoming.Kind == ClipKind.Text)
            {
                existing.Rtf ??= incoming.Rtf;
                existing.Html ??= incoming.Html;
            }
            existing.SourceApp = incoming.SourceApp ?? existing.SourceApp;
            Touch(existing, countAsUse: false);
            return existing;
        }

        Items.Insert(InsertIndexFor(incoming), incoming);
        Trim();
        MarkDirty();
        return incoming;
    }

    /// <summary>Moves an item to the top of its group (pinned / unpinned) and updates its timestamp.</summary>
    public void Touch(ClipItem item, bool countAsUse)
    {
        item.LastUsedUtc = DateTime.UtcNow;
        if (countAsUse) item.UseCount++;
        MoveToGroupTop(item);
        MarkDirty();
    }

    public void SetPinned(ClipItem item, bool pinned)
    {
        if (item.Pinned == pinned) return;
        item.Pinned = pinned;
        MoveToGroupTop(item);
        MarkDirty();
    }

    public void Remove(ClipItem item)
    {
        if (Items.Remove(item))
        {
            if (item.ImagePath is not null) TryDelete(item.ImagePath);
            MarkDirty();
        }
    }

    public void Clear(bool keepPinned)
    {
        foreach (var item in Items.Where(i => !(keepPinned && i.Pinned)).ToList())
        {
            Items.Remove(item);
            if (item.ImagePath is not null) TryDelete(item.ImagePath);
        }
        MarkDirty();
    }

    public ClipItem? FindByHash(string hash) => Items.FirstOrDefault(i => i.Hash == hash);

    public void Trim()
    {
        // Remove the oldest unpinned items beyond the cap.
        while (Items.Count > MaxItems)
        {
            var victim = Items.LastOrDefault(i => !i.Pinned);
            if (victim is null) break;
            Remove(victim);
        }
    }

    private int InsertIndexFor(ClipItem item)
    {
        if (item.Pinned) return 0;
        int i = 0;
        while (i < Items.Count && Items[i].Pinned) i++;
        return i;
    }

    private void MoveToGroupTop(ClipItem item)
    {
        int from = Items.IndexOf(item);
        if (from < 0) return;
        Items.RemoveAt(from);
        Items.Insert(InsertIndexFor(item), item);
    }

    private void MarkDirty()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        if (!_dirty) return;
        try
        {
            var tmp = _historyFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Items.ToList(), JsonOptions));
            File.Move(tmp, _historyFile, overwrite: true);
            _dirty = false;
        }
        catch (Exception ex)
        {
            App.Log("Failed to save history: " + ex);
        }
    }

    private void CleanupOrphanImages()
    {
        try
        {
            var used = new HashSet<string>(Items.Where(i => i.ImageFile != null).Select(i => i.ImageFile!), StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(ImagesDir, "*.png"))
                if (!used.Contains(Path.GetFileName(file))) TryDelete(file);
        }
        catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
