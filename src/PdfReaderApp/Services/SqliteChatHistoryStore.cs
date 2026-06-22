using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lưu lịch sử chat trong chats.db, tách khỏi library.db và index.db.</summary>
public sealed class SqliteChatHistoryStore : IChatHistoryStore
{
    private readonly string _connectionString;
    private readonly object _lock = new();

    public SqliteChatHistoryStore(string dbPath)
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
CREATE TABLE IF NOT EXISTS chat_message (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  document_id TEXT NOT NULL,
  role TEXT NOT NULL,
  content TEXT NOT NULL,
  created_at INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS ix_chat_message_doc ON chat_message(document_id, id);";
            cmd.ExecuteNonQuery();
        }
    }

    public void Append(string documentId, string role, string content, long createdAtUnix)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO chat_message (document_id, role, content, created_at)
VALUES ($id, $role, $content, $ts);";
            cmd.Parameters.AddWithValue("$id", documentId);
            cmd.Parameters.AddWithValue("$role", role);
            cmd.Parameters.AddWithValue("$content", content);
            cmd.Parameters.AddWithValue("$ts", createdAtUnix);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<ChatHistoryEntry> GetAll(string documentId)
    {
        lock (_lock)
        {
            var list = new List<ChatHistoryEntry>();
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT document_id, role, content, created_at FROM chat_message WHERE document_id=$id ORDER BY id ASC";
            cmd.Parameters.AddWithValue("$id", documentId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new ChatHistoryEntry(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3)));
            return list;
        }
    }

    public void DeleteForDocument(string documentId)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM chat_message WHERE document_id=$id";
            cmd.Parameters.AddWithValue("$id", documentId);
            cmd.ExecuteNonQuery();
        }
    }
}
