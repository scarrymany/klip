using Klip.Models;
using Klip.Services;
using Microsoft.Data.Sqlite;

namespace Klip.Tests;

public sealed class ClipStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _db;

    public ClipStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "KlipTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = Path.Combine(_dir, "klip.db");
    }

    [Fact]
    public void Seeds_welcome_note_once()
    {
        using (var store = new ClipStore(_db))
        {
            var first = store.ListClips();
            Assert.Single(first);
            Assert.Equal(ClipSources.Manual, first[0].Source);
        }

        using (var store = new ClipStore(_db))
            Assert.Single(store.ListClips());
    }

    [Fact]
    public void Consecutive_duplicate_clipboard_is_ignored()
    {
        using var store = new ClipStore(_db);
        Assert.NotNull(store.TryAddFromClipboard("hello"));
        Assert.Null(store.TryAddFromClipboard("hello"));
        Assert.Equal(2, store.ListClips().Count);
    }

    [Fact]
    public void Older_duplicate_is_promoted_instead_of_copied()
    {
        using var store = new ClipStore(_db);
        var first = store.TryAddFromClipboard("alpha");
        Assert.NotNull(first);
        Assert.NotNull(store.TryAddFromClipboard("beta"));
        var again = store.TryAddFromClipboard("alpha");
        Assert.NotNull(again);
        Assert.Equal(first!.Id, again!.Id);

        var listed = store.ListClips();
        Assert.Equal(3, listed.Count);
        Assert.Equal(first.Id, listed.First(c => !c.Pinned).Id);
    }

    [Fact]
    public void Trim_keeps_manual_notes_and_pinned()
    {
        using var store = new ClipStore(_db);
        var note = store.AddNote("keep", "manual note body", ClipKinds.Note, ClipColors.None, null);
        store.SetPinned(note.Id, true);

        for (var i = 0; i < ClipStore.HistoryLimit + 20; i++)
            store.TryAddFromClipboard($"clip-{i}");

        var items = store.ListClips();
        Assert.True(items.Count <= ClipStore.HistoryLimit);
        Assert.Contains(items, c => c.Id == note.Id);
    }

    [Fact]
    public void Search_finds_text_beyond_list_preview()
    {
        using var store = new ClipStore(_db);
        var hidden = new string('x', 700) + "UNIQUE_TOKEN_ZZZ";
        var added = store.TryAddFromClipboard(hidden);
        Assert.NotNull(added);
        Assert.False(store.ListClips().Single(c => c.Id == added!.Id).HasFullContent);

        var ids = store.SearchIds("UNIQUE_TOKEN_ZZZ");
        Assert.Contains(added!.Id, ids);

        var full = store.GetById(added.Id);
        Assert.NotNull(full);
        Assert.True(full!.HasFullContent);
        Assert.Contains("UNIQUE_TOKEN_ZZZ", full.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Like_wildcards_are_literal()
    {
        using var store = new ClipStore(_db);
        store.TryAddFromClipboard("100% done");
        Assert.Single(store.SearchIds("100%"));
        Assert.Empty(store.SearchIds("100_"));
    }

    [Fact]
    public void Migrates_legacy_schema()
    {
        using (var raw = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _db }.ToString()))
        {
            raw.Open();
            using var cmd = raw.CreateCommand();
            cmd.CommandText =
                """
                CREATE TABLE clips (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    collection_id INTEGER,
                    title TEXT,
                    content TEXT NOT NULL,
                    kind TEXT NOT NULL DEFAULT 'clip',
                    color TEXT NOT NULL DEFAULT 'none',
                    pinned INTEGER NOT NULL DEFAULT 0,
                    copy_count INTEGER NOT NULL DEFAULT 0,
                    last_copied_at TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                INSERT INTO clips (title, content, kind, color, pinned, copy_count, created_at, updated_at)
                VALUES ('Old note', 'legacy body', 'note', 'none', 0, 0, '2026-01-01T00:00:00.0000000Z', '2026-01-01T00:00:00.0000000Z');
                """;
            cmd.ExecuteNonQuery();
        }

        using var store = new ClipStore(_db);
        var item = Assert.Single(store.ListClips());
        Assert.Equal(ClipSources.Manual, item.Source);
        Assert.Equal("Old note", item.Title);
        Assert.False(string.IsNullOrEmpty(ClipStore.HashContent("legacy body")));
    }

    [Fact]
    public void Normalize_caps_length_and_newlines()
    {
        var text = ClipStore.Normalize("a\r\nb\rc" + new string('z', ClipStore.MaxContentLength));
        Assert.DoesNotContain('\r', text);
        Assert.Equal(ClipStore.MaxContentLength, text.Length);
        Assert.StartsWith("a\nb\nc", text, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // Temp leftovers are fine.
        }
    }
}
