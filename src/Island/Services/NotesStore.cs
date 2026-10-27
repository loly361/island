using System;
using System.Collections.Generic;
using System.Linq;

namespace Island.Services;

public sealed class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Rtf { get; set; } = "";
    public DateTime Updated { get; set; } = DateTime.Now;
}

/// <summary>Formatted notes persisted as RTF so bold, italics and lists survive restarts.</summary>
public sealed class NotesStore
{
    private readonly List<Note> _items;
    private readonly string? _path;

    public NotesStore(string? path = null)
    {
        _path = path;
        _items = path is null ? new List<Note>() : JsonStore.Load(path, () => new List<Note>());
    }

    public List<Note> Items => _items.OrderByDescending(n => n.Updated).ToList();

    public Note Create()
    {
        var note = new Note();
        _items.Add(note);
        Persist();
        return note;
    }

    public void Save(Note note, string title, string rtf)
    {
        note.Title = title;
        note.Rtf = rtf;
        note.Updated = DateTime.Now;
        Persist();
    }

    public void Delete(Note note)
    {
        if (_items.Remove(note)) Persist();
    }

    private void Persist()
    {
        if (_path is not null) JsonStore.Save(_path, _items);
    }
}
