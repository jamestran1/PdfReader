using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lưu metadata thư viện trong library.db, tách khỏi index.db của AI.</summary>
public sealed class SqliteLibraryStore : ILibraryStore
{
    private readonly string _connectionString;
    private readonly object _lock = new();

    public SqliteLibraryStore(string dbPath)
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
CREATE TABLE IF NOT EXISTS library (
  document_id TEXT PRIMARY KEY,
  title TEXT NOT NULL,
  stored_path TEXT NOT NULL,
  thumb_path TEXT,
  page_count INTEGER NOT NULL,
  imported_at INTEGER NOT NULL,
  last_opened_at INTEGER NOT NULL,
  author TEXT,
  publisher TEXT);";
            cmd.ExecuteNonQuery();

            // DB tạo trước #78 thiếu hai cột này; thêm tại chỗ để giữ dữ liệu cũ.
            AddColumnIfMissing(conn, "library", "author", "TEXT");
            AddColumnIfMissing(conn, "library", "publisher", "TEXT");
        }
    }

    private static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(1), column, System.StringComparison.Ordinal))
                return true;
        return false;
    }

    private static void AddColumnIfMissing(SqliteConnection conn, string table, string column, string columnType)
    {
        if (ColumnExists(conn, table, column)) return;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnType}";
        cmd.ExecuteNonQuery();
    }

    public void Upsert(LibraryItem item)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO library (document_id, title, stored_path, thumb_path, page_count, imported_at, last_opened_at, author, publisher)
VALUES ($id, $title, $path, $thumb, $pc, $imp, $open, $author, $publisher)
ON CONFLICT(document_id) DO UPDATE SET
  title=$title, stored_path=$path, thumb_path=$thumb, page_count=$pc, last_opened_at=$open, author=$author, publisher=$publisher;";
            cmd.Parameters.AddWithValue("$id", item.DocumentId);
            cmd.Parameters.AddWithValue("$title", item.Title);
            cmd.Parameters.AddWithValue("$path", item.StoredPath);
            cmd.Parameters.AddWithValue("$thumb", (object?)item.ThumbPath ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("$pc", item.PageCount);
            cmd.Parameters.AddWithValue("$imp", item.ImportedAtUnix);
            cmd.Parameters.AddWithValue("$open", item.LastOpenedAtUnix);
            cmd.Parameters.AddWithValue("$author", (object?)item.Author ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("$publisher", (object?)item.Publisher ?? System.DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<LibraryItem> GetAll()
    {
        lock (_lock)
        {
            var list = new List<LibraryItem>();
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT document_id, title, stored_path, thumb_path, page_count, imported_at, last_opened_at, author, publisher FROM library ORDER BY last_opened_at DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(Read(r));
            return list;
        }
    }

    public LibraryItem? Get(string documentId)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT document_id, title, stored_path, thumb_path, page_count, imported_at, last_opened_at, author, publisher FROM library WHERE document_id=$id";
            cmd.Parameters.AddWithValue("$id", documentId);
            using var r = cmd.ExecuteReader();
            return r.Read() ? Read(r) : null;
        }
    }

    public void TouchLastOpened(string documentId, long whenUnix)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE library SET last_opened_at=$t WHERE document_id=$id";
            cmd.Parameters.AddWithValue("$t", whenUnix);
            cmd.Parameters.AddWithValue("$id", documentId);
            cmd.ExecuteNonQuery();
        }
    }

    public void Remove(string documentId)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM library WHERE document_id=$id";
            cmd.Parameters.AddWithValue("$id", documentId);
            cmd.ExecuteNonQuery();
        }
    }

    private static LibraryItem Read(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.GetInt32(4), r.GetInt64(5), r.GetInt64(6),
        r.IsDBNull(7) ? null : r.GetString(7),
        r.IsDBNull(8) ? null : r.GetString(8));
}
