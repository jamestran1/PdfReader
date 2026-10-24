# CLAUDE.md

Hướng dẫn cho Claude Code khi làm việc trong repo này: bản đồ dự án, lệnh build và quy trình. Quy tắc code nằm trong `CODING_STANDARDS.md`, nạp bên dưới.

@CODING_STANDARDS.md

## Dự án

**Trí Thư**: ứng dụng đọc/chỉnh sửa/chú thích PDF cho Windows Desktop, tích hợp AI dạng NotebookLM (chat + RAG trên nội dung tài liệu). Giao diện theo Material Design 3.

- **Framework:** WPF, .NET `net10.0-windows`, mô hình MVVM.
- **Ngôn ngữ giao tiếp / UI:** tiếng Việt (chuỗi UI, message AI mặc định bằng tiếng Việt).

## Build & Test

```powershell
dotnet build PdfReaderApp.slnx
dotnet test                          # xUnit, project tests/PdfReaderApp.Tests
dotnet run --project src/PdfReaderApp # chạy app WPF
pwsh scripts/check.ps1               # lớp chặn: format, build, warning trên dòng đổi, XAML cấm, test
Invoke-Pester scripts/tests          # test của script kiểm tra và hook (Pester 5+)
```

Hook Stop/SubagentStop (`.claude/hooks/stop-check.ps1`) chạy `scripts/check.ps1 -Fix` khi agent dừng và chặn dừng nếu còn lỗi. Tắt khi bị kẹt: `TRITHU_HOOKS=off`.

## Kiến trúc (Hybrid Engine)

Hai lớp render/logic chạy song song:

- **Render (Graphics):** SkiaSharp + `VirtualCanvas`: chỉ vẽ phần trong viewport, cho phép cuộn ảo (virtualization) qua nhiều trang và infinite zoom, vượt giới hạn 32k pixel của WPF.
- **PDF Core (Logic):** PDFium (`PdfiumViewer.Net.WPF`): nạp/render trang, và **Ghost Mapping**: ánh xạ object PDF (text/image/path) thành đối tượng tương tác để click/kéo/sửa.

## Bản đồ mã nguồn

| Đường dẫn | Vai trò |
|---|---|
| `src/PdfReaderApp/ViewModels/MainViewModel.cs` | State + commands (Open, Next/PrevPage, ZoomIn/Out, SendMessage chat). `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`). |
| `src/PdfReaderApp/Controls/PdfViewerControl.xaml(.cs)` | UserControl render PDF qua Skia; expose Dependency Properties (`CurrentPage`, `Zoom`, `DocumentSource`); `IDisposable`. |
| `src/PdfReaderApp/Controls/MicroEditor.xaml(.cs)` | In-place text editor (sửa text trực tiếp trên trang). |
| `src/PdfReaderApp/Core/RenderEngine.cs` | Render engine (`IDisposable`), cầu nối PDFium ↔ Skia. |
| `src/PdfReaderApp/Core/PdfObjectManager.cs` | Ghost Mapping (`GhostText`...) cho tương tác object. |
| `src/PdfReaderApp/Core/Commands/EditTextCommand.cs` | `IUndoCommand`, pattern Command cho Undo/Redo. |
| `src/PdfReaderApp/Services/AiChatService.cs` | Chat với LLM (RAG qua `RagContextService`, index trong `SqliteDocumentIndex`). |
| `src/PdfReaderApp/Services/PdfStructureAnalyzer.cs` | Trích text, chia `TextChunk` cho RAG. |
| `Inspector_*.cs` (root) + `Inspector_Core/` | Tiện ích chẩn đoán/inspect PDFium/Skia (text, flags, rotation, render params). |

## Dependencies chính

`CommunityToolkit.Mvvm`, `MaterialDesignThemes` + `MaterialDesignColors`, `PdfiumViewer.Net.WPF`, `SkiaSharp.Views.WPF`, `System.Drawing.Common`.

## Tài liệu thiết kế & quy trình (workflow Matt Pocock)

Repo dùng bộ skill **Matt Pocock** (`grilling`, `domain-modeling`, `to-spec`, `to-tickets`). Nguyên tắc: **issue tracker (GitHub) là source of truth** cho PRD và task, KHÔNG lưu PRD/plan trùng lặp vào repo.

- **Artifact bền, commit vào repo:**
  - `CODING_STANDARDS.md`: quy tắc code cho agent viết code và AI review.
  - `CONTEXT.md` (gốc repo): glossary / ngôn ngữ miền (do `domain-modeling` duy trì). Chỉ là từ điển, không phải spec.
  - `docs/adr/`: Architecture Decision Records (vd `0001-workspace-architecture`, `0002-per-feature-sqlite-databases`, `0003-multi-document-tabs`).
- **PRD & task:** sống trên GitHub issues (gắn nhãn triage `ready-for-agent`), không ở file local.
- **Quy trình tính năng mới:** `grilling` (+ `domain-modeling`) → `to-spec` (publish PRD lên issue) → `to-tickets` (chẻ thành vertical-slice issues) → `plan-to-issue <issue>` (viết plan TDD chi tiết lên body issue, idempotent qua marker `<!-- plan:start/end -->`, có cổng review+approve) → `/tdd` (implement task-by-task từ issue).
- **Góp ý về code:** góp ý bền về code (C#, XAML, test, giao diện) thêm vào `CODING_STANDARDS.md` qua PR, không lưu vào memory của agent. Memory chỉ giữ sở thích cá nhân và quy trình.
- **Model selection:** brainstorm, planning và review dùng **Opus** (bản mới nhất, hiện Opus 5.5); code (implement) dùng **Sonnet** (bản mới nhất, hiện Sonnet 5.5), không dùng Haiku.
- **Legacy:** `docs/superpowers/specs|plans/` là của workflow `superpowers` cũ; các file `2026-06-04-*` là design gốc Gemini, giữ làm lịch sử, KHÔNG thêm file mới vào đây.
