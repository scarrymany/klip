using Klip.Models;
using Klip.Services;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

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
        Assert.Null(store.TryAddFromClipboard("alpha"));
    }

    [Fact]
    public void Saves_image_as_hashed_png_and_returns_absolute_path()
    {
        var bytes = ImageBytes(42);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        using var store = new ClipStore(_db);
        var added = store.TryAddImageFromClipboard(bytes, 1920, 1080);

        Assert.NotNull(added);
        Assert.Equal(ClipKinds.Image, added!.Kind);
        Assert.Equal("", added.Content);
        Assert.Equal(1920, added.ImageWidth);
        Assert.Equal(1080, added.ImageHeight);
        Assert.True(Path.IsPathFullyQualified(added.ImagePath!));
        Assert.Equal(Path.Combine(_dir, "images", hash + ".png"), added.ImagePath);
        Assert.Equal(bytes, File.ReadAllBytes(added.ImagePath!));

        var listed = Assert.Single(store.ListClips(), item => item.Id == added.Id);
        Assert.Equal(added.ImagePath, listed.ImagePath);
        Assert.Equal(1920, listed.ImageWidth);
        Assert.Equal(1080, listed.ImageHeight);
    }

    [Fact]
    public void Consecutive_image_duplicate_is_ignored()
    {
        using var store = new ClipStore(_db);
        var bytes = ImageBytes(1);

        Assert.NotNull(store.TryAddImageFromClipboard(bytes, 10, 20));
        Assert.Null(store.TryAddImageFromClipboard(bytes, 10, 20));
        Assert.Single(store.ListClips(), item => item.IsImage);
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "images"), "*.png"));
    }

    [Fact]
    public void Older_image_duplicate_is_promoted_and_updates_dimensions()
    {
        using var store = new ClipStore(_db);
        var first = store.TryAddImageFromClipboard(ImageBytes(1), 10, 20);
        Assert.NotNull(first);
        Assert.NotNull(store.TryAddImageFromClipboard(ImageBytes(2), 30, 40));

        var promoted = store.TryAddImageFromClipboard(ImageBytes(1), 50, 60);

        Assert.NotNull(promoted);
        Assert.Equal(first!.Id, promoted!.Id);
        Assert.Equal(50, promoted.ImageWidth);
        Assert.Equal(60, promoted.ImageHeight);
        Assert.Equal(2, store.ListClips().Count(item => item.IsImage));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_dir, "images"), "*.png").Length);
        Assert.Null(store.TryAddImageFromClipboard(ImageBytes(1), 50, 60));
    }

    [Fact]
    public void Delete_removes_image_file_after_row()
    {
        using var store = new ClipStore(_db);
        var image = store.TryAddImageFromClipboard(ImageBytes(3), 10, 20);
        Assert.NotNull(image);
        Assert.True(File.Exists(image!.ImagePath));

        store.Delete(image.Id);

        Assert.Null(store.GetById(image.Id));
        Assert.False(File.Exists(image.ImagePath));
    }

    [Fact]
    public void Startup_removes_orphaned_images_and_temporary_files()
    {
        string imagePath;
        using (var store = new ClipStore(_db))
        {
            var image = store.TryAddImageFromClipboard(ImageBytes(4), 10, 20);
            Assert.NotNull(image);
            imagePath = image!.ImagePath!;
        }

        using (var raw = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _db }.ToString()))
        {
            raw.Open();
            using var delete = raw.CreateCommand();
            delete.CommandText = "DELETE FROM clips WHERE kind = 'image'";
            delete.ExecuteNonQuery();
        }
        var tempPath = Path.Combine(_dir, "images", ".interrupted.tmp");
        File.WriteAllBytes(tempPath, ImageBytes(5));

        using var reopened = new ClipStore(_db);

        Assert.False(File.Exists(imagePath));
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public void Image_limit_keeps_pinned_and_only_one_hundred_unpinned()
    {
        using var store = new ClipStore(_db);
        var pinned = store.TryAddImageFromClipboard(ImageBytes(0), 10, 20);
        Assert.NotNull(pinned);
        store.SetPinned(pinned!.Id, true);

        var oldestUnpinned = store.TryAddImageFromClipboard(ImageBytes(1), 10, 20);
        Assert.NotNull(oldestUnpinned);
        for (var i = 2; i <= ClipStore.ImageHistoryLimit + 1; i++)
            Assert.NotNull(store.TryAddImageFromClipboard(ImageBytes(i), 10, 20));

        var images = store.ListClips().Where(item => item.IsImage).ToList();
        Assert.Equal(ClipStore.ImageHistoryLimit, images.Count(item => !item.Pinned));
        Assert.Contains(images, item => item.Id == pinned.Id && item.Pinned);
        Assert.DoesNotContain(images, item => item.Id == oldestUnpinned!.Id);
        Assert.True(File.Exists(pinned.ImagePath));
        Assert.False(File.Exists(oldestUnpinned!.ImagePath));
    }

    [Fact]
    public void Oversized_or_invalid_image_is_rejected()
    {
        using var store = new ClipStore(_db);

        Assert.Null(store.TryAddImageFromClipboard([], 10, 20));
        Assert.Null(store.TryAddImageFromClipboard(ImageBytes(1), 0, 20));
        Assert.Null(store.TryAddImageFromClipboard(new byte[ClipStore.MaxImageBytes + 1], 10, 20));
        Assert.False(Directory.Exists(Path.Combine(_dir, "images")));
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

        using (var store = new ClipStore(_db))
        {
            var item = Assert.Single(store.ListClips());
            Assert.Equal(ClipSources.Manual, item.Source);
            Assert.Equal("Old note", item.Title);
            Assert.Equal("legacy body", item.Content);
            Assert.False(string.IsNullOrEmpty(ClipStore.HashContent("legacy body")));
        }

        using var migrated = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _db }.ToString());
        migrated.Open();
        using (var columns = migrated.CreateCommand())
        {
            columns.CommandText = "PRAGMA table_info(clips)";
            using var reader = columns.ExecuteReader();
            var names = new List<string>();
            while (reader.Read())
                names.Add(reader.GetString(1));

            Assert.Contains("image_path", names);
            Assert.Contains("image_width", names);
            Assert.Contains("image_height", names);
        }

        using var version = migrated.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        Assert.Equal(2L, (long)version.ExecuteScalar()!);
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

    private static byte[] ImageBytes(int value)
        => [0x89, 0x50, 0x4E, 0x47, .. BitConverter.GetBytes(value)];
}
