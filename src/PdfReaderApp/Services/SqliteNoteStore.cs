using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lưu ghi chú trong notes.db, tách khỏi library.db/chats.db/index.db.
/// Schema có PRAGMA user_version để migrate ở các layer sau (thêm cột tag/màu/region).</summary>
public sealed class SqliteNoteStore : INoteStore
{
    private const long SchemaVersion = 1;
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
  content TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS ix_note_owner ON note(owner_key);";
            cmd.ExecuteNonQuery();

            // Khung migration theo phiên bản schema (các layer sau dùng ALTER TABLE ADD COLUMN).
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

    public void Add(Note note)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO note (id, owner_key, document_id, page_index, content, created_at, updated_at)
VALUES ($id, $owner, $doc, $page, $content, $created, $updated);";
            cmd.Parameters.AddWithValue("$id", note.Id);
            cmd.Parameters.AddWithValue("$owner", note.OwnerKey);
            cmd.Parameters.AddWithValue("$doc", (object?)note.DocumentId ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("$page", (object?)note.PageIndex ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("$content", note.Content);
            cmd.Parameters.AddWithValue("$created", note.CreatedAtUnixMs);
            cmd.Parameters.AddWithValue("$updated", note.UpdatedAtUnixMs);
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

    public IReadOnlyList<Note> GetForOwner(string ownerKey)
    {
        lock (_lock)
        {
            var list = new List<Note>();
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, owner_key, document_id, page_index, content, created_at, updated_at FROM note WHERE owner_key=$owner";
            cmd.Parameters.AddWithValue("$owner", ownerKey);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new Note(
                    r.GetString(0),
                    r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2),
                    r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                    r.GetString(4),
                    r.GetInt64(5),
                    r.GetInt64(6)));
            }
            return list;
        }
    }
}
