using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Klip.Models;
using Microsoft.Data.Sqlite;

namespace Klip.Services;

public sealed class ClipStore : IDisposable
{
    public const int HistoryLimit = 500;
    public const int MaxContentLength = 1_000_000;
    public const int ListPreviewLength = 500;
    public const int SchemaVersion = 1;

    private readonly SqliteConnection _db;
    private readonly object _gate = new();

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Klip");

    public static string DatabasePath => Path.Combine(DataDirectory, "klip.db");

    public ClipStore() : this(DatabasePath)
    {
    }

    public ClipStore(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
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
        Migrate();
        SeedWelcome();
    }

    private void Migrate()
    {
        var version = ReadUserVersion();
        if (version < 1)
            MigrateTo1();
    }

    private void MigrateTo1()
    {
        if (!HasColumn("clips", "source"))
            Execute("ALTER TABLE clips ADD COLUMN source TEXT NOT NULL DEFAULT 'clipboard'");
        if (!HasColumn("clips", "content_hash"))
            Execute("ALTER TABLE clips ADD COLUMN content_hash TEXT");

        Execute(
            """
            UPDATE clips
            SET source = 'manual'
            WHERE source = 'clipboard'
              AND (title IS NOT NULL OR kind = 'note')
            """);

        using (var select = _db.CreateCommand())
        {
            select.CommandText = "SELECT id, content FROM clips WHERE content_hash IS NULL OR content_hash = ''";
            using var reader = select.ExecuteReader();
            var pending = new List<(long Id, string Hash)>();
            while (reader.Read())
                pending.Add((reader.GetInt64(0), HashContent(reader.GetString(1))));

            foreach (var (id, hash) in pending)
            {
                using var update = _db.CreateCommand();
                update.CommandText = "UPDATE clips SET content_hash = $hash WHERE id = $id";
                update.Parameters.AddWithValue("$hash", hash);
                update.Parameters.AddWithValue("$id", id);
                update.ExecuteNonQuery();
            }
        }

        Execute("CREATE INDEX IF NOT EXISTS clips_hash_idx ON clips (content_hash)");
        Execute("CREATE INDEX IF NOT EXISTS clips_source_idx ON clips (source)");
        SetUserVersion(1);
    }

