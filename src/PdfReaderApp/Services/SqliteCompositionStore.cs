using System.Text.Json;
using Microsoft.Data.Sqlite;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lưu Bản soạn trong compositions.db, tách khỏi notes.db và workspaces.db (ADR 0002, không FK chéo db).
/// Danh sách id Note nguồn lưu dạng mảng JSON để giữ đúng thứ tự đánh số <c>[n]</c>.</summary>
public sealed class SqliteCompositionStore : ICompositionStore
{
    private const long SchemaVersion = 3;
    private readonly string _connectionString;
    private readonly Lock _lock = new();

    public SqliteCompositionStore(string dbPath)
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
CREATE TABLE IF NOT EXISTS composition (
  id TEXT PRIMARY KEY,
  owner_key TEXT NOT NULL,
  title TEXT NOT NULL,
  kind TEXT NOT NULL,
  content TEXT NOT NULL,
  chat_model TEXT NOT NULL,
  source_note_ids TEXT NOT NULL,
  created_at INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS ix_composition_owner ON composition(owner_key);";
            cmd.ExecuteNonQuery();
            AddAdditionalRequestColumnIfMissing(conn);
            AddUpdatedAtColumnIfMissing(conn);

            using var ver = conn.CreateCommand();
            ver.CommandText = "PRAGMA user_version;";
            long current = (long)(ver.ExecuteScalar() ?? 0L);
            if (current < SchemaVersion)
            {
                using var set = conn.CreateCommand();
#pragma warning disable S2077 // PRAGMA không nhận tham số; chỉ ghép hằng số SchemaVersion (#123)
                set.CommandText = $"PRAGMA user_version = {SchemaVersion};";
#pragma warning restore S2077
                set.ExecuteNonQuery();
            }
        }
    }

    // Chuỗi migration tuyến tính: v1 (bảng gốc) -> v2 (additional_request, #292) -> v3 (updated_at, #294).
    // CREATE TABLE IF NOT EXISTS không thêm cột vào bảng cũ nên mỗi bước tự kiểm cột (chạy lại vẫn an toàn).
    private static void AddAdditionalRequestColumnIfMissing(SqliteConnection conn)
        => AddColumnIfMissing(conn, "additional_request", "TEXT NULL");

    private static void AddUpdatedAtColumnIfMissing(SqliteConnection conn)
        => AddColumnIfMissing(conn, "updated_at", "INTEGER");

    private static void AddColumnIfMissing(SqliteConnection conn, string column, string type)
    {
        using var probe = conn.CreateCommand();
        probe.CommandText = "SELECT COUNT(*) FROM pragma_table_info('composition') WHERE name=$name";
        probe.Parameters.AddWithValue("$name", column);
        if ((long)(probe.ExecuteScalar() ?? 0L) > 0) return;

        using var alter = conn.CreateCommand();
#pragma warning disable S2077 // DDL không nhận tham số; column và type là hằng của lớp này
        alter.CommandText = $"ALTER TABLE composition ADD COLUMN {column} {type}";
#pragma warning restore S2077
        alter.ExecuteNonQuery();
    }

    public void Add(Composition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO composition (id, owner_key, title, kind, content, chat_model, source_note_ids, created_at, additional_request)
VALUES ($id, $owner, $title, $kind, $content, $model, $sources, $created, $request);";
            cmd.Parameters.AddWithValue("$request", (object?)composition.AdditionalRequest ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", composition.Id);
            cmd.Parameters.AddWithValue("$owner", composition.OwnerKey);
            cmd.Parameters.AddWithValue("$title", composition.Title);
            cmd.Parameters.AddWithValue("$kind", composition.Kind.ToString());
            cmd.Parameters.AddWithValue("$content", composition.Content);
            cmd.Parameters.AddWithValue("$model", composition.ChatModel);
            cmd.Parameters.AddWithValue("$sources", JsonSerializer.Serialize(composition.SourceNoteIds));
            cmd.Parameters.AddWithValue("$created", composition.CreatedAtUnixMs);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<Composition> GetForOwner(string ownerKey)
    {
        lock (_lock)
        {
            var list = new List<Composition>();
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT id, owner_key, title, kind, content, chat_model, source_note_ids, created_at, additional_request, updated_at
FROM composition WHERE owner_key=$owner ORDER BY created_at DESC, rowid DESC";
            cmd.Parameters.AddWithValue("$owner", ownerKey);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new Composition(
                    r.GetString(0), r.GetString(1), r.GetString(2), ParseKind(r.GetString(3)),
                    r.GetString(4), r.GetString(5), ParseSources(r.GetString(6)), r.GetInt64(7),
                    r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetInt64(9)));
            }

            return list;
        }
    }

    public int Update(string id, string title, string content, long updatedAtUnixMs)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE composition SET title=$title, content=$content, updated_at=$updated WHERE id=$id";
            cmd.Parameters.AddWithValue("$title", title);
            cmd.Parameters.AddWithValue("$content", content);
            cmd.Parameters.AddWithValue("$updated", updatedAtUnixMs);
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
            cmd.CommandText = "DELETE FROM composition WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery();
        }
    }

    public int DeleteForOwner(string ownerKey)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM composition WHERE owner_key=$owner";
            cmd.Parameters.AddWithValue("$owner", ownerKey);
            return cmd.ExecuteNonQuery();
        }
    }

    private static CompositionKind ParseKind(string text)
        => Enum.TryParse(text, ignoreCase: false, out CompositionKind kind) ? kind : CompositionKind.Summary;

    private static List<string> ParseSources(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
#pragma warning disable CA1031 // degrade: JSON nguồn hỏng thì Bản soạn vẫn đọc được, chỉ mất liên kết [n]
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"SqliteCompositionStore: JSON id Note nguồn hỏng: {ex}");
            return [];
        }
#pragma warning restore CA1031
    }
}
