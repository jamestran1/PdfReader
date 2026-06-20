# Thiết kế: SQLite Index + RAG + Search — Sub-project 2 (SP2)

**Ngày:** 2026-06-20
**Trạng thái:** Đã duyệt design, chờ implementation plan
**Phụ thuộc:** SP1 (OpenAI chat — `AiChatService.AskStreamingAsync(question, context)`, hạ tầng key/OpenAI), Phase 1 iText (`TextBlock`, `PdfStructureAnalyzer.AnalyzeRich()`), `PdfCoordinateMapper` (Phase 1).
**Động cơ:** Thay context "nhồi theo trang" của SP1 bằng RAG truy hồi theo ngữ nghĩa (chính xác cho tài liệu dài), và hiện thực feature Search trên toolbar (hiện chỉ có ô TextBox). Index một lần vào SQLite, dùng lại.

## 1. Quyết định đã chốt

| Hạng mục | Quyết định |
|---|---|
| Chunking | Gom `TextBlock` theo trang, cắt **~900 ký tự, overlap ~100** |
| DB | **Một DB chung** `%APPDATA%\PdfReaderApp\index.db`, phân biệt tài liệu bằng `document_id` = **SHA256 nội dung file** |
| Khi index | **Tự động khi mở file, chạy nền**, có `IProgress` |
| Vector search | **sqlite-vec** (`vec0` virtual table, KNN trong SQL) — native `vec0.dll`, Windows x64, gói cùng app |
| Embedding model | OpenAI **`text-embedding-3-small`** (1536 chiều), gọi qua `Microsoft.Extensions.AI` + provider OpenAI, cùng API key SP1 |
| Search UX | **List kết quả + nhảy tới trang + highlight trên trang** (Skia overlay + `PdfCoordinateMapper`) |
| RAG retrieval | **vector-only top-k** (`k=5`); hybrid FTS5+vector để sau (YAGNI) |

> Quyết định sqlite-vec **thay** ghi chú "cosine trong C#" trong spec SP1 (2026-06-20-ai-chat-openai-design.md §8).

## 2. Kiến trúc & Components

```
┌──────────── MainViewModel ────────────┐
│ Open→index nền   Search→FTS5   Chat→RAG│
└───┬───────────────┬──────────────┬─────┘
┌───▼─────────────┐ │       ┌──────▼─────────────┐
│ DocumentIndexing│ │       │  AiChatService     │ (SP1, giữ nguyên)
│ Service          │ │      │ AskStreamingAsync  │
└───┬─────────────┘ │       └────────────────────┘
    │ dùng          │ dùng
┌───▼───────────────▼────────────────────┐
│           IDocumentIndex (facade)       │
│  EnsureSchema/SearchText/RetrieveRelevant│
│  impl: SqliteDocumentIndex              │
│   • FTS5 (search)   • vec0 (RAG)         │
└───┬──────────────────────┬──────────────┘
┌───▼──────────┐   ┌────────▼─────────────────┐
│ TextChunker  │   │ IEmbeddingGeneratorFactory │
│ (pure)       │   │ + OpenAi… (M.E.AI)        │
└──────────────┘   └──────────────────────────┘
```

| Thành phần | Vai trò | Test |
|---|---|---|
| `TextChunker` (pure) | `List<TextBlock>` → `List<Chunk{PageIndex,Ordinal,Text}>` (gom theo trang, cắt ~900, overlap ~100) | unit |
| `DocumentId` | SHA256 nội dung file → `document_id` | unit |
| `IDocumentIndex` + `SqliteDocumentIndex` | Facade SQLite: `EnsureSchema`, ghi chunks/FTS5/vec0, `SearchText` (FTS5), `RetrieveRelevant` (vec0 KNN), quản `documents.status` | unit (DB temp) |
| `IEmbeddingGeneratorFactory` + `OpenAiEmbeddingGeneratorFactory` | Dựng `IEmbeddingGenerator` (M.E.AI) từ key — pattern giống `IChatClientFactory` (SP1) | mock |
| `DocumentIndexingService` | Điều phối: hash → check → chunk → embed (batch) → ghi DB; nền + `IProgress` + `CancellationToken` | mock |
| Search highlight overlay | `SearchResult{PageIndex,Snippet,ChunkId}` → Skia overlay (rect tọa độ trang) qua `PdfCoordinateMapper` | thủ công |

