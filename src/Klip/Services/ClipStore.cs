using System.IO;
using Klip.Models;
using Microsoft.Data.Sqlite;

namespace Klip.Services;

public sealed class ClipStore : IDisposable
{
    public const int HistoryLimit = 500;
    private readonly SqliteConnection _db;

    public ClipStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Klip");
        Directory.CreateDirectory(dir);
        _db = new SqliteConnection($"Data Source={Path.Combine(dir, "klip.db")}");
        _db.Open();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            create table if not exists collections (
              id integer primary key autoincrement,
              name text not null,
              created_at text not null
            );
            create table if not exists clips (
              id integer primary key autoincrement,
              collection_id integer,
              title text,
              content text not null,
              kind text not null default 'clip',
              color text not null default 'none',
              pinned integer not null default 0,
              copy_count integer not null default 0,
              created_at text not null,
              updated_at text not null,
              last_copied_at text
            );
            create index if not exists clips_updated on clips(pinned desc, updated_at desc);
            """;
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<ClipItem> List(string? query = null, string? kind = null, bool pinnedOnly = false, long? collectionId = null)
    {
        using var cmd = _db.CreateCommand();
        var where = new List<string>();
        if (pinnedOnly) where.Add("pinned = 1");
        if (kind is not null)
        {
            where.Add("kind = $kind");
            cmd.Parameters.AddWithValue("$kind", kind);
        }
        if (collectionId is not null)
        {
            where.Add("collection_id = $cid");
            cmd.Parameters.AddWithValue("$cid", collectionId.Value);
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            where.Add("(content like $q or ifnull(title,'') like $q)");
            cmd.Parameters.AddWithValue("$q", "%" + query.Trim() + "%");
        }

        cmd.CommandText = $"""
            select id, collection_id, title, content, kind, color, pinned, copy_count,
                   created_at, updated_at, last_copied_at
            from clips
            {(where.Count > 0 ? "where " + string.Join(" and ", where) : "")}
            order by pinned desc, updated_at desc
            limit {HistoryLimit}
            """;
        return ReadClips(cmd);
    }

    public ClipItem Add(string content, string? title = null, string? kind = null, long? collectionId = null)
    {
        content = content.Replace("\0", "").TrimEnd();
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Пустой фрагмент");

        using (var last = _db.CreateCommand())
        {
            last.CommandText = "select content from clips order by id desc limit 1";
            var prev = last.ExecuteScalar() as string;
            if (prev == content) return List().First();
        }

        var now = DateTime.UtcNow.ToString("o");
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            insert into clips (collection_id, title, content, kind, color, created_at, updated_at)
            values ($cid, $title, $content, $kind, 'none', $now, $now);
            select last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$cid", (object?)collectionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$title", (object?)title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$content", content);
        cmd.Parameters.AddWithValue("$kind", kind ?? ClipItem.DetectKind(content));
        cmd.Parameters.AddWithValue("$now", now);
        var id = (long)(cmd.ExecuteScalar() ?? 0);
        Trim();
        return Get(id) ?? throw new InvalidOperationException("Не удалось сохранить");
    }

    public ClipItem? Get(long id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            select id, collection_id, title, content, kind, color, pinned, copy_count,
                   created_at, updated_at, last_copied_at
            from clips where id = $id
            """;
        cmd.Parameters.AddWithValue("$id", id);
        return ReadClips(cmd).FirstOrDefault();
    }

    public void Update(ClipItem item)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            update clips set
              collection_id = $cid, title = $title, content = $content, kind = $kind,
              color = $color, pinned = $pinned, updated_at = $now
            where id = $id
            """;
        cmd.Parameters.AddWithValue("$cid", (object?)item.CollectionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$title", (object?)item.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$content", item.Content);
        cmd.Parameters.AddWithValue("$kind", item.Kind);
        cmd.Parameters.AddWithValue("$color", item.Color);
        cmd.Parameters.AddWithValue("$pinned", item.Pinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$id", item.Id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "delete from clips where id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void MarkCopied(long id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            update clips set copy_count = copy_count + 1, last_copied_at = $now
            where id = $id
            """;
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<CollectionItem> ListCollections()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            select c.id, c.name, c.created_at,
                   (select count(*) from clips x where x.collection_id = c.id) as cnt
            from collections c order by c.created_at
            """;
        var list = new List<CollectionItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new CollectionItem
            {
                Id = r.GetInt64(0),
                Name = r.GetString(1),
                CreatedAt = DateTime.Parse(r.GetString(2)),
                Count = r.GetInt32(3),
            });
        }
        return list;
    }

    public CollectionItem AddCollection(string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new InvalidOperationException("Название пустое");
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "insert into collections (name, created_at) values ($n, $t); select last_insert_rowid();";
        cmd.Parameters.AddWithValue("$n", name[..Math.Min(name.Length, 48)]);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        var id = (long)(cmd.ExecuteScalar() ?? 0);
        return new CollectionItem { Id = id, Name = name, CreatedAt = DateTime.UtcNow };
    }

    public void DeleteCollection(long id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            update clips set collection_id = null where collection_id = $id;
            delete from collections where id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public (int All, int Pinned, int Clip, int Note, int Code, int Link) Counts()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            select
              count(*),
              sum(case when pinned = 1 then 1 else 0 end),
              sum(case when kind = 'clip' then 1 else 0 end),
              sum(case when kind = 'note' then 1 else 0 end),
              sum(case when kind = 'code' then 1 else 0 end),
              sum(case when kind = 'link' then 1 else 0 end)
            from clips
            """;
        using var r = cmd.ExecuteReader();
        r.Read();
        return (
            r.IsDBNull(0) ? 0 : r.GetInt32(0),
            r.IsDBNull(1) ? 0 : r.GetInt32(1),
            r.IsDBNull(2) ? 0 : r.GetInt32(2),
            r.IsDBNull(3) ? 0 : r.GetInt32(3),
            r.IsDBNull(4) ? 0 : r.GetInt32(4),
            r.IsDBNull(5) ? 0 : r.GetInt32(5));
    }

    private void Trim()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            delete from clips where id in (
              select id from clips where pinned = 0
              order by updated_at desc
              limit -1 offset $keep
            )
            """;
        cmd.Parameters.AddWithValue("$keep", HistoryLimit);
        cmd.ExecuteNonQuery();
    }

    private static List<ClipItem> ReadClips(SqliteCommand cmd)
    {
        var list = new List<ClipItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ClipItem
            {
                Id = r.GetInt64(0),
                CollectionId = r.IsDBNull(1) ? null : r.GetInt64(1),
                Title = r.IsDBNull(2) ? null : r.GetString(2),
                Content = r.GetString(3),
                Kind = r.GetString(4),
                Color = r.GetString(5),
                Pinned = r.GetInt32(6) == 1,
                CopyCount = r.GetInt32(7),
                CreatedAt = DateTime.Parse(r.GetString(8)),
                UpdatedAt = DateTime.Parse(r.GetString(9)),
                LastCopiedAt = r.IsDBNull(10) ? null : DateTime.Parse(r.GetString(10)),
            });
        }
        return list;
    }

    public void Dispose() => _db.Dispose();
}
