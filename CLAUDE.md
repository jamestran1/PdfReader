# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Dự án

**Trí Thư** — ứng dụng đọc/chỉnh sửa/chú thích PDF cho Windows Desktop, tích hợp AI dạng NotebookLM (chat + RAG trên nội dung tài liệu). Giao diện theo Material Design 3.

- **Framework:** WPF, .NET `net10.0-windows`, mô hình MVVM.
- **Ngôn ngữ giao tiếp / UI:** tiếng Việt (chuỗi UI, message AI mặc định bằng tiếng Việt).

## Build & Test

```powershell
dotnet build PdfReaderApp.slnx            # build toàn bộ solution
dotnet test                                # xUnit, project tests/PdfReaderApp.Tests
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"  # chạy 1 test
dotnet run --project src/PdfReaderApp      # chạy app WPF
```

Solution gồm 2 project: `src/PdfReaderApp` (app chính) và `tests/PdfReaderApp.Tests` (xUnit). Test project dùng `InternalsVisibleTo` để truy cập internal members.

**Native dependency:** `native/vec0.dll` (sqlite-vec extension) được copy vào output directory. Cần cho vector search (KNN) trong RAG; nếu thiếu, FTS5 text search vẫn hoạt động (graceful degrade).

## Kiến trúc (Hybrid Engine)

Hai lớp render/logic chạy song song:

- **Render (Graphics):** SkiaSharp + `VirtualCanvas` (trong `PdfViewerControl`) — chỉ vẽ phần trong viewport, cho phép cuộn ảo (virtualization) qua nhiều trang và infinite zoom, vượt giới hạn 32k pixel của WPF.
- **PDF Core (Logic):** PDFium (`PdfiumViewer.Net.WPF`) cho render trang + **iText** cho text extraction/search. `RenderEngine` cầu nối PDFium → GDI+ → SKBitmap. **Ghost Mapping** (`PdfObjectManager`): ánh xạ object PDF thành đối tượng tương tác.

### Dependency Injection — Poor Man's DI

Không dùng DI container. `MainViewModel` có hai constructor:
- **Parameterless** (production): tạo tất cả service thật (`ITextPdfDocumentService`, `WindowsSettingsService`, `OpenAiChatClientFactory`, `SqliteDocumentIndex`, …).
- **Full-parameter** (test): inject mọi interface (`IPdfDocumentService`, `ISettingsService`, `IChatClientFactory`, `IDocumentIndex`, …). Các store nullable (`INoteStore?`, `IWorkspaceStore?`) có fallback tạo SQLite thật nếu null.

### Data Storage — Per-Feature SQLite (ADR 0002)

Mỗi feature một file SQLite riêng trong `%LOCALAPPDATA%/Trí Thư/`:

| Database | Store class | Vai trò |
|---|---|---|
| `library.db` | `SqliteLibraryStore` | Catalog tài liệu đã import |
| `chats.db` | `SqliteChatHistoryStore` | Lịch sử chat AI |
| `index.db` | `SqliteDocumentIndex` | FTS5 + vec0 cho search/RAG |
| `notes.db` | `SqliteNoteStore` | Ghi chú & highlight |
| `workspaces.db` | `SqliteWorkspaceStore` | Workspace + membership |

**Không có FK xuyên-db.** Toàn vẹn liên-store do code phối hợp (ViewModel/service), không bằng FK. Mỗi store có `EnsureSchema()` + `user_version` migration riêng.

### Tab & Workspace Model

- `TabSetViewModel` — quản lý Open Set (tập Tab đang mở), thuần logic, không phụ thuộc PDFium.
- **Default Workspace** (`IsWorkspaceSession=false`): đọc một doc lẻ, không Tab Strip. **Named Workspace**: đa tài liệu + Tab Strip + chat per-workspace.
- View-state (trang, zoom) sống trên `OpenTab`. `MainViewModel.CurrentPage/ZoomLevel/TotalPages` proxy sang `ActiveViewTab` khi ở Workspace, hoặc dùng backing field khi đọc lẻ.

### AI / Chat

`AiChatService` dùng `Microsoft.Extensions.AI` abstraction (`IChatClient`) với backend OpenAI. API key lấy từ `ISettingsService` (registry). RAG pipeline: `PdfStructureAnalyzer` → `TextChunker` → `DocumentIndexingService` (embed + index) → `RagContextService` (retrieve context) → `AiChatService` (prompt LLM).

### Theme & Design System

M3 token system trong `Themes/TriThuTokens.xaml` (light) + `TriThuTokens.Dark.xaml`. Component styles tách file: `NavRail.xaml`, `TitleBar.xaml`, `DocTabStrip.xaml`, `LibraryCard.xaml`, `RightPanel.xaml`, `ReaderTopBar.xaml`, `WorkspaceCard.xaml`. Theme toggle: `IThemeService` → `MaterialDesignThemeService` áp dụng palette, `ISettingsService` lưu preference.

## Tài liệu thiết kế & quy trình

Từ 2026-06-25 repo dùng bộ skill **Matt Pocock** (`grilling`, `domain-modeling`, `to-prd`, `to-issues`). **GitHub issues là source of truth** cho PRD và task — KHÔNG lưu PRD/plan trùng lặp vào repo.

- **Artifact bền, commit vào repo:**
  - `CONTEXT.md` (gốc repo) — glossary / ngôn ngữ miền. Chỉ là từ điển, không phải spec.
  - `docs/adr/` — Architecture Decision Records.
- **Quy trình tính năng mới:** `grilling` → `domain-modeling` → `to-prd` (issue) → `to-issues` (vertical-slice issues) → `plan-to-issue <issue>` → `/tdd` (implement).
- **Legacy:** `docs/superpowers/specs|plans/` là của workflow cũ; giữ làm lịch sử, KHÔNG thêm file mới vào đây.

## Quy ước

- **KHÔNG add/commit thư mục `conductor/`** vào git (đã có trong `.gitignore`).
- Bỏ qua `obj/`, `bin/`, `*_wpftmp.*`.
- AI/LLM: **không** hardcode API key; dùng biến môi trường / user-secrets.
- **Model selection:** brainstorming & planning → **Opus 4.8**; code (implement) → **Sonnet 4.6**.
