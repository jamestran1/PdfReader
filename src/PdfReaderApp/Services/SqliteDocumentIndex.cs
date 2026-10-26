using System.Globalization;
using Microsoft.Data.Sqlite;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class SqliteDocumentIndex : IDocumentIndex
{
    // Hai cổng độc lập: di trú đa model (đổi hình dạng bảng) và re-fold search_text (đổi logic Fold).
    // Tăng RefoldSchemaVersion khi Fold đổi; không bao giờ tăng MultiModelSchemaVersion cho việc đó.
    private const int MultiModelSchemaVersion = 5;
    private const int RefoldSchemaVersion = 6;
    private const string VecTablePrefix = "vec_chunks_";
    private readonly SqliteConnection _conn;
    private readonly Lock _lock = new();
    private readonly bool _vecAvailable;
    private bool _orphanedIndexingReset;
    // Claim đang sống theo (document, model); chỉ trong bộ nhớ vì claim không sống qua lần mở lại (xem ResetOrphanedIndexingStatusOnce).
    private readonly Dictionary<(string DocumentId, string Model), long> _claimTokens = [];
    private long _lastClaimToken;

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
#pragma warning disable CA1031 // degrade: không nạp được vec0 thì chỉ dùng FTS5, RAG lùi về ngữ cảnh theo trang
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"SqliteDocumentIndex: không nạp được vec0: {ex}");
            _vecAvailable = false; // degrade: FTS5 only, RAG falls back to per-page context
        }