**Ranh giới:** VM giữ `AskStreamingAsync(question, context)` của SP1 — chỉ **đổi nguồn context** sang `IDocumentIndex.RetrieveRelevant`. Native `vec0.dll` nạp qua `Microsoft.Data.Sqlite` `LoadExtension`.

## 3. Mô hình dữ liệu (SQLite)

```sql
CREATE TABLE documents (
  document_id     TEXT PRIMARY KEY,   -- SHA256 nội dung file
  file_path       TEXT,
  page_count      INTEGER,
  chunk_count     INTEGER,
  embedding_model TEXT,               -- 'text-embedding-3-small'
  status          TEXT,               -- 'indexing'|'complete'|'partial'|'text-only'|'empty'
  indexed_at      INTEGER
);

CREATE TABLE chunks (
  chunk_id    INTEGER PRIMARY KEY AUTOINCREMENT,
  document_id TEXT NOT NULL,
  page_index  INTEGER NOT NULL,       -- 0-based
  ordinal     INTEGER NOT NULL,
  text        TEXT NOT NULL
);
CREATE INDEX idx_chunks_doc ON chunks(document_id);

CREATE VIRTUAL TABLE chunks_fts USING fts5(
  text, content='chunks', content_rowid='chunk_id');

CREATE VIRTUAL TABLE vec_chunks USING vec0(
  chunk_id INTEGER PRIMARY KEY, embedding FLOAT[1536]);
```

- 3 cấu trúc cùng nói về 1 chunk, nối qua `chunk_id`: `chunks` (canonical) / `chunks_fts` (tìm từ khóa) / `vec_chunks` (tìm gần nghĩa). FTS5 external-content → không nhân đôi text.
- **Highlight KHÔNG lưu bounds trong DB:** Search trả `page_index`; VM dùng `_documentBlocks` (TextBlock có bounds, cache từ SP1) để tìm block chứa từ khóa trên trang đó rồi map qua `PdfCoordinateMapper`.
- `embedding_model` lưu kèm để phát hiện đổi model → re-index.
- Embedding lưu BLOB float[1536] định dạng vec0.

> Cú pháp DDL/KNN chính xác của vec0 (primary-key column, `MATCH ... k=`, partition theo `document_id` nếu hỗ trợ) verify lúc implement; rủi ro cô lập trong `SqliteDocumentIndex`.

## 4. Luồng index (background)

```
Mở file → document_id = SHA256(file). Tra documents:
  complete + embedding_model khớp   → dùng lại, bỏ qua
  text-only + giờ có key            → embed bổ sung
  embedding_model lệch / indexing cũ→ index lại (xóa chunk cũ trước)
  chưa có                           → index mới
Index (Task nền, IProgress, CancellationToken):
  a. _documentBlocks → TextChunker → List<Chunk>
  b. transaction: documents(status='indexing') + chunks + chunks_fts → commit
     → SEARCH SẴN SÀNG
  c. nếu có key+mạng: batch (≈100) → IEmbeddingGenerator → vec_chunks; status='complete'
                                                            → RAG SẴN SÀNG
     nếu không: status='text-only'  (Search vẫn chạy; RAG fallback)
  d. IProgress báo % theo chunk đã embed
```
Idempotent: index lại = xóa chunks/vec/fts của `document_id` rồi làm lại. Không chặn UI.

## 5. Truy hồi & tích hợp

