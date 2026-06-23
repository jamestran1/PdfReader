# Backlog — Ultimate PDF Reader & Editor

Cập nhật: 2026-06-22. Quy ước trạng thái: ✅ done · 🔄 in progress · ⬜ backlog. Ưu tiên: P0 cao → P3 thấp.

## 🔄 Đang làm
- (trống)

## Epic: Taking Note → Research Workspace (NotebookLM)
Vision: `docs/superpowers/specs/2026-06-22-taking-note-epic.md`.
- ✅ **Layer 1** — note theo sách (store + NotesViewModel + tab Notes) — PR #14.
- ⬜ **Layer 2** — bắt note từ vùng chọn text + highlight tô màu + lưu câu trả lời AI thành note.
- ⬜ **Layer 3** — Workspace (gom nhiều tài liệu; chuyển phạm vi note/chat document→workspace).
- ⬜ **Layer 4** — AI đa tài liệu (RAG/chat xuyên source, trích dẫn source+trang).
- ⬜ **Layer 5** — tổ chức/tìm/xuất/chia sẻ workspace.

## ⬜ Backlog (theo ưu tiên, từ audit 2026-06-22)
| P | Tính năng | Trạng thái hiện tại | Ghi chú |
|---|---|---|---|
| P0 | Lưu / Export PDF | THIẾU | Không có Save/Export/Print, không Ctrl+S |
| P0 | Sửa text ghi vào PDF | STUB | UI sửa chạy nhưng `EditTextCommand.Execute/Undo` chỉ Debug.WriteLine, không đổi PDF |
| P1 | Undo/Redo | STUB | Có `_undoStack` nhưng không UI/Ctrl+Z, command rỗng, chưa có redo |
| P1 | Nút Read/Edit ở rail trái | DEAD UI | Chưa bind command (chỉ Settings có) |
| P2 | Annotation/markup (highlight thủ công, ghi chú, hình, bút) | THIẾU | Mới chỉ có highlight tìm kiếm (tạm) |
| P2 | Thumbnail / Outline / Bookmark panel | THIẾU | Chưa có panel trang/mục lục |
| P2 | Tỉa độ dài lịch sử LLM khi hội thoại quá dài | THIẾU | Ghi nhận khi làm PR #12: lịch sử chat nạp lại vào `SeedHistory` có thể phình, chạm giới hạn token. Cần tỉa (giữ N lượt gần nhất hoặc tóm tắt). |
| P3 | Search: prev/next match + đếm "1/15" | DỞ | Chỉ popup + click, chưa nhảy match kế |

## ✅ Đã xong (gần đây)
- ✅ Taking Note Layer 1: note theo sách (SQLite + filter/sort + tab Notes, neo trang) — PR #14.
- ✅ Thư viện tài liệu: import → copy vào app + thumbnail + lưới thẻ, mở lại — PR #11.
- ✅ Lịch sử chat theo từng sách (lưu SQLite, AI nhớ tiếp mạch) — PR #12.
- ✅ Ẩn panel chat khi ở thư viện + kéo chỉnh bề rộng (GridSplitter) — PR #13.
- ✅ AI chat + RAG (OpenAI streaming + embeddings + index SQLite) — PR #8/SP2.
- ✅ Tìm kiếm: index, snippet có dấu + in đậm, click→nhảy + highlight đúng từ khóa (iText) — PR #9.
- ✅ 4 chế độ view (Single/Continuous/Facing/Continuous Facing) + tách bìa — PR #10.
- ✅ Canh giữa trang (fix DPI), render nét khi zoom, zoom-fit khi mở — PR #10.
- ✅ Phím tắt: zoom, lật trang, đầu/cuối, fit (Ctrl+0), mở (Ctrl+O), find (Ctrl+F), go-to-page (Ctrl+G) — PR #10.

## Quy ước
- Mỗi feature: brainstorm → spec (`docs/superpowers/specs/`) → plan (`docs/superpowers/plans/`) → implement (subagent-driven) → PR.
- Cập nhật file này khi bắt đầu/hoàn thành một mục.
