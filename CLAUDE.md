# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Dự án

**Trí Thư** — ứng dụng đọc/chỉnh sửa/chú thích PDF đa nền tảng (Windows + macOS), tích hợp AI dạng NotebookLM (chat + RAG trên nội dung tài liệu). Giao diện theo Material Design 3.

- **Framework:** .NET 10, MVVM. Hai UI head: WPF (Windows) và .NET MAUI (Windows + macOS).
- **Ngôn ngữ giao tiếp / UI:** tiếng Việt (chuỗi UI, message AI mặc định bằng tiếng Việt).

## Build & Test

```powershell
# Core library (cross-platform, builds on any OS)
dotnet build src/TriThu.Core/TriThu.Core.csproj

# WPF app (Windows only, or cross-compile with EnableWindowsTargeting)
dotnet build src/PdfReaderApp/PdfReaderApp.csproj

# MAUI app (requires maui-desktop workload: sudo dotnet workload install maui-desktop)
dotnet build src/TriThu.Maui/TriThu.Maui.csproj -f net10.0-maccatalyst   # macOS
dotnet build src/TriThu.Maui/TriThu.Maui.csproj -f net10.0-windows10.0.19041.0  # Windows

# Run MAUI app
dotnet run --project src/TriThu.Maui -f net10.0-maccatalyst
open "src/TriThu.Maui/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/Trí Thư.app"  # or launch directly

# Tests (xUnit, targets net10.0-windows — runs on Windows or cross-compile)
dotnet test
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"  # single test

# WPF app (Windows only)
dotnet run --project src/PdfReaderApp
```

## Solution Structure

```
PdfReaderApp.slnx
├── src/TriThu.Core/         — net10.0 class library (cross-platform, 72 files)
│   ├── Models/              — Domain models (AppTheme, LibraryItem, Note, Workspace, OpenTab, ...)
│   ├── Services/            — Business logic (SQLite stores, AI/LLM, iText, search/RAG, Docnet PDF render)
│   ├── ViewModels/          — MainViewModel, TabSetViewModel, NotesViewModel, ...
│   ├── Core/                — Pure logic (PageLayoutCalculator, TextSelectionResolver, PdfObjectManager, ...)
│   └── Platform/            — Abstraction interfaces (IUiDispatcher, IFilePickerService, ISettingsDialogService)
│
├── src/PdfReaderApp/        — net10.0-windows WPF app (20 files, Windows-only UI shell)
│   ├── Controls/            — PdfViewerControl (SkiaSharp + SKElement), MicroEditor
│   ├── ViewModels/          — WpfMainViewModel (thin subclass, parameterless constructor)
│   ├── Platform/            — WPF implementations (WpfDispatcher, WpfFilePickerService, ...)
│   ├── Services/            — WindowsSettingsService (DPAPI), MaterialDesignThemeService
│   ├── Themes/              — M3 token ResourceDictionaries (WPF format)
│   └── Converters/          — WPF IValueConverters
│
├── src/TriThu.Maui/         — net10.0-maccatalyst;net10.0-windows MAUI app (22 files)
│   ├── Controls/            — PdfViewerControl (SkiaSharp + SKCanvasView)
│   ├── Pages/               — MainPage, SettingsPage
│   ├── Platform/            — MAUI implementations (MauiDispatcher, MauiSettingsService, ...)
│   ├── Converters/          — MAUI IValueConverters
│   └── Resources/Styles/    — M3 token ResourceDictionaries (MAUI format, light + dark)
│
├── tests/PdfReaderApp.Tests/ — xUnit tests (net10.0-windows)
└── native/                   — vec0.dll (Windows) + vec0.dylib (macOS) for sqlite-vec
```

## Kiến trúc

### Hybrid Engine (PDF Rendering)

- **PDF Core:** Docnet.Core (cross-platform PDFium wrapper) via `IPdfRenderService` → `DocnetPdfRenderService`. Renders pages to BGRA byte arrays → `SKBitmap`. Also provides character-level text access for ghost mapping.
- **Text Extraction:** iText 8 (`ITextPdfDocumentService`) — keyword search rects, metadata, structure analysis.
- **Render (Graphics):** SkiaSharp virtual canvas — viewport-culled rendering, infinite zoom, page caching. WPF uses `SKElement`; MAUI uses `SKCanvasView`.

### Platform Abstraction Pattern

Core defines interfaces; each UI project provides implementations:

