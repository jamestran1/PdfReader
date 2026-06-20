# CLAUDE.md

Hướng dẫn cho Claude Code khi làm việc trong repo này. (Migrated từ ngữ cảnh Gemini agent → Claude, 2026-06-18.)

## Dự án

**Ultimate PDF Reader & Editor** — ứng dụng đọc/chỉnh sửa/chú thích PDF cho Windows Desktop, tích hợp AI dạng NotebookLM (chat + RAG trên nội dung tài liệu). Giao diện theo Material Design 3.

- **Framework:** WPF, .NET `net10.0-windows`, mô hình MVVM.
- **Ngôn ngữ giao tiếp / UI:** tiếng Việt (chuỗi UI, message AI mặc định bằng tiếng Việt).

## Build & Test

```powershell
dotnet build PdfReaderApp.slnx
dotnet test                          # xUnit, project tests/PdfReaderApp.Tests
dotnet run --project src/PdfReaderApp # chạy app WPF
```

## Kiến trúc (Hybrid Engine)

Hai lớp render/logic chạy song song:

- **Render (Graphics):** SkiaSharp + `VirtualCanvas` — chỉ vẽ phần trong viewport, cho phép cuộn ảo (virtualization) qua nhiều trang và infinite zoom, vượt giới hạn 32k pixel của WPF.
- **PDF Core (Logic):** PDFium (`PdfiumViewer.Net.WPF`) — nạp/render trang, và **Ghost Mapping**: ánh xạ object PDF (text/image/path) thành đối tượng tương tác để click/kéo/sửa.

## Bản đồ mã nguồn

| Đường dẫn | Vai trò |
|---|---|
| `src/PdfReaderApp/ViewModels/MainViewModel.cs` | State + commands (Open, Next/PrevPage, ZoomIn/Out, SendMessage chat). `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`). |
| `src/PdfReaderApp/Controls/PdfViewerControl.xaml(.cs)` | UserControl render PDF qua Skia; expose Dependency Properties (`CurrentPage`, `Zoom`, `DocumentSource`); `IDisposable`. |
| `src/PdfReaderApp/Controls/MicroEditor.xaml(.cs)` | In-place text editor (sửa text trực tiếp trên trang). |
| `src/PdfReaderApp/Core/RenderEngine.cs` | Render engine (`IDisposable`) — cầu nối PDFium ↔ Skia. |
| `src/PdfReaderApp/Core/PdfObjectManager.cs` | Ghost Mapping (`GhostText`...) cho tương tác object. |
| `src/PdfReaderApp/Core/Commands/EditTextCommand.cs` | `IUndoCommand` — pattern Command cho Undo/Redo. |
| `src/PdfReaderApp/Services/AIService.cs` | Lớp gọi LLM. **Hiện là placeholder** (Task.Delay giả lập, chưa gọi API thật). |
| `src/PdfReaderApp/Services/PdfStructureAnalyzer.cs` | Trích text, chia `TextChunk` cho RAG. |
| `Inspector_*.cs` (root) + `Inspector_Core/` | Tiện ích chẩn đoán/inspect PDFium/Skia (text, flags, rotation, render params). |

## Dependencies chính

`CommunityToolkit.Mvvm`, `MaterialDesignThemes` + `MaterialDesignColors`, `PdfiumViewer.Net.WPF`, `SkiaSharp.Views.WPF`, `System.Drawing.Common`.

## Tài liệu thiết kế

Specs ở `docs/superpowers/specs/`, plans ở `docs/superpowers/plans/`. Các file `2026-06-04-*` là design gốc (di chuyển từ thư mục `docs/plans/` cũ do Gemini tạo). Khi thêm tính năng mới, dùng skill `superpowers:brainstorming` → spec → `superpowers:writing-plans`.

## Quy ước & lưu ý quan trọng

- **KHÔNG add/commit thư mục `conductor/`** vào git (đã có trong `.gitignore`). Đây là thư mục công cụ cục bộ. (Quy tắc kế thừa từ memory của Gemini agent.)
- File `obj/` và `bin/` không commit — chỉ sửa source thật, bỏ qua `*_wpftmp.*` (file tạm WPF trong `obj/`).
- AI/LLM: nếu nối API thật, **không** hardcode API key trong source; dùng biến môi trường / user-secrets.
- **Model selection:** khi brainstorming và planning dùng **Opus 4.8**; khi code (implement) dùng **Sonnet 4.6**.

## Trạng thái tính năng (tính đến 2026-06-18)

- ✅ Render Skia ảo hóa, centering, horizontal scroll, zoom-to-cursor; chat sidebar (UI + AIService placeholder); Ghost mapping.
- ⚠️ **Search:** mới có ô TextBox trên toolbar (`MainWindow.xaml`), **chưa có logic** tìm/highlight.
- ⚠️ **Navigation:** Next/Prev + binding `CurrentPage` hai chiều hoạt động; cần refine đồng bộ scroll ↔ số trang; chưa có Thumbnail.
- ⚠️ **AIService:** placeholder, chưa gọi LLM thật.
