using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lưu Bản soạn trong compositions.db, tách khỏi notes.db và workspaces.db (ADR 0002, không FK chéo db).
/// Danh sách id Note nguồn lưu dạng mảng JSON để giữ đúng thứ tự đánh số <c>[n]</c>.</summary>
public sealed class SqliteCompositionStore : ICompositionStore
{
    private const long SchemaVersion = 1;
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

    public void Add(Composition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO composition (id, owner_key, title, kind, content, chat_model, source_note_ids, created_at)
VALUES ($id, $owner, $title, $kind, $content, $model, $sources, $created);";
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
SELECT id, owner_key, title, kind, content, chat_model, source_note_ids, created_at
FROM composition WHERE owner_key=$owner ORDER BY created_at DESC, rowid DESC";
            cmd.Parameters.AddWithValue("$owner", ownerKey);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new Composition(
                    r.GetString(0), r.GetString(1), r.GetString(2), ParseKind(r.GetString(3)),
                    r.GetString(4), r.GetString(5), ParseSources(r.GetString(6)), r.GetInt64(7)));
            }

            return list;
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
