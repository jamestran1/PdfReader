# CLAUDE.md

Hướng dẫn cho Claude Code khi làm việc trong repo này: bản đồ dự án, lệnh build và quy trình. Quy tắc code nằm trong `CODING_STANDARDS.md`, nạp bên dưới.

@CODING_STANDARDS.md

## Dự án

**Trí Thư**: ứng dụng đọc/chỉnh sửa/chú thích PDF cho Windows Desktop, tích hợp AI dạng NotebookLM (chat + RAG trên nội dung tài liệu). Giao diện theo Material Design 3.

- **Framework:** WPF, .NET `net10.0-windows`, mô hình MVVM.
- **Ngôn ngữ giao tiếp / UI:** tiếng Việt (chuỗi UI, message AI mặc định bằng tiếng Việt).
- **Mã hóa CLI trên Windows:** luôn đặt `[Console]::OutputEncoding = $OutputEncoding = [System.Text.Encoding]::UTF8` khi chạy script/CLI có text tiếng Việt. Khi tạo/sửa PR qua `gh`, luôn dùng file UTF-8 (`--body-file`), không pipe trực tiếp qua stdin để tránh biến chữ tiếng Việt thành `?`.

## Build & Test

```powershell
dotnet build PdfReaderApp.slnx
dotnet test                          # xUnit, project tests/PdfReaderApp.Tests
dotnet run --project src/PdfReaderApp # chạy app WPF
pwsh scripts/check.ps1               # lớp chặn: format, build, warning toàn repo, XAML cấm, test
Invoke-Pester scripts/tests          # test của script kiểm tra và hook (Pester 5+)
pwsh scripts/snapshot.ps1 -Out x.png # app tự chụp cửa sổ chính (dữ liệu trống, không chụp màn hình)
pwsh scripts/pr-assets.ps1 -Pr N -Files x.png # đưa ảnh lên nhánh pr-assets, in link để nhúng vào PR
```

Biến môi trường dev: `TRITHU_DATA_DIR` (thư mục dữ liệu thay cho `%APPDATA%\PdfReaderApp`), `TRITHU_SCREENSHOT` / `TRITHU_SCREENSHOT_DELAY` (tự chụp rồi thoát).

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

Repo dùng bộ skill **Matt Pocock** (nạp global trên máy, xem `npx skills list -g`, không lưu thư mục skills vào repo). Nguyên tắc: **issue tracker (GitHub) là source of truth** cho PRD và task, KHÔNG lưu PRD/plan trùng lặp vào repo.

- **Artifact bền, commit vào repo:**
  - `CODING_STANDARDS.md`: quy tắc code cho agent viết code và AI review.
  - `GLOSSARY.md` (gốc repo): glossary / ngôn ngữ miền (do `domain-modeling` duy trì). Chỉ là từ điển, không phải spec.
  - `docs/adr/`: Architecture Decision Records (vd `0001-workspace-architecture`, `0002-per-feature-sqlite-databases`, `0003-multi-document-tabs`).
- **PRD & task:** sống trên GitHub issues (gắn nhãn triage `ready-for-agent`), không ở file local.
- **Lệnh `gh` để đọc ticket** (kể cả khi số là PR) **và cho map Wayfinder** (sub-issue, blocked_by, nhận ticket): `docs/agents/issue-tracker.md`. **Prototype UI** (skill `prototype` cho app WPF): `docs/agents/prototyping.md`.
- **Quy trình tính năng mới:** `grill-with-docs` → (việc nhiều session: `to-spec` publish PRD lên issue → `to-tickets` chẻ thành vertical-slice issues) → `/implement <issue>` (chạy `/tdd` tại seam đã thống nhất, rồi `/code-review`) → PR (`/pr`) → `/retro` (tổng kết session, đề xuất guardrail và chuẩn code). Hỏi `/ask-matt` khi không chắc bước tiếp theo.
- **Góp ý về code:** góp ý bền về code (C#, XAML, test, giao diện) thêm vào `CODING_STANDARDS.md` qua PR, không lưu vào memory của agent. Memory chỉ giữ sở thích cá nhân và quy trình.
- **Model selection:** brainstorm, planning và review dùng **Opus** (bản mới nhất, hiện Opus 5.5); code (implement) dùng **Sonnet** (bản mới nhất, hiện Sonnet 5.5), không dùng Haiku.
- **Legacy:** `docs/superpowers/specs|plans/` là của workflow `superpowers` cũ; các file `2026-06-04-*` là design gốc Gemini, giữ làm lịch sử, KHÔNG thêm file mới vào đây.
