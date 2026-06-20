using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class SqliteDocumentIndex : IDocumentIndex
{
    private const string EmbeddingDim = "1536";
    private readonly SqliteConnection _conn;
    private readonly object _lock = new();
    private bool _vecAvailable;

    /// <summary>True when sqlite-vec loaded; false -> vector RAG degrades, FTS5 search still works.</summary>
    public bool VectorSearchAvailable => _vecAvailable;

    public SqliteDocumentIndex(string dbPath, string vec0Path)
    {
        _conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        _conn.Open();
        Exec("PRAGMA journal_mode=WAL;");
        try
        {
            _conn.EnableExtensions(true);
            _conn.LoadExtension(vec0Path);
            _vecAvailable = true;
        }
        catch (Exception)
        {
            _vecAvailable = false; // degrade: FTS5 only, RAG falls back to per-page context
        }
    }

    public void EnsureSchema()
    {
        lock (_lock)
        {
            Exec(@"
CREATE TABLE IF NOT EXISTS documents (
  document_id TEXT PRIMARY KEY, file_path TEXT, page_count INTEGER,
  chunk_count INTEGER, embedding_model TEXT, status TEXT, indexed_at INTEGER);
CREATE TABLE IF NOT EXISTS chunks (
  chunk_id INTEGER PRIMARY KEY AUTOINCREMENT, document_id TEXT NOT NULL,
  page_index INTEGER NOT NULL, ordinal INTEGER NOT NULL, text TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS idx_chunks_doc ON chunks(document_id);
CREATE VIRTUAL TABLE IF NOT EXISTS chunks_fts USING fts5(
  text, content='chunks', content_rowid='chunk_id');");
            if (_vecAvailable)
                Exec(@"CREATE VIRTUAL TABLE IF NOT EXISTS vec_chunks USING vec0(
  document_id TEXT partition key, chunk_id INTEGER PRIMARY KEY, embedding FLOAT[" + EmbeddingDim + "]);");
        }
    }

    public DocumentIndexStatus GetStatus(string documentId, string embeddingModel)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT status, embedding_model FROM documents WHERE document_id=$id";
            cmd.Parameters.AddWithValue("$id", documentId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return DocumentIndexStatus.None;

            string status = r.GetString(0);
            string model = r.IsDBNull(1) ? "" : r.GetString(1);
            // Model mismatch on a completed index means it must be rebuilt.
            if (status == "complete" && model != embeddingModel) return DocumentIndexStatus.None;

            return status switch
            {
                "indexing" => DocumentIndexStatus.Indexing,
                "complete" => DocumentIndexStatus.Complete,
                "partial" => DocumentIndexStatus.Partial,
                "text-only" => DocumentIndexStatus.TextOnly,
                "empty" => DocumentIndexStatus.Empty,
                _ => DocumentIndexStatus.None
            };
        }
    }

    private void DeleteDocumentCore(string documentId)
    {
        var deletes = new List<string>
        {
            "DELETE FROM chunks_fts WHERE rowid IN (SELECT chunk_id FROM chunks WHERE document_id=$id)",
            "DELETE FROM chunks WHERE document_id=$id",
            "DELETE FROM documents WHERE document_id=$id"
        };
        if (_vecAvailable)
            deletes.Insert(1, "DELETE FROM vec_chunks WHERE document_id=$id");

        using var tx = _conn.BeginTransaction();
        foreach (var sql in deletes)
        {
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$id", documentId);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void DeleteDocument(string documentId)
    {
        lock (_lock) { DeleteDocumentCore(documentId); }
    }

    public IReadOnlyList<long> WriteChunks(
        string documentId, string? filePath, int pageCount, IReadOnlyList<Chunk> chunks)
    {
        lock (_lock)
        {
            DeleteDocumentCore(documentId); // idempotent re-index, atomic with inserts
            var ids = new List<long>();
            using var tx = _conn.BeginTransaction();

            using (var doc = _conn.CreateCommand())
            {
                doc.Transaction = tx;
                doc.CommandText = @"INSERT INTO documents
                    (document_id, file_path, page_count, chunk_count, embedding_model, status, indexed_at)
                    VALUES ($id,$path,$pc,$cc,NULL,'indexing',$ts)";
                doc.Parameters.AddWithValue("$id", documentId);
                doc.Parameters.AddWithValue("$path", (object?)filePath ?? DBNull.Value);
                doc.Parameters.AddWithValue("$pc", pageCount);
                doc.Parameters.AddWithValue("$cc", chunks.Count);
                doc.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                doc.ExecuteNonQuery();
            }

            foreach (var c in chunks)
            {
                long id;
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"INSERT INTO chunks (document_id, page_index, ordinal, text)
                        VALUES ($d,$p,$o,$t); SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("$d", c.DocumentId);
                    cmd.Parameters.AddWithValue("$p", c.PageIndex);
                    cmd.Parameters.AddWithValue("$o", c.Ordinal);
                    cmd.Parameters.AddWithValue("$t", c.Text);
                    id = (long)cmd.ExecuteScalar()!;
                }
                using (var fts = _conn.CreateCommand())
                {
                    fts.Transaction = tx;
                    fts.CommandText = "INSERT INTO chunks_fts (rowid, text) VALUES ($id,$t)";
                    fts.Parameters.AddWithValue("$id", id);
                    fts.Parameters.AddWithValue("$t", c.Text);
                    fts.ExecuteNonQuery();
                }
                ids.Add(id);
            }

            tx.Commit();
            return ids;
        }
    }

    public void WriteEmbeddings(IReadOnlyList<(long ChunkId, float[] Vector)> embeddings)
    {
        if (!_vecAvailable) return; // degrade: no vector store available
        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            foreach (var (chunkId, vector) in embeddings)
            {
                using var cmd = _conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO vec_chunks (document_id, chunk_id, embedding)
                    VALUES ((SELECT document_id FROM chunks WHERE chunk_id=$id), $id, $vec)";
                cmd.Parameters.AddWithValue("$id", chunkId);
                cmd.Parameters.AddWithValue("$vec", ToJson(vector));
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public void SetStatus(string documentId, DocumentIndexStatus status, string embeddingModel)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE documents SET status=$s, embedding_model=$m WHERE document_id=$id";
            cmd.Parameters.AddWithValue("$s", status switch
            {
                DocumentIndexStatus.TextOnly => "text-only",
                _ => status.ToString().ToLowerInvariant()
            });
            cmd.Parameters.AddWithValue("$m", embeddingModel);
            cmd.Parameters.AddWithValue("$id", documentId);
            cmd.ExecuteNonQuery();
        }
    }

    // Task 4 implements this (wrap body in lock (_lock)).
    public List<SearchResult> SearchText(string documentId, string query, int limit = 50)
        => throw new NotImplementedException();

    // Task 5 implements this (wrap body in lock (_lock); return empty if !_vecAvailable).
    public List<Chunk> RetrieveRelevant(string documentId, float[] queryVector, int k = 5)
        => throw new NotImplementedException();

    internal static string ToJson(float[] vector) =>
        "[" + string.Join(",", vector.Select(f => f.ToString(CultureInfo.InvariantCulture))) + "]";

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        lock (_lock) { _conn.Dispose(); }
    }
}