**Chat (RAG), giữ ranh giới SP1:**
```
status=='complete': context = RetrieveRelevant(documentId, question, k=5)
   → embed câu hỏi → vec0 KNN top-5 trong document_id → nối text (cắt theo cap token 48000 của SP1)
ngược lại: context = DocumentContextBuilder.BuildAround(...)   ← fallback SP1 (trang ±2)
→ AiChatService.AskStreamingAsync(question, context)           ← KHÔNG đổi
```
Chat luôn chạy: chưa index xong dùng tạm theo trang, xong tự nâng RAG. Có thể đính "(nguồn: trang X)" từ `page_index`.

**Search (FTS5):**
```
Gõ + Enter → SearchText(documentId, query) → chunks_fts MATCH
   → List<SearchResult{page_index, snippet, chunk_id}> → popup/panel dưới ô search
click kết quả → CurrentPage=page_index (nhảy) + highlight:
   quét _documentBlocks trên trang chứa từ khóa → PdfCoordinateMapper → overlay Skia
```

**Component UI mới:** `MainViewModel` (+`IDocumentIndex`, `DocumentIndexingService`, `SearchCommand`, `SearchResults`, `IndexingProgress`); `PdfViewerControl` (+lớp overlay highlight nhận rect tọa độ trang + trang hiện tại); panel kết quả search MaterialDesign.

## 6. Xử lý lỗi

Degrade nhẹ, không crash; dùng lại `AiErrorClassifier` (SP1) cho lỗi OpenAI.

| Tình huống | Xử lý |
|---|---|
| Không key/mạng khi index | `text-only`; Search FTS5 chạy; RAG fallback theo trang; báo nhẹ |
| Lỗi embedding giữa batch | phân loại; lưu phần đã embed; `partial`; thử tiếp lần sau; fallback |
| `vec0.dll` nạp lỗi | degrade: FTS5 search chạy, RAG fallback; cảnh báo 1 lần |
| DB lock/IO | WAL + retry busy; không crash |
| DB hỏng | backup + tạo lại schema |
| File đổi nội dung | hash mới → tự index lại |
| PDF không có text (scan) | 0 chunk → `empty`; báo "không có text trích được" (OCR ngoài phạm vi) |
| Index dở do đóng app | `indexing` cũ → index lại từ đầu |
| Đổi embedding model | `embedding_model` lệch → re-index |

## 7. Testing (xUnit)

| Nhóm | Nội dung |
|---|---|
| `TextChunker` (pure) | gom theo trang; ~900 + overlap ~100; không vượt max; trang ngắn→1 chunk; rỗng→0; ranh giới/overlap đúng; `page_index` đúng |
| `DocumentId` | cùng bytes→cùng hash; khác→khác; ổn định |
| `SqliteDocumentIndex` FTS5 | DB temp: ghi chunks → SearchText đúng chunk + page + snippet; lọc theo document_id |
| `SqliteDocumentIndex` vec0 KNN | nạp vec0; vector dựng sẵn → RetrieveRelevant top-k đúng thứ tự; lọc document_id |
| `SqliteDocumentIndex` schema/idempotent | tạo schema; index lại → không nhân đôi |
| `DocumentIndexingService` | mock `IEmbeddingGenerator`: chunk→embed→store; status transitions; không key→text-only; IProgress; CancellationToken |

**Không test (thủ công):** gọi OpenAI embedding thật, Skia highlight overlay, search UI, composition root.
**Môi trường:** FTS5 dùng SQLite bundled. vec0 test **cần `vec0.dll`** trong bin test (Windows x64); plan ghi bước copy native lib; thiếu lib → test vec0 skip có điều kiện (không vỡ suite).

## 8. Ngoài phạm vi (sau)
- Hybrid retrieval (trộn FTS5 + vector cho RAG).
- OCR cho PDF scan ảnh.
- Dọn index cũ khi file đổi hash (giữ bản cũ ở SP2).
- Đa tài liệu / so sánh nhiều file cùng lúc.
- Chọn embedding model trong Settings.
