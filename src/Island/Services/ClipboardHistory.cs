using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Island.Services;

public sealed class ClipEntry
{
    public string Text { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTime At { get; set; } = DateTime.Now;
    public bool Pinned { get; set; }
}

/// <summary>Clipboard history with de-duplication, pinning and a size cap.</summary>
public sealed class ClipboardHistory
{
    public const int MaxUnpinned = 100;
    public const int MaxTextLength = 20_000;

    /// <summary>Unpinned copies older than this are dropped on startup; pinned ones stay.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    private readonly List<ClipEntry> _items;
    private readonly string? _path;

    /// <param name="path">Encrypted file (DPAPI, current user only). Plain-text history from older builds is migrated once.</param>
    public ClipboardHistory(string? path = null)
    {
        _path = path;
        if (path is null)
        {
            _items = new List<ClipEntry>();
            return;
        }

        _items = LoadEncrypted(path) ?? new List<ClipEntry>();
        _items.RemoveAll(e => !e.Pinned && e.At < DateTime.Now - Retention);

        var legacy = Path.ChangeExtension(path, ".json");
        if (File.Exists(legacy))
        {
            Persist();
            try { File.Delete(legacy); } catch { }
        }
    }

    private static List<ClipEntry>? LoadEncrypted(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<List<ClipEntry>>(plain);
            }
        }
        catch
        {
            // Unreadable, or written by another Windows account: start with an empty history.
        }

        var legacy = Path.ChangeExtension(path, ".json");
        return File.Exists(legacy) ? JsonStore.TryLoad<List<ClipEntry>>(legacy) : null;
    }

    public event Action? Changed;

    public IReadOnlyList<ClipEntry> Items => _items;

    public bool Add(string? text, string source, DateTime? now = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Length > MaxTextLength) text = text[..MaxTextLength];

        var existing = _items.FirstOrDefault(e => e.Text == text);
        var pinned = false;
        if (existing is not null)
        {
            pinned = existing.Pinned;
            _items.Remove(existing);
        }

        _items.Insert(0, new ClipEntry
        {
            Text = text,
            Source = source,
            At = now ?? DateTime.Now,
            Pinned = pinned,
        });

        Trim();
        Persist();
        return true;
    }

    public void TogglePin(ClipEntry entry)
    {
        entry.Pinned = !entry.Pinned;
        Persist();
    }

    public void Remove(ClipEntry entry)
    {
        if (_items.Remove(entry)) Persist();
    }

    public void ClearUnpinned()
    {
        if (_items.RemoveAll(e => !e.Pinned) > 0) Persist();
    }

    /// <summary>Pinned first, then newest first; filtered by a case-insensitive substring.</summary>
    public List<ClipEntry> Filter(string? query)
    {
        IEnumerable<ClipEntry> q = _items;
        if (!string.IsNullOrWhiteSpace(query))
        {
            var needle = query.Trim();
            q = q.Where(e => e.Text.Contains(needle, StringComparison.CurrentCultureIgnoreCase));
        }

        return q.OrderByDescending(e => e.Pinned).ThenByDescending(e => e.At).ToList();
    }

    private void Trim()
    {
        var overflow = _items.Where(e => !e.Pinned).Skip(MaxUnpinned).ToList();
        foreach (var e in overflow) _items.Remove(e);
    }

    private void Persist()
    {
        if (_path is not null) SaveEncrypted(_path, _items);
        Changed?.Invoke();
    }

    private static void SaveEncrypted(string path, List<ClipEntry> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(items), null, DataProtectionScope.CurrentUser);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // Best-effort, like JsonStore: the history keeps working from memory.
        }
    }
}

public static class TimeLabels
{
    public static string Ago(DateTime at, DateTime now)
    {
        var d = now - at;
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        if (d.TotalSeconds < 45) return Loc.T("сейчас");
        if (d.TotalMinutes < 60) return Loc.F("{0} мин", (int)Math.Max(1, d.TotalMinutes));
        if (d.TotalHours < 24) return Loc.F("{0} ч", (int)d.TotalHours);
        if (d.TotalDays < 2) return Loc.T("вчера");
        return Loc.F("{0} дн", (int)d.TotalDays);
    }

    public static string Clock(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
}
