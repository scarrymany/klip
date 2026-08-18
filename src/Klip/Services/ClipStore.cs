using System.Globalization;
using Klip.Models;
using Microsoft.Data.Sqlite;

namespace Klip.Services;

public sealed class ClipStore : IDisposable
{
    public const int HistoryLimit = 500;
    public const int MaxContentLength = 1_000_000;

    private readonly SqliteConnection _db;
    private readonly object _gate = new();

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Klip");

    public static string DatabasePath => Path.Combine(DataDirectory, "klip.db");

    public ClipStore()
    {
        Directory.CreateDirectory(DataDirectory);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true,
            Cache = SqliteCacheMode.Shared,
        }.ToString());
        _db.Open();
        Initialize();
    }

    private void Initialize()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA temp_store=MEMORY;

            CREATE TABLE IF NOT EXISTS collections (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS clips (
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
                updated_at TEXT NOT NULL,
                FOREIGN KEY (collection_id) REFERENCES collections(id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS clips_updated_idx ON clips (updated_at DESC);
            CREATE INDEX IF NOT EXISTS clips_pinned_idx ON clips (pinned);
            CREATE INDEX IF NOT EXISTS clips_kind_idx ON clips (kind);
            CREATE INDEX IF NOT EXISTS clips_collection_idx ON clips (collection_id);
            """;
        cmd.ExecuteNonQuery();
        SeedWelcome();
    }

    private void SeedWelcome()
    {
        using var check = _db.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM clips";
        var count = Convert.ToInt32(check.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (count > 0)
            return;

        using var insert = _db.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO clips (title, content, kind, color, pinned, copy_count, created_at, updated_at)
            VALUES ($title, $content, $kind, 'none', 1, 0, $now, $now);
            """;
        insert.Parameters.AddWithValue("$title", "Добро пожаловать в Клип");
        insert.Parameters.AddWithValue(
            "$content",
            "Клип следит за буфером обмена и сохраняет текст локально.\n\n" +
            "Ctrl+Shift+V показывает и скрывает окно.\n" +
            "Нажмите на запись, чтобы скопировать её обратно.\n" +
            "Закреплённые фрагменты не вытесняются из истории.");
        insert.Parameters.AddWithValue("$kind", ClipKinds.Note);
        insert.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
        insert.ExecuteNonQuery();
    }

    public IReadOnlyList<ClipItem> ListClips()
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText =
                """
                SELECT id, collection_id, title, content, kind, color, pinned,
                       copy_count, last_copied_at, created_at, updated_at
                FROM clips
                ORDER BY pinned DESC, updated_at DESC, id DESC
                """;
            return ReadClips(cmd);
        }
    }

    public IReadOnlyList<CollectionItem> ListCollections()
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText =
                """
                SELECT c.id, c.name, c.created_at,
                       (SELECT COUNT(*) FROM clips x WHERE x.collection_id = c.id) AS cnt
                FROM collections c
                ORDER BY c.created_at ASC, c.id ASC
                """;
            using var reader = cmd.ExecuteReader();
            var list = new List<CollectionItem>();
            while (reader.Read())
            {
                list.Add(new CollectionItem
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    CreatedAt = Parse(reader.GetString(2)),
                    Count = reader.GetInt32(3),
                });
            }

            return list;
        }
    }

    public ClipItem? TryAddFromClipboard(string content)
    {
        content = Normalize(content);
        if (content.Length == 0)
            return null;

        lock (_gate)
        {
            var latest = GetLatestUnlocked();
            if (latest is not null && latest.Content == content)
                return null;

            var now = DateTime.UtcNow;
            var kind = ClipKinds.Detect(content);
            using var cmd = _db.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO clips (content, kind, color, pinned, copy_count, created_at, updated_at)
                VALUES ($content, $kind, 'none', 0, 0, $now, $now);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$content", content);
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$now", Format(now));
            var id = (long)cmd.ExecuteScalar()!;
            TrimUnlocked();
            return GetByIdUnlocked(id);
        }
    }

    public ClipItem AddNote(string? title, string content, string kind, string color, long? collectionId)
    {
        content = Normalize(content);
        if (content.Length == 0)
            throw new InvalidOperationException("Текст не может быть пустым.");

        if (string.IsNullOrWhiteSpace(kind))
            kind = ClipKinds.Detect(content);

        lock (_gate)
        {
            var now = DateTime.UtcNow;
            using var cmd = _db.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO clips (collection_id, title, content, kind, color, pinned, copy_count, created_at, updated_at)
                VALUES ($collection, $title, $content, $kind, $color, 0, 0, $now, $now);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$collection", collectionId is null ? DBNull.Value : collectionId.Value);
            cmd.Parameters.AddWithValue("$title", string.IsNullOrWhiteSpace(title) ? DBNull.Value : title.Trim());
            cmd.Parameters.AddWithValue("$content", content);
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$color", string.IsNullOrWhiteSpace(color) ? ClipColors.None : color);
            cmd.Parameters.AddWithValue("$now", Format(now));
            var id = (long)cmd.ExecuteScalar()!;
            TrimUnlocked();
            return GetByIdUnlocked(id) ?? throw new InvalidOperationException("Не удалось сохранить запись.");
        }
    }

    public void Update(ClipItem item)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText =
                """
                UPDATE clips
                SET collection_id = $collection,
                    title = $title,
                    content = $content,
                    kind = $kind,
                    color = $color,
                    pinned = $pinned,
                    copy_count = $copies,
                    last_copied_at = $copied,
                    updated_at = $updated
                WHERE id = $id
                """;
            cmd.Parameters.AddWithValue("$collection", item.CollectionId is null ? DBNull.Value : item.CollectionId.Value);
            cmd.Parameters.AddWithValue("$title", string.IsNullOrWhiteSpace(item.Title) ? DBNull.Value : item.Title.Trim());
            cmd.Parameters.AddWithValue("$content", Normalize(item.Content));
            cmd.Parameters.AddWithValue("$kind", item.Kind);
            cmd.Parameters.AddWithValue("$color", item.Color);
            cmd.Parameters.AddWithValue("$pinned", item.Pinned ? 1 : 0);
            cmd.Parameters.AddWithValue("$copies", item.CopyCount);
            cmd.Parameters.AddWithValue("$copied", item.LastCopiedAt is null ? DBNull.Value : Format(item.LastCopiedAt.Value));
            cmd.Parameters.AddWithValue("$updated", Format(item.UpdatedAt));
            cmd.Parameters.AddWithValue("$id", item.Id);
            cmd.ExecuteNonQuery();
        }
    }

    public void SetPinned(long id, bool pinned)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE clips SET pinned = $pinned, updated_at = $now WHERE id = $id";
            cmd.Parameters.AddWithValue("$pinned", pinned ? 1 : 0);
            cmd.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void SetColor(long id, string color)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE clips SET color = $color, updated_at = $now WHERE id = $id";
            cmd.Parameters.AddWithValue("$color", color);
            cmd.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void SetCollection(long id, long? collectionId)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE clips SET collection_id = $collection, updated_at = $now WHERE id = $id";
            cmd.Parameters.AddWithValue("$collection", collectionId is null ? DBNull.Value : collectionId.Value);
            cmd.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void MarkCopied(long id)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText =
                """
                UPDATE clips
                SET copy_count = copy_count + 1,
                    last_copied_at = $now,
                    updated_at = $now
                WHERE id = $id
                """;
            cmd.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void Delete(long id)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM clips WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public CollectionItem AddCollection(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Название папки пустое.");

        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO collections (name, created_at) VALUES ($name, $now);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
            var id = (long)cmd.ExecuteScalar()!;
            return new CollectionItem
            {
                Id = id,
                Name = name,
                CreatedAt = DateTime.UtcNow,
                Count = 0,
            };
        }
    }

    public void DeleteCollection(long id)
    {
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            using (var clear = _db.CreateCommand())
            {
                clear.Transaction = tx;
                clear.CommandText = "UPDATE clips SET collection_id = NULL WHERE collection_id = $id";
                clear.Parameters.AddWithValue("$id", id);
                clear.ExecuteNonQuery();
            }

            using (var del = _db.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM collections WHERE id = $id";
                del.Parameters.AddWithValue("$id", id);
                del.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    public string? GetSetting(string key)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT value FROM settings WHERE key = $key";
            cmd.Parameters.AddWithValue("$key", key);
            return cmd.ExecuteScalar() as string;
        }
    }

    public void SetSetting(string key, string value)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO settings (key, value) VALUES ($key, $value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value
                """;
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$value", value);
            cmd.ExecuteNonQuery();
        }
    }

    private ClipItem? GetLatestUnlocked()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            SELECT id, collection_id, title, content, kind, color, pinned,
                   copy_count, last_copied_at, created_at, updated_at
            FROM clips
            ORDER BY id DESC
            LIMIT 1
            """;
        var list = ReadClips(cmd);
        return list.Count == 0 ? null : list[0];
    }

    private ClipItem? GetByIdUnlocked(long id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            SELECT id, collection_id, title, content, kind, color, pinned,
                   copy_count, last_copied_at, created_at, updated_at
            FROM clips
            WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$id", id);
        var list = ReadClips(cmd);
        return list.Count == 0 ? null : list[0];
    }

    private void TrimUnlocked()
    {
        using var countCmd = _db.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM clips";
        var count = Convert.ToInt32(countCmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (count <= HistoryLimit)
            return;

        using var trim = _db.CreateCommand();
        trim.CommandText =
            """
            DELETE FROM clips
            WHERE id IN (
                SELECT id FROM clips
                WHERE pinned = 0
                ORDER BY updated_at ASC, id ASC
                LIMIT $extra
            )
            """;
        trim.Parameters.AddWithValue("$extra", count - HistoryLimit);
        trim.ExecuteNonQuery();
    }

    private static List<ClipItem> ReadClips(SqliteCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var list = new List<ClipItem>();
        while (reader.Read())
        {
            list.Add(new ClipItem
            {
                Id = reader.GetInt64(0),
                CollectionId = reader.IsDBNull(1) ? null : reader.GetInt64(1),
                Title = reader.IsDBNull(2) ? null : reader.GetString(2),
                Content = reader.GetString(3),
                Kind = reader.GetString(4),
                Color = reader.GetString(5),
                Pinned = reader.GetInt32(6) != 0,
                CopyCount = reader.GetInt32(7),
                LastCopiedAt = reader.IsDBNull(8) ? null : Parse(reader.GetString(8)),
                CreatedAt = Parse(reader.GetString(9)),
                UpdatedAt = Parse(reader.GetString(10)),
            });
        }

        return list;
    }

    private static string Normalize(string content)
    {
        var text = content.Replace("\r\n", "\n").Replace('\r', '\n');
        if (text.Length > MaxContentLength)
            text = text[..MaxContentLength];
        return text;
    }

    private static string Format(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        return utc.ToString("o", CultureInfo.InvariantCulture);
    }

    private static DateTime Parse(string value)
        => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public void Dispose()
    {
        lock (_gate)
        {
            _db.Dispose();
        }
    }
}