| Interface (Core) | WPF Implementation | MAUI Implementation |
|---|---|---|
| `IUiDispatcher` | `WpfDispatcher` (DispatcherTimer) | `MauiDispatcher` (MainThread) |
| `IFilePickerService` | `WpfFilePickerService` (OpenFileDialog) | `MauiFilePickerService` (FilePicker) |
| `ISettingsDialogService` | `WpfSettingsDialogService` (SettingsWindow) | `MauiSettingsDialogService` (DisplayPromptAsync) |
| `ISettingsService` | `WindowsSettingsService` (DPAPI) | `MauiSettingsService` (SecureStorage) |
| `IThemeService` | `MaterialDesignThemeService` | `MauiThemeService` (UserAppTheme) |

### MainViewModel Split

`MainViewModel` lives in Core with a full-parameter constructor (all dependencies injected). Each platform provides a thin subclass or DI registration:
- **WPF:** `WpfMainViewModel` — parameterless constructor creates all concrete services.
- **MAUI:** `MauiProgram.cs` — registers via `Microsoft.Extensions.DependencyInjection`.

### Data Storage — Per-Feature SQLite (ADR 0002)

Each feature uses a separate SQLite file in `%LOCALAPPDATA%/PdfReaderApp/` (Windows) or `FileSystem.AppDataDirectory` (MAUI):

| Database | Store class | Vai trò |
|---|---|---|
| `library.db` | `SqliteLibraryStore` | Catalog tài liệu đã import |
| `chats.db` | `SqliteChatHistoryStore` | Lịch sử chat AI |
| `index.db` | `SqliteDocumentIndex` | FTS5 + vec0 cho search/RAG |
| `notes.db` | `SqliteNoteStore` | Ghi chú & highlight |
| `workspaces.db` | `SqliteWorkspaceStore` | Workspace + membership |

**Không có FK xuyên-db.** Toàn vẹn liên-store do code phối hợp.

### AI / Chat

`AiChatService` dùng `Microsoft.Extensions.AI` abstraction (`IChatClient`) với backend OpenAI. RAG pipeline: `PdfStructureAnalyzer` → `TextChunker` → `DocumentIndexingService` → `RagContextService` → `AiChatService`.

### Native Dependencies

- **`native/vec0.dll`** (Windows) + **`native/vec0.dylib`** (macOS ARM64) — sqlite-vec extension for vector search (KNN) in RAG. Graceful degrade to FTS5 if missing.
- **`pdfium.dylib`** — bundled by Docnet.Core for `osx-arm64`. For Mac Catalyst, manually referenced via `NativeReference` in MAUI `.csproj` (Docnet doesn't auto-copy for `maccatalyst-arm64` RID).

## MAUI-Specific Notes

- `.csproj` uses `Sdk="Microsoft.NET.Sdk"` with `<UseMaui>true</UseMaui>` (not `Microsoft.NET.Sdk.Maui` — the latter has SDK resolution issues with some workload installations).
- `AppTheme` collision: MAUI has `Microsoft.Maui.ApplicationModel.AppTheme`. Files that use our `PdfReaderApp.Models.AppTheme` need `using AppTheme = PdfReaderApp.Models.AppTheme;`.
- `IPdfRenderService` collision: MAUI Graphics has `Microsoft.Maui.Graphics.IPdfRenderService`. Files need `using IPdfRenderService = PdfReaderApp.Services.IPdfRenderService;`.
- `ScrollBarVisibility.Auto` doesn't exist in MAUI — use `Default`.
- Compiled bindings (`x:DataType`) + `CommandParameter` with `{x:Reference}` can silently pass null. Use code-behind for Entry-to-command parameter wiring.

## Tài liệu thiết kế & quy trình

Từ 2026-06-25 repo dùng bộ skill **Matt Pocock** (`grilling`, `domain-modeling`, `to-prd`, `to-issues`). **GitHub issues là source of truth** cho PRD và task.

- **Artifact bền:** `CONTEXT.md` (glossary), `docs/adr/` (Architecture Decision Records).
- **Quy trình:** `grilling` → `domain-modeling` → `to-prd` (issue) → `to-issues` (vertical-slice) → `plan-to-issue` → `/tdd`.
- **Legacy:** `docs/superpowers/` — workflow cũ, giữ làm lịch sử.

## Quy ước

- **KHÔNG add/commit thư mục `conductor/`** (trong `.gitignore`).
- Bỏ qua `obj/`, `bin/`, `*_wpftmp.*`.
- AI/LLM: **không** hardcode API key; dùng biến môi trường / user-secrets / SecureStorage.
- **Model selection:** brainstorming & planning → **Opus 4.8**; code (implement) → **Sonnet 4.6**.