#pragma warning restore CA1031
    }

    public void EnsureSchema()
    {
        lock (_lock)
        {
            MigrateToMultiModelIfNeeded();
            // Bảng đơn vec_chunks cũ còn sót (DB di trú lúc vec0 không nạp được) chỉ DROP được khi vec0 sẵn sàng.
            if (_vecAvailable) Exec("DROP TABLE IF EXISTS vec_chunks;");

            Exec(@"
CREATE TABLE IF NOT EXISTS documents (
  document_id TEXT PRIMARY KEY, file_path TEXT, page_count INTEGER,
  chunk_count INTEGER, indexed_at INTEGER);
CREATE TABLE IF NOT EXISTS document_models (
  document_id TEXT NOT NULL, model_name TEXT NOT NULL,
  status TEXT NOT NULL, indexed_at INTEGER NOT NULL,
  PRIMARY KEY (document_id, model_name));
CREATE TABLE IF NOT EXISTS chunks (
  chunk_id INTEGER PRIMARY KEY AUTOINCREMENT, document_id TEXT NOT NULL,
  page_index INTEGER NOT NULL, ordinal INTEGER NOT NULL, text TEXT NOT NULL,
  search_text TEXT);
CREATE INDEX IF NOT EXISTS idx_chunks_doc ON chunks(document_id);
CREATE VIRTUAL TABLE IF NOT EXISTS chunks_fts USING fts5(
  search_text, content='chunks', content_rowid='chunk_id', tokenize='trigram');");

            // Migration: ensure existing DBs have search_text column and trigram FTS
            MigrateToTrigramIfNeeded();

            // Re-fold search_text if Fold logic changed (version gate)
            RefoldSearchTextIfStale();

            ResetOrphanedIndexingStatusOnce();
        }
    }

    private int ReadUserVersion()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    // v5: vector chuyển sang bảng theo số chiều và theo model, status chuyển sang document_models.
    // Index cũ gắn với một model duy nhất nên xoá hết để mọi tài liệu lập chỉ mục lại sạch.
    // Nhận diện DB cũ bằng hình dạng bảng documents (còn cột status/embedding_model), không chỉ bằng số phiên bản.
    private void MigrateToMultiModelIfNeeded()
    {
        if (ReadUserVersion() >= MultiModelSchemaVersion) return;

        using var shape = _conn.CreateCommand();
        shape.CommandText = "SELECT COUNT(*) FROM pragma_table_info('documents') WHERE name IN ('status','embedding_model')";
        bool hasLegacyShape = Convert.ToInt32(shape.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;

        using var tx = _conn.BeginTransaction();
        if (hasLegacyShape)
        {
            ExecInTransaction(tx, "DROP TABLE IF EXISTS chunks_fts;");
            ExecInTransaction(tx, "DROP TABLE IF EXISTS chunks;");
            ExecInTransaction(tx, "DROP TABLE IF EXISTS documents;");
        }

        // PRAGMA không nhận tham số; chỉ ghép hằng số. Sonar không lần qua helper nên không cần tắt S2077 (#123).
        ExecInTransaction(tx, $"PRAGMA user_version = {MultiModelSchemaVersion}");
        tx.Commit();
    }

    private void MigrateToTrigramIfNeeded()
    {
        bool hasSearchText = HasSearchTextColumn();
        if (hasSearchText && IsChunksFtsTrigram()) return; // nothing to migrate

        using var tx = _conn.BeginTransaction();

        if (!hasSearchText)
            ExecInTransaction(tx, "ALTER TABLE chunks ADD COLUMN search_text TEXT;");

        // Recreate FTS as trigram (drop old non-trigram version)
        ExecInTransaction(tx, "DROP TABLE IF EXISTS chunks_fts;");
        ExecInTransaction(tx, @"CREATE VIRTUAL TABLE chunks_fts USING fts5(
  search_text, content='chunks', content_rowid='chunk_id', tokenize='trigram');");

        BackfillSearchTextAndFts(tx);

        tx.Commit();
    }

    private bool HasSearchTextColumn()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(chunks)";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (string.Equals(r.GetString(1), "search_text", StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private bool IsChunksFtsTrigram()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE name='chunks_fts'";
        var val = cmd.ExecuteScalar() as string;
        return val != null && val.Contains("trigram", StringComparison.OrdinalIgnoreCase);
    }

    private void ExecInTransaction(SqliteTransaction tx, string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // Backfill search_text and FTS for existing rows.
    private void BackfillSearchTextAndFts(SqliteTransaction tx)
    {
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
    }

    private void RefoldSearchTextIfStale()
    {
        // Read current user_version
        int currentVersion;
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA user_version";
            currentVersion = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        if (currentVersion >= RefoldSchemaVersion) return;

        // Re-fold every chunk's search_text with the current Fold logic,
        // then rebuild the external-content FTS index in one shot.
        var rowsToRefold = new List<(long ChunkId, string Text)>();
        using (var sel = _conn.CreateCommand())
        {
            sel.CommandText = "SELECT chunk_id, text FROM chunks";
            using var r = sel.ExecuteReader();
            while (r.Read())
                rowsToRefold.Add((r.GetInt64(0), r.GetString(1)));
        }

        using var tx = _conn.BeginTransaction();

        foreach (var (chunkId, text) in rowsToRefold)
        {
            string folded = SearchNormalizer.Fold(text);
            using var upd = _conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = "UPDATE chunks SET search_text=$st WHERE chunk_id=$id";
            upd.Parameters.AddWithValue("$st", folded);
            upd.Parameters.AddWithValue("$id", chunkId);
            upd.ExecuteNonQuery();
        }

        // Rebuild FTS index from updated content table
        using (var rebuild = _conn.CreateCommand())
        {
            rebuild.Transaction = tx;
            rebuild.CommandText = "INSERT INTO chunks_fts(chunks_fts) VALUES('rebuild')";
            rebuild.ExecuteNonQuery();
        }

        // Stamp the new version
        using (var ver = _conn.CreateCommand())
        {
            ver.Transaction = tx;
#pragma warning disable S2077 // PRAGMA không nhận tham số; chỉ ghép hằng số RefoldSchemaVersion (#123)
            ver.CommandText = $"PRAGMA user_version = {RefoldSchemaVersion}";
#pragma warning restore S2077
            ver.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public DocumentIndexStatus GetStatus(string documentId, string embeddingModel)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT status FROM document_models WHERE document_id=$id AND model_name=$m";
            cmd.Parameters.AddWithValue("$id", documentId);
            cmd.Parameters.AddWithValue("$m", embeddingModel);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return DocumentIndexStatus.None;

            string raw = r.GetString(0);
            return Enum.GetValues<DocumentIndexStatus>().FirstOrDefault(v => ToDbStatus(v) == raw);
        }
    }

    private void DeleteDocumentCore(string documentId)
    {
        var deletes = new List<string>();
        // Tên bảng từ ListVecTables (chỉ tiền tố và chữ số); Sonar không lần theo List nên không báo (#123).
        if (_vecAvailable)
            deletes.AddRange(ListVecTables().Select(t => $"DELETE FROM {t} WHERE document_id=$id"));
        deletes.Add("DELETE FROM chunks_fts WHERE rowid IN (SELECT chunk_id FROM chunks WHERE document_id=$id)");
        deletes.Add("DELETE FROM chunks WHERE document_id=$id");
        deletes.Add("DELETE FROM document_models WHERE document_id=$id");
        deletes.Add("DELETE FROM documents WHERE document_id=$id");

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
            var claimedModels = ReadIndexingModels(documentId);
            DeleteDocumentCore(documentId); // idempotent re-index, atomic with inserts
            foreach (string model in claimedModels) SetStatus(documentId, DocumentIndexStatus.Indexing, model);
            var ids = new List<long>();
            using var tx = _conn.BeginTransaction();

            using (var doc = _conn.CreateCommand())
            {
                doc.Transaction = tx;
                doc.CommandText = @"INSERT INTO documents
                    (document_id, file_path, page_count, chunk_count, indexed_at)
                    VALUES ($id,$path,$pc,$cc,$ts)";
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

    public IReadOnlyList<StoredChunk> GetChunks(string documentId)
    {
        lock (_lock)
        {
            var result = new List<StoredChunk>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT chunk_id, text FROM chunks WHERE document_id=$id ORDER BY chunk_id";
            cmd.Parameters.AddWithValue("$id", documentId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                result.Add(new StoredChunk(r.GetInt64(0), r.GetString(1)));

            return result;
        }
    }

    public void WriteEmbeddings(string embeddingModel, IReadOnlyList<(long ChunkId, float[] Vector)> embeddings)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        if (embeddings.Count == 0) return;
        int dim = embeddings[0].Vector?.Length ?? 0;
        if (dim == 0 || embeddings.Any(e => e.Vector is null || e.Vector.Length != dim))
            throw new ArgumentException("Mọi vector trong một lô phải cùng số chiều và không rỗng.", nameof(embeddings));
        if (!_vecAvailable) return; // degrade: no vector store available
        lock (_lock)
        {
            EnsureVecTable(dim);

            using var tx = _conn.BeginTransaction();
            DeleteExistingVectors(tx, embeddingModel, embeddings, dim);
            foreach (var (chunkId, vector) in embeddings)
            {
                using var cmd = _conn.CreateCommand();
                cmd.Transaction = tx;
#pragma warning disable S2077 // tên bảng từ VecTableName(int), giá trị qua tham số (#123)
                cmd.CommandText = $@"INSERT INTO {VecTableName(dim)} (model_name, document_id, chunk_id, embedding)
                    VALUES ($m, (SELECT document_id FROM chunks WHERE chunk_id=$id), $id, $vec)";
#pragma warning restore S2077
                cmd.Parameters.AddWithValue("$m", embeddingModel);
                cmd.Parameters.AddWithValue("$id", chunkId);
                cmd.Parameters.AddWithValue("$vec", ToJson(vector));
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    // chunk_id chỉ là cột metadata nên vec0 không tự chặn trùng; tác vụ lập chỉ mục bị huỷ mà còn chạy
    // dở có thể ghi lại cùng chunk, nên xoá bản cũ của (model, chunk) trước khi chèn.
    private void DeleteExistingVectors(
        SqliteTransaction tx, string embeddingModel, IReadOnlyList<(long ChunkId, float[] Vector)> embeddings, int dim)
    {
        var batchIds = embeddings.Select(e => e.ChunkId).ToHashSet();
        var documentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (long id in batchIds)
        {
            using var doc = _conn.CreateCommand();
            doc.Transaction = tx;
            doc.CommandText = "SELECT document_id FROM chunks WHERE chunk_id=$id";
            doc.Parameters.AddWithValue("$id", id);
            if (doc.ExecuteScalar() is string documentId) documentIds.Add(documentId);
        }

        var staleRowIds = new List<long>();
        foreach (string documentId in documentIds)
        {
            using var scan = _conn.CreateCommand();
            scan.Transaction = tx;
#pragma warning disable S2077 // tên bảng từ VecTableName(int), giá trị qua tham số (#123)
            scan.CommandText = $"SELECT rowid, chunk_id FROM {VecTableName(dim)} WHERE model_name=$m AND document_id=$d";
#pragma warning restore S2077
            scan.Parameters.AddWithValue("$m", embeddingModel);
            scan.Parameters.AddWithValue("$d", documentId);
            using var r = scan.ExecuteReader();
            while (r.Read())
            {
                if (batchIds.Contains(r.GetInt64(1))) staleRowIds.Add(r.GetInt64(0));
            }
        }

        foreach (long rowId in staleRowIds)
        {
            using var del = _conn.CreateCommand();
            del.Transaction = tx;
#pragma warning disable S2077 // tên bảng từ VecTableName(int), giá trị qua tham số (#123)
            del.CommandText = $"DELETE FROM {VecTableName(dim)} WHERE rowid=$r";
#pragma warning restore S2077
            del.Parameters.AddWithValue("$r", rowId);
            del.ExecuteNonQuery();
        }
    }

    public void DeleteEmbeddings(string documentId, string embeddingModel)
    {
        if (!_vecAvailable) return;
        lock (_lock)
        {
            foreach (string table in ListVecTables())
            {
                using var cmd = _conn.CreateCommand();
#pragma warning disable S2077 // tên bảng từ ListVecTables, đã kiểm chỉ gồm tiền tố và chữ số (#123)
                cmd.CommandText = $"DELETE FROM {table} WHERE model_name=$m AND document_id=$id";
#pragma warning restore S2077
                cmd.Parameters.AddWithValue("$m", embeddingModel);
                cmd.Parameters.AddWithValue("$id", documentId);
                cmd.ExecuteNonQuery();
            }
        }
    }

    // Tên bảng chỉ ghép từ int đã định dạng invariant, không bao giờ từ chuỗi người dùng.
    private static string VecTableName(int dimension) =>
        VecTablePrefix + dimension.ToString(CultureInfo.InvariantCulture);

    // model_name và document_id là partition key để KNN chỉ quét đúng một model của một tài liệu,
    // hai model cùng số chiều dùng chung bảng mà không lẫn vector; chunk_id là cột metadata
    // vì một chunk có thể có vector của nhiều model nên không làm khoá chính được.
    private void EnsureVecTable(int dimension)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        // Chỉ ghép số nguyên dimension; Sonar không lần qua helper nên không cần tắt S2077 (#123).
        Exec($@"CREATE VIRTUAL TABLE IF NOT EXISTS {VecTableName(dimension)} USING vec0(
  model_name TEXT partition key, document_id TEXT partition key,
  chunk_id INTEGER, embedding FLOAT[{dimension.ToString(CultureInfo.InvariantCulture)}]);");
    }

    private bool VecTableExists(int dimension)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$n";
        cmd.Parameters.AddWithValue("$n", VecTableName(dimension));
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    // Bảng vec0 là bảng ảo; các bảng phụ (_chunks, _rowids...) có sql 'CREATE TABLE' nên bị loại.
    private List<string> ListVecTables()
    {
        var tables = new List<string>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT name FROM sqlite_master
            WHERE type='table' AND sql LIKE 'CREATE VIRTUAL TABLE%' AND name GLOB 'vec_chunks_[0-9]*'";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string name = r.GetString(0);
            if (name[VecTablePrefix.Length..].All(char.IsAsciiDigit)) tables.Add(name);
        }

        return tables;
    }

    private static string ToDbStatus(DocumentIndexStatus status) => status switch
    {
        DocumentIndexStatus.TextOnly => "text-only",
        _ => status.ToString().ToLowerInvariant()
    };

    public bool TryBeginIndexing(string documentId, string embeddingModel, out long claimToken)
    {
        claimToken = 0;
        // Kiểm tra và đặt trong cùng một lock: hai lượt (tài liệu đang mở và hàng đợi Workspace) không thể cùng thắng.
        lock (_lock)
        {
            if (GetStatus(documentId, embeddingModel) == DocumentIndexStatus.Indexing) return false;
            SetStatus(documentId, DocumentIndexStatus.Indexing, embeddingModel);
            claimToken = ++_lastClaimToken;
            _claimTokens[(documentId, embeddingModel)] = claimToken;
            return true;
        }
    }

    public bool WriteEmbeddingsIfClaimed(
        string documentId, string embeddingModel, long claimToken, IReadOnlyList<(long ChunkId, float[] Vector)> embeddings)
    {
        // Kiểm claim và ghi trong cùng một lock (Monitor cho phép vào lại) nên không có khe hở giữa kiểm và ghi.
        lock (_lock)
        {
            if (!IsClaimCurrent(documentId, embeddingModel, claimToken)) return false;
            WriteEmbeddings(embeddingModel, embeddings);
            return true;
        }
    }

    public bool FinishIndexing(string documentId, string embeddingModel, long claimToken, DocumentIndexStatus status)
    {
        lock (_lock)
        {
            if (!IsClaimCurrent(documentId, embeddingModel, claimToken)) return false;
            _claimTokens.Remove((documentId, embeddingModel));
            SetStatus(documentId, status, embeddingModel);
            return true;
        }
    }

    // Claim còn hiệu lực khi token khớp VÀ hàng trạng thái vẫn là Indexing (DeleteDocument xoá hàng nhưng để lại token cũ).
    // Gọi trong _lock.
    private bool IsClaimCurrent(string documentId, string embeddingModel, long claimToken)
        => _claimTokens.TryGetValue((documentId, embeddingModel), out long current)
            && current == claimToken
            && GetStatus(documentId, embeddingModel) == DocumentIndexStatus.Indexing;

    public void ReleaseIndexing(string documentId, string embeddingModel, long claimToken)
    {
        lock (_lock)
        {
            // Claim đã đổi chủ (tài liệu bị xoá rồi lượt khác giành quyền) hoặc hàng trạng thái đã mất thì không đụng tới.
            if (!IsClaimCurrent(documentId, embeddingModel, claimToken)) return;
            _claimTokens.Remove((documentId, embeddingModel));
            SetStatus(documentId, DocumentIndexStatus.Partial, embeddingModel);
        }
    }

    // Trạng thái Indexing mà không còn tác vụ nào chạy (app tắt hoặc sập giữa chừng) sẽ chặn lập chỉ mục mãi mãi.
    // Chạy một lần sau khi mở: EnsureSchema gọi lại giữa phiên không được xoá claim đang sống.
    private void ResetOrphanedIndexingStatusOnce()
    {
        if (_orphanedIndexingReset) return;
        _orphanedIndexingReset = true;
        Exec($"UPDATE document_models SET status='{ToDbStatus(DocumentIndexStatus.Partial)}' WHERE status='{ToDbStatus(DocumentIndexStatus.Indexing)}'");
    }

    // WriteChunks xoá mọi trạng thái của tài liệu; claim Indexing của lượt đang ghi chunk phải được giữ lại.
    private List<string> ReadIndexingModels(string documentId)
    {
        var models = new List<string>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT model_name FROM document_models WHERE document_id=$id AND status=$s";
        cmd.Parameters.AddWithValue("$id", documentId);
        cmd.Parameters.AddWithValue("$s", ToDbStatus(DocumentIndexStatus.Indexing));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            models.Add(r.GetString(0));
        }

        return models;
    }

    public void SetStatus(string documentId, DocumentIndexStatus status, string embeddingModel)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO document_models (document_id, model_name, status, indexed_at)
                VALUES ($id, $m, $s, $ts)
                ON CONFLICT(document_id, model_name) DO UPDATE SET status=excluded.status, indexed_at=excluded.indexed_at";
            cmd.Parameters.AddWithValue("$s", ToDbStatus(status));
            cmd.Parameters.AddWithValue("$m", embeddingModel);
            cmd.Parameters.AddWithValue("$id", documentId);
            cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<SearchResult> SearchText(string documentId, string query, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        lock (_lock)
        {
            string phrase = SearchNormalizer.Fold(query);
            if (phrase.Length == 0) return [];

            try
            {
                return phrase.Length >= 3
                    ? SearchTrigram(documentId, query, phrase, limit)
                    : SearchLike(documentId, query, phrase, limit);
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                return [];
            }
        }
    }

    // Trigram MATCH path: match the whole folded phrase as a single quoted string
    // so "kinh hanh" matches the contiguous substring, not AND-of-words.
    private List<SearchResult> SearchTrigram(string documentId, string query, string phrase, int limit)
    {
        string matchExpr = "\"" + phrase.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT c.page_index,
       c.text,
       c.chunk_id
FROM chunks_fts
JOIN chunks c ON c.chunk_id = chunks_fts.rowid
WHERE chunks_fts MATCH $q AND c.document_id = $id
ORDER BY rank
LIMIT $lim";
        cmd.Parameters.AddWithValue("$q", matchExpr);
        cmd.Parameters.AddWithValue("$id", documentId);
        cmd.Parameters.AddWithValue("$lim", limit);
        return ReadSearchResults(cmd, query);
    }

    // LIKE fallback for phrases shorter than 3 chars (trigram can't index them).
    private List<SearchResult> SearchLike(string documentId, string query, string phrase, int limit)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT page_index, text, chunk_id FROM chunks " +
            "WHERE document_id=$id AND search_text LIKE $p LIMIT $lim";
        cmd.Parameters.AddWithValue("$id", documentId);
        cmd.Parameters.AddWithValue("$p", "%" + phrase + "%");
        cmd.Parameters.AddWithValue("$lim", limit);
        return ReadSearchResults(cmd, query);
    }

    private static List<SearchResult> ReadSearchResults(SqliteCommand cmd, string query)
    {
        var results = new List<SearchResult>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            results.Add(new SearchResult(
                r.GetInt32(0),
                SearchSnippetBuilder.Build(r.GetString(1), query),
                r.GetInt64(2)));
        }

        return results;
    }

    public IReadOnlyList<Chunk> RetrieveRelevant(
        string documentId, string embeddingModel, float[] queryVector, int k = 5)
        => [.. RetrieveRelevantScored(documentId, embeddingModel, queryVector, k).Select(s => s.Chunk)];

    public IReadOnlyList<ScoredChunk> RetrieveRelevantScored(
        string documentId, string embeddingModel, float[] queryVector, int k = 5)
    {
        ArgumentNullException.ThrowIfNull(queryVector);
        if (!_vecAvailable || queryVector.Length == 0) return []; // degrade: caller falls back to per-page context
        lock (_lock)
        {
            // Chưa có model nào ghi vector ở số chiều này thì chưa có bảng.
            if (!VecTableExists(queryVector.Length)) return [];

            var results = new List<ScoredChunk>();
            using var cmd = _conn.CreateCommand();
#pragma warning disable S2077 // tên bảng từ VecTableName(int), giá trị qua tham số (#123)
            cmd.CommandText = $@"
SELECT c.document_id, c.page_index, c.ordinal, c.text, v.distance
FROM {VecTableName(queryVector.Length)} v
JOIN chunks c ON c.chunk_id = v.chunk_id
WHERE v.model_name = $m AND v.document_id = $id AND v.embedding MATCH $vec AND k = $k
ORDER BY distance";
#pragma warning restore S2077
            cmd.Parameters.AddWithValue("$m", embeddingModel);
            cmd.Parameters.AddWithValue("$id", documentId);
            cmd.Parameters.AddWithValue("$vec", ToJson(queryVector));
            cmd.Parameters.AddWithValue("$k", k);

            try
            {
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    results.Add(new ScoredChunk(
                        new Chunk(r.GetString(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3)), r.GetDouble(4)));
                }
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                return []; // degrade: vec0 query error, caller falls back
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