    private void SeedWelcome()
    {
        using var check = _db.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM clips";
        var count = Convert.ToInt32(check.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (count > 0)
            return;

        var now = DateTime.UtcNow;
        var content =
            "Клип следит за буфером обмена и сохраняет текст локально.\n\n" +
            "Ctrl+Shift+V показывает и скрывает окно.\n" +
            "Нажмите на запись, чтобы скопировать её обратно.\n" +
            "Закреплённые фрагменты не вытесняются из истории.";
        using var insert = _db.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO clips (title, content, kind, color, pinned, copy_count, created_at, updated_at, source, content_hash)
            VALUES ($title, $content, $kind, 'none', 1, 0, $now, $now, $source, $hash);
            """;
        insert.Parameters.AddWithValue("$title", "Добро пожаловать в Клип");
        insert.Parameters.AddWithValue("$content", content);
        insert.Parameters.AddWithValue("$kind", ClipKinds.Note);
        insert.Parameters.AddWithValue("$now", Format(now));
        insert.Parameters.AddWithValue("$source", ClipSources.Manual);
        insert.Parameters.AddWithValue("$hash", HashContent(content));
        insert.ExecuteNonQuery();
    }

    public IReadOnlyList<ClipItem> ListClips()
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText =
                """
                SELECT id, collection_id, title,
                       CASE WHEN length(content) > $preview THEN substr(content, 1, $preview) ELSE content END,
                       kind, color, pinned, copy_count, last_copied_at, created_at, updated_at,
                       length(content), source
                FROM clips
                ORDER BY pinned DESC, updated_at DESC, id DESC
                """;
            cmd.Parameters.AddWithValue("$preview", ListPreviewLength);
            return ReadClips(cmd, includeLength: true);
        }
    }

    public ClipItem? GetById(long id)
    {
        lock (_gate)
            return GetByIdUnlocked(id);
    }

    public IReadOnlyList<long> SearchIds(string query)
    {
        var needle = query.Trim();
        if (needle.Length == 0)
            return [];

        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText =
                """
                SELECT id FROM clips
                WHERE IFNULL(title, '') LIKE $q ESCAPE '\'
                   OR content LIKE $q ESCAPE '\'
                """;
            cmd.Parameters.AddWithValue("$q", "%" + EscapeLike(needle) + "%");
            using var reader = cmd.ExecuteReader();
            var ids = new List<long>();
            while (reader.Read())
                ids.Add(reader.GetInt64(0));
            return ids;
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

        var hash = HashContent(content);
        lock (_gate)
        {
            if (IsSameAsLatestUnlocked(content, hash))
                return null;

            var existingId = FindIdByHashUnlocked(hash);
            var now = DateTime.UtcNow;
            if (existingId is { } id)
            {
                using var bump = _db.CreateCommand();
                bump.CommandText = "UPDATE clips SET updated_at = $now, content_hash = $hash WHERE id = $id";
                bump.Parameters.AddWithValue("$now", Format(now));
                bump.Parameters.AddWithValue("$hash", hash);
                bump.Parameters.AddWithValue("$id", id);
                bump.ExecuteNonQuery();
                return GetByIdUnlocked(id);
            }

            var kind = ClipKinds.Detect(content);
            using var cmd = _db.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO clips (content, kind, color, pinned, copy_count, created_at, updated_at, source, content_hash)
                VALUES ($content, $kind, 'none', 0, 0, $now, $now, $source, $hash);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$content", content);
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$now", Format(now));
            cmd.Parameters.AddWithValue("$source", ClipSources.Clipboard);
            cmd.Parameters.AddWithValue("$hash", hash);
            var inserted = (long)cmd.ExecuteScalar()!;
            TrimUnlocked();
            return GetByIdUnlocked(inserted);
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
                INSERT INTO clips (collection_id, title, content, kind, color, pinned, copy_count, created_at, updated_at, source, content_hash)
                VALUES ($collection, $title, $content, $kind, $color, 0, 0, $now, $now, $source, $hash);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$collection", collectionId is null ? DBNull.Value : collectionId.Value);
            cmd.Parameters.AddWithValue("$title", string.IsNullOrWhiteSpace(title) ? DBNull.Value : title.Trim());
            cmd.Parameters.AddWithValue("$content", content);
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$color", string.IsNullOrWhiteSpace(color) ? ClipColors.None : color);
            cmd.Parameters.AddWithValue("$now", Format(now));
            cmd.Parameters.AddWithValue("$source", ClipSources.Manual);
            cmd.Parameters.AddWithValue("$hash", HashContent(content));
            var id = (long)cmd.ExecuteScalar()!;
            return GetByIdUnlocked(id) ?? throw new InvalidOperationException("Не удалось сохранить запись.");
        }
    }

    public void Update(ClipItem item)
    {
        lock (_gate)
        {
            var content = Normalize(item.Content);
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
                    updated_at = $updated,
                    content_hash = $hash
                WHERE id = $id
                """;
            cmd.Parameters.AddWithValue("$collection", item.CollectionId is null ? DBNull.Value : item.CollectionId.Value);
            cmd.Parameters.AddWithValue("$title", string.IsNullOrWhiteSpace(item.Title) ? DBNull.Value : item.Title.Trim());
            cmd.Parameters.AddWithValue("$content", content);
            cmd.Parameters.AddWithValue("$kind", item.Kind);
            cmd.Parameters.AddWithValue("$color", item.Color);
            cmd.Parameters.AddWithValue("$pinned", item.Pinned ? 1 : 0);
            cmd.Parameters.AddWithValue("$copies", item.CopyCount);
            cmd.Parameters.AddWithValue("$copied", item.LastCopiedAt is null ? DBNull.Value : Format(item.LastCopiedAt.Value));
            cmd.Parameters.AddWithValue("$updated", Format(item.UpdatedAt));
            cmd.Parameters.AddWithValue("$hash", HashContent(content));
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

    public static string HashContent(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string EscapeLike(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    public static string Normalize(string content)
    {
        var text = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (text.Length > MaxContentLength)
            text = text[..MaxContentLength];
        return text;
    }

    private bool IsSameAsLatestUnlocked(string content, string hash)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            SELECT content, content_hash
            FROM clips
            ORDER BY id DESC
            LIMIT 1
            """;
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return false;

        if (!reader.IsDBNull(1))
        {
            var latestHash = reader.GetString(1);
            if (!string.IsNullOrEmpty(latestHash))
                return string.Equals(latestHash, hash, StringComparison.Ordinal);
        }

        return reader.GetString(0) == content;
    }

    private long? FindIdByHashUnlocked(string hash)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT id FROM clips WHERE content_hash = $hash ORDER BY updated_at DESC, id DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$hash", hash);
        var value = cmd.ExecuteScalar();
        if (value is null or DBNull)
            return null;
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private ClipItem? GetByIdUnlocked(long id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            SELECT id, collection_id, title, content, kind, color, pinned,
                   copy_count, last_copied_at, created_at, updated_at,
                   length(content), source
            FROM clips
            WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$id", id);
        var list = ReadClips(cmd, includeLength: true, fullContent: true);
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
                WHERE pinned = 0 AND source = 'clipboard'
                ORDER BY updated_at ASC, id ASC
                LIMIT $extra
            )
            """;
        trim.Parameters.AddWithValue("$extra", count - HistoryLimit);
        trim.ExecuteNonQuery();
    }

    private static List<ClipItem> ReadClips(SqliteCommand cmd, bool includeLength, bool fullContent = false)
    {
        using var reader = cmd.ExecuteReader();
        var list = new List<ClipItem>();
        while (reader.Read())
        {
            var content = reader.GetString(3);
            var length = includeLength ? reader.GetInt32(11) : content.Length;
            var source = includeLength && !reader.IsDBNull(12) ? reader.GetString(12) : ClipSources.Clipboard;
            list.Add(new ClipItem
            {
                Id = reader.GetInt64(0),
                CollectionId = reader.IsDBNull(1) ? null : reader.GetInt64(1),
                Title = reader.IsDBNull(2) ? null : reader.GetString(2),
                Content = content,
                Kind = reader.GetString(4),
                Color = reader.GetString(5),
                Pinned = reader.GetInt32(6) != 0,
                CopyCount = reader.GetInt32(7),
                LastCopiedAt = reader.IsDBNull(8) ? null : Parse(reader.GetString(8)),
                CreatedAt = Parse(reader.GetString(9)),
                UpdatedAt = Parse(reader.GetString(10)),
                Source = source,
                HasFullContent = fullContent || length <= content.Length,
            });
        }

        return list;
    }

    private int ReadUserVersion()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private void SetUserVersion(int version)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "PRAGMA user_version = " + version.ToString(CultureInfo.InvariantCulture);
        cmd.ExecuteNonQuery();
    }

    private bool HasColumn(string table, string column)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(" + table + ")";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void Execute(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
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
