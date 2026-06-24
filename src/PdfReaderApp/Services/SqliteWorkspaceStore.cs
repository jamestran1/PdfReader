using Microsoft.Data.Sqlite;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lưu Workspace + membership trong workspaces.db, tách khỏi library/notes/chats/index (ADR 0002).
/// Connection per-operation Pooling=False + lock; user_version riêng.</summary>
public sealed class SqliteWorkspaceStore : IWorkspaceStore
{
    private const long SchemaVersion = 1;
    private readonly string _connectionString;
    private readonly object _lock = new();

    public SqliteWorkspaceStore(string dbPath)
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
CREATE TABLE IF NOT EXISTS workspace (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  is_default INTEGER NOT NULL,
  default_document_id TEXT,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS workspace_document (
  workspace_id TEXT NOT NULL,
  document_id TEXT NOT NULL,
  PRIMARY KEY (workspace_id, document_id));
CREATE INDEX IF NOT EXISTS ix_ws_doc_document ON workspace_document(document_id);";
            cmd.ExecuteNonQuery();

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

    public void Upsert(Workspace w)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO workspace (id, name, is_default, default_document_id, created_at, updated_at)
VALUES ($id, $name, $def, $defDoc, $created, $updated)
ON CONFLICT(id) DO UPDATE SET name=$name, is_default=$def, default_document_id=$defDoc, updated_at=$updated;";
            cmd.Parameters.AddWithValue("$id", w.Id);
            cmd.Parameters.AddWithValue("$name", w.Name);
            cmd.Parameters.AddWithValue("$def", w.IsDefault ? 1 : 0);
            cmd.Parameters.AddWithValue("$defDoc", (object?)w.DefaultDocumentId ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("$created", w.CreatedAtUnixMs);
            cmd.Parameters.AddWithValue("$updated", w.UpdatedAtUnixMs);
            cmd.ExecuteNonQuery();
        }
    }

    public Workspace? Get(string id)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name, is_default, default_document_id, created_at, updated_at FROM workspace WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadRow(r) : null;
        }
    }

    public System.Collections.Generic.IReadOnlyList<Workspace> GetAll(bool includeDefault)
    {
        lock (_lock)
        {
            var list = new System.Collections.Generic.List<Workspace>();
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name, is_default, default_document_id, created_at, updated_at FROM workspace"
                + (includeDefault ? "" : " WHERE is_default=0")
                + " ORDER BY updated_at DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadRow(r));
            return list;
        }
    }

    public void AddDocument(string workspaceId, string documentId)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO workspace_document (workspace_id, document_id) VALUES ($w, $d)";
            cmd.Parameters.AddWithValue("$w", workspaceId);
            cmd.Parameters.AddWithValue("$d", documentId);
            cmd.ExecuteNonQuery();
        }
    }

    public void RemoveDocument(string workspaceId, string documentId)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM workspace_document WHERE workspace_id=$w AND document_id=$d";
            cmd.Parameters.AddWithValue("$w", workspaceId);
            cmd.Parameters.AddWithValue("$d", documentId);
            cmd.ExecuteNonQuery();
        }
    }

    public System.Collections.Generic.IReadOnlyList<string> GetDocumentIds(string workspaceId)
        => QueryStrings("SELECT document_id FROM workspace_document WHERE workspace_id=$k", workspaceId);

    public System.Collections.Generic.IReadOnlyList<string> GetWorkspaceIdsForDocument(string documentId)
        => QueryStrings("SELECT workspace_id FROM workspace_document WHERE document_id=$k", documentId);

    private System.Collections.Generic.IReadOnlyList<string> QueryStrings(string sql, string key)
    {
        lock (_lock)
        {
            var list = new System.Collections.Generic.List<string>();
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$k", key);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r.GetString(0));
            return list;
        }
    }

    public Workspace GetOrCreateDefaultForDocument(string documentId, string name, long nowUnixMs)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using (var find = conn.CreateCommand())
            {
                find.CommandText = "SELECT id, name, is_default, default_document_id, created_at, updated_at FROM workspace WHERE is_default=1 AND default_document_id=$d";
                find.Parameters.AddWithValue("$d", documentId);
                using var fr = find.ExecuteReader();
                if (fr.Read()) return ReadRow(fr);
            }

            var ws = new Workspace(System.Guid.NewGuid().ToString("N"), name, true, documentId, nowUnixMs, nowUnixMs);
            using (var ins = conn.CreateCommand())
            {
                ins.CommandText = @"INSERT INTO workspace (id, name, is_default, default_document_id, created_at, updated_at)
VALUES ($id, $name, 1, $d, $t, $t);";
                ins.Parameters.AddWithValue("$id", ws.Id);
                ins.Parameters.AddWithValue("$name", ws.Name);
                ins.Parameters.AddWithValue("$d", documentId);
                ins.Parameters.AddWithValue("$t", nowUnixMs);
                ins.ExecuteNonQuery();
            }
            using (var mem = conn.CreateCommand())
            {
                mem.CommandText = "INSERT OR IGNORE INTO workspace_document (workspace_id, document_id) VALUES ($w, $d)";
                mem.Parameters.AddWithValue("$w", ws.Id);
                mem.Parameters.AddWithValue("$d", documentId);
                mem.ExecuteNonQuery();
            }
            return ws;
        }
    }

    public void Rename(string id, string name, long nowUnixMs)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE workspace SET name=$name, updated_at=$t WHERE id=$id";
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$t", nowUnixMs);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void Delete(string id)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var del = conn.CreateCommand();
            del.CommandText = "DELETE FROM workspace WHERE id=$id; DELETE FROM workspace_document WHERE workspace_id=$id;";
            del.Parameters.AddWithValue("$id", id);
            del.ExecuteNonQuery();
        }
    }

    private static Workspace ReadRow(SqliteDataReader r) => new(
        r.GetString(0),
        r.GetString(1),
        r.GetInt64(2) != 0,
        r.IsDBNull(3) ? null : r.GetString(3),
        r.GetInt64(4),
        r.GetInt64(5));
}
