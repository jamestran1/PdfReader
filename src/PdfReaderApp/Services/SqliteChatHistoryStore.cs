using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lưu lịch sử chat trong chats.db, tách khỏi library.db và index.db.</summary>
public sealed class SqliteChatHistoryStore : IChatHistoryStore
{
    private readonly string _connectionString;
    private readonly Lock _lock = new();

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

            // DB tạo trước #108 thiếu cột bảng nguồn trích dẫn: thêm tại chỗ, dòng cũ để NULL (không có link).
            if (!ColumnExists(conn, "chat_message", "sources"))
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE chat_message ADD COLUMN sources TEXT";
                alter.ExecuteNonQuery();
            }
        }
    }

    private static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info($table) WHERE name = $column";
        cmd.Parameters.AddWithValue("$table", table);
        cmd.Parameters.AddWithValue("$column", column);
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    public void Append(string documentId, string role, string content, long createdAtUnix,
        IReadOnlyList<RagSource>? sources = null)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO chat_message (document_id, role, content, created_at, sources)
VALUES ($id, $role, $content, $ts, $sources);";
            cmd.Parameters.AddWithValue("$id", documentId);
            cmd.Parameters.AddWithValue("$role", role);
            cmd.Parameters.AddWithValue("$content", content);
            cmd.Parameters.AddWithValue("$ts", createdAtUnix);
            cmd.Parameters.AddWithValue("$sources",
                sources is { Count: > 0 } ? JsonSerializer.Serialize(sources) : DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    // JSON hỏng (sửa tay, bản lạ) thì coi như không có bảng nguồn: tin vẫn hiện, chỉ mất link.
    private static List<RagSource> ParseSources(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<RagSource>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    public IReadOnlyList<ChatHistoryEntry> GetAll(string documentId)
    {
        lock (_lock)
        {
            var list = new List<ChatHistoryEntry>();
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT document_id, role, content, created_at, sources FROM chat_message WHERE document_id=$id ORDER BY id ASC";
            cmd.Parameters.AddWithValue("$id", documentId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new ChatHistoryEntry(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3),
                    ParseSources(r.IsDBNull(4) ? null : r.GetString(4))));
            }

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
