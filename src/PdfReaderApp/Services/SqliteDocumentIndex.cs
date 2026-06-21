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
  page_index INTEGER NOT NULL, ordinal INTEGER NOT NULL, text TEXT NOT NULL,
  search_text TEXT);
CREATE INDEX IF NOT EXISTS idx_chunks_doc ON chunks(document_id);
CREATE VIRTUAL TABLE IF NOT EXISTS chunks_fts USING fts5(
  search_text, content='chunks', content_rowid='chunk_id', tokenize='trigram');");
            if (_vecAvailable)
                Exec(@"CREATE VIRTUAL TABLE IF NOT EXISTS vec_chunks USING vec0(
  document_id TEXT partition key, chunk_id INTEGER PRIMARY KEY, embedding FLOAT[" + EmbeddingDim + "]);");

            // Migration: ensure existing DBs have search_text column and trigram FTS
            MigrateToTrigramIfNeeded();
        }
    }

    private void MigrateToTrigramIfNeeded()
    {
        // Check whether search_text column exists in chunks
        bool hasSearchText = false;
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(chunks)";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (r.GetString(1) == "search_text") { hasSearchText = true; break; }
            }
        }

        // Check whether chunks_fts is already a trigram table
        bool isTrigram = false;
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT sql FROM sqlite_master WHERE name='chunks_fts'";
            var val = cmd.ExecuteScalar() as string;
            isTrigram = val != null && val.Contains("trigram", StringComparison.OrdinalIgnoreCase);
        }

        if (hasSearchText && isTrigram) return; // nothing to migrate

        using var tx = _conn.BeginTransaction();

        if (!hasSearchText)
        {
            using var alter = _conn.CreateCommand();
            alter.Transaction = tx;
            alter.CommandText = "ALTER TABLE chunks ADD COLUMN search_text TEXT;";
            alter.ExecuteNonQuery();
        }

        // Recreate FTS as trigram (drop old non-trigram version)
        using (var drop = _conn.CreateCommand())
        {
            drop.Transaction = tx;
            drop.CommandText = "DROP TABLE IF EXISTS chunks_fts;";
            drop.ExecuteNonQuery();
        }
        using (var create = _conn.CreateCommand())
        {
            create.Transaction = tx;
            create.CommandText = @"CREATE VIRTUAL TABLE chunks_fts USING fts5(
  search_text, content='chunks', content_rowid='chunk_id', tokenize='trigram');";
            create.ExecuteNonQuery();
        }

        // Backfill search_text and FTS for existing rows
        var rowsToBackfill = new List<(long ChunkId, string Text)>();
        using (var sel = _conn.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT chunk_id, text FROM chunks";
            using var r = sel.ExecuteReader();
            while (r.Read())
                rowsToBackfill.Add((r.GetInt64(0), r.GetString(1)));
        }

        foreach (var (chunkId, text) in rowsToBackfill)
        {
            string folded = SearchNormalizer.Fold(text);
            using var upd = _conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = "UPDATE chunks SET search_text=$st WHERE chunk_id=$id";
            upd.Parameters.AddWithValue("$st", folded);
            upd.Parameters.AddWithValue("$id", chunkId);
            upd.ExecuteNonQuery();

            using var ins = _conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO chunks_fts(rowid, search_text) VALUES($id,$st)";
            ins.Parameters.AddWithValue("$id", chunkId);
            ins.Parameters.AddWithValue("$st", folded);
            ins.ExecuteNonQuery();
        }

        tx.Commit();
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
                string folded = SearchNormalizer.Fold(c.Text);
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"INSERT INTO chunks (document_id, page_index, ordinal, text, search_text)
                        VALUES ($d,$p,$o,$t,$st); SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("$d", c.DocumentId);
                    cmd.Parameters.AddWithValue("$p", c.PageIndex);
                    cmd.Parameters.AddWithValue("$o", c.Ordinal);
                    cmd.Parameters.AddWithValue("$t", c.Text);
                    cmd.Parameters.AddWithValue("$st", folded);
                    id = (long)cmd.ExecuteScalar()!;
                }
                using (var fts = _conn.CreateCommand())
                {
                    fts.Transaction = tx;
                    fts.CommandText = "INSERT INTO chunks_fts (rowid, search_text) VALUES ($id,$st)";
                    fts.Parameters.AddWithValue("$id", id);
                    fts.Parameters.AddWithValue("$st", folded);
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

    public List<SearchResult> SearchText(string documentId, string query, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<SearchResult>();

        lock (_lock)
        {
            var terms = SearchNormalizer.Fold(query)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0) return new List<SearchResult>();

            var results = new List<SearchResult>();

            try
            {
                bool allLongEnough = terms.All(t => t.Length >= 3);

                if (allLongEnough)
                {
                    // Trigram MATCH path: AND of quoted substrings
                    string matchExpr = string.Join(" AND ",
                        terms.Select(t => "\"" + t.Replace("\"", "\"\"") + "\""));

                    using var cmd = _conn.CreateCommand();
                    cmd.CommandText = @"
SELECT c.page_index,
       snippet(chunks_fts, 0, '[', ']', '...', 12) AS snip,
       c.chunk_id
FROM chunks_fts
JOIN chunks c ON c.chunk_id = chunks_fts.rowid
WHERE chunks_fts MATCH $q AND c.document_id = $id
ORDER BY rank
LIMIT $lim";
                    cmd.Parameters.AddWithValue("$q", matchExpr);
                    cmd.Parameters.AddWithValue("$id", documentId);
                    cmd.Parameters.AddWithValue("$lim", limit);

                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        results.Add(new SearchResult(r.GetInt32(0), r.GetString(1), r.GetInt64(2)));
                }
                else
                {
                    // LIKE fallback for terms shorter than 3 chars
                    var sb = new System.Text.StringBuilder(
                        "SELECT page_index, substr(search_text,1,80), chunk_id FROM chunks WHERE document_id=$id");
                    for (int i = 0; i < terms.Length; i++)
                        sb.Append($" AND search_text LIKE $p{i}");
                    sb.Append(" LIMIT $lim");

                    using var cmd = _conn.CreateCommand();
                    cmd.CommandText = sb.ToString();
                    cmd.Parameters.AddWithValue("$id", documentId);
                    for (int i = 0; i < terms.Length; i++)
                        cmd.Parameters.AddWithValue($"$p{i}", "%" + terms[i] + "%");
                    cmd.Parameters.AddWithValue("$lim", limit);

                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        results.Add(new SearchResult(r.GetInt32(0), r.GetString(1), r.GetInt64(2)));
                }
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                return new List<SearchResult>();
            }

            return results;
        }
    }

    public List<Chunk> RetrieveRelevant(string documentId, float[] queryVector, int k = 5)
    {
        ArgumentNullException.ThrowIfNull(queryVector);
        if (!_vecAvailable) return new List<Chunk>(); // degrade: caller falls back to per-page context
        lock (_lock)
        {
            var results = new List<Chunk>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
SELECT c.document_id, c.page_index, c.ordinal, c.text
FROM vec_chunks v
JOIN chunks c ON c.chunk_id = v.chunk_id
WHERE v.document_id = $id AND v.embedding MATCH $vec AND k = $k
ORDER BY distance";
            cmd.Parameters.AddWithValue("$id", documentId);
            cmd.Parameters.AddWithValue("$vec", ToJson(queryVector));
            cmd.Parameters.AddWithValue("$k", k);

            try
            {
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    results.Add(new Chunk(r.GetString(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3)));
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                return new List<Chunk>(); // degrade: vec0 query error, caller falls back
            }

            return results;
        }
    }

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
