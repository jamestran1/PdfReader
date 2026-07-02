using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lưu ghi chú trong notes.db, tách khỏi library.db/chats.db/index.db.
/// Schema có PRAGMA user_version để migrate ở các layer sau (thêm cột tag/màu/region).</summary>
public sealed class SqliteNoteStore : INoteStore
{
    private const long SchemaVersion = 3;
    private readonly string _connectionString;
    private readonly object _lock = new();

    public SqliteNoteStore(string dbPath)
    {
        _connectionString = $"Data Source={dbPath};Pooling=False";
    }

    private SqliteConnection OpenConn()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public void EnsureSchema()
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS note (
  id TEXT PRIMARY KEY,
  owner_key TEXT NOT NULL,
  document_id TEXT,
  page_index INTEGER,
  quote TEXT,
  content TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL,
  rects TEXT,
  color TEXT);
CREATE INDEX IF NOT EXISTS ix_note_owner ON note(owner_key);";
            cmd.ExecuteNonQuery();

            // Db cũ (v1) tạo trước khi có cột quote: thêm cột nếu thiếu.
            if (!ColumnExists(conn, "note", "quote"))
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE note ADD COLUMN quote TEXT;";
                alter.ExecuteNonQuery();
            }

            // Db cũ (v2) chưa có cột rects/color: thêm nếu thiếu.
            if (!ColumnExists(conn, "note", "rects"))
            {
                using var a1 = conn.CreateCommand();
                a1.CommandText = "ALTER TABLE note ADD COLUMN rects TEXT;";
                a1.ExecuteNonQuery();
            }
            if (!ColumnExists(conn, "note", "color"))
            {
                using var a2 = conn.CreateCommand();
                a2.CommandText = "ALTER TABLE note ADD COLUMN color TEXT;";
                a2.ExecuteNonQuery();
            }

            using var ver = conn.CreateCommand();
            ver.CommandText = "PRAGMA user_version;";
            long current = (long)(ver.ExecuteScalar() ?? 0L);
            if (current < SchemaVersion)
            {
                using var set = conn.CreateCommand();
                set.CommandText = $"PRAGMA user_version = {SchemaVersion};";
                set.ExecuteNonQuery();
            }
        }
    }

    private static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (string.Equals(r.GetString(1), column, System.StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public void Add(Note note)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO note (id, owner_key, document_id, page_index, quote, content, created_at, updated_at, rects, color)
VALUES ($id, $owner, $doc, $page, $quote, $content, $created, $updated, $rects, $color);";
            cmd.Parameters.AddWithValue("$id", note.Id);
            cmd.Parameters.AddWithValue("$owner", note.OwnerKey);
            cmd.Parameters.AddWithValue("$doc", (object?)note.DocumentId ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("$page", (object?)note.PageIndex ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("$quote", (object?)note.Quote ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("$content", note.Content);
            cmd.Parameters.AddWithValue("$created", note.CreatedAtUnixMs);
            cmd.Parameters.AddWithValue("$updated", note.UpdatedAtUnixMs);
            string? rectsJson = note.Rects == null ? null
                : System.Text.Json.JsonSerializer.Serialize(note.Rects);
            cmd.Parameters.AddWithValue("$rects", (object?)rectsJson ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("$color", (object?)note.Color ?? System.DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public int Update(string id, string content, long nowUnixMs)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE note SET content=$content, updated_at=$updated WHERE id=$id";
            cmd.Parameters.AddWithValue("$content", content);
            cmd.Parameters.AddWithValue("$updated", nowUnixMs);
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery();
        }
    }

    public int Delete(string id)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM note WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery();
        }
    }

    public int ReassignOwner(string oldKey, string newKey)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE note SET owner_key=$new WHERE owner_key=$old";
            cmd.Parameters.AddWithValue("$new", newKey);
            cmd.Parameters.AddWithValue("$old", oldKey);
            return cmd.ExecuteNonQuery();
        }
    }

    public int DeleteForOwner(string ownerKey)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM note WHERE owner_key=$o";
            cmd.Parameters.AddWithValue("$o", ownerKey);
            return cmd.ExecuteNonQuery();
        }
    }

    public int DeleteForDocument(string documentId)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM note WHERE document_id=$d";
            cmd.Parameters.AddWithValue("$d", documentId);
            return cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<Note> GetForOwner(string ownerKey)
    {
        lock (_lock)
        {
            var list = new List<Note>();
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, owner_key, document_id, page_index, quote, content, created_at, updated_at, rects, color FROM note WHERE owner_key=$owner";
            cmd.Parameters.AddWithValue("$owner", ownerKey);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                IReadOnlyList<HighlightRect>? rects = null;
                if (!r.IsDBNull(8))
                {
                    try { rects = System.Text.Json.JsonSerializer.Deserialize<List<HighlightRect>>(r.GetString(8)); }
                    catch { rects = null; }
                }
                string? color = r.IsDBNull(9) ? null : r.GetString(9);
                list.Add(new Note(
                    r.GetString(0),
                    r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2),
                    r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.GetString(5),
                    r.GetInt64(6),
                    r.GetInt64(7),
                    rects,
                    color));
            }
            return list;
        }
    }
}
