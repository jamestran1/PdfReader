# Backlog — Ultimate PDF Reader & Editor

Cập nhật: 2026-06-22. Quy ước trạng thái: ✅ done · 🔄 in progress · ⬜ backlog. Ưu tiên: P0 cao → P3 thấp.

## 🔄 Đang làm
- ⬜ **Thư viện tài liệu (Document Library)** — P0. Import file → lưu lại; lần sau mở lại từ giao diện thư viện. (Đang brainstorm.)

## ⬜ Backlog (theo ưu tiên, từ audit 2026-06-22)
| P | Tính năng | Trạng thái hiện tại | Ghi chú |
|---|---|---|---|
| P0 | Lưu / Export PDF | THIẾU | Không có Save/Export/Print, không Ctrl+S |
| P0 | Sửa text ghi vào PDF | STUB | UI sửa chạy nhưng `EditTextCommand.Execute/Undo` chỉ Debug.WriteLine, không đổi PDF |
| P1 | Undo/Redo | STUB | Có `_undoStack` nhưng không UI/Ctrl+Z, command rỗng, chưa có redo |
| P1 | Nút Read/Edit ở rail trái | DEAD UI | Chưa bind command (chỉ Settings có) |
| P2 | Annotation/markup (highlight thủ công, ghi chú, hình, bút) | THIẾU | Mới chỉ có highlight tìm kiếm (tạm) |
| P2 | Thumbnail / Outline / Bookmark panel | THIẾU | Chưa có panel trang/mục lục |
| P3 | Search: prev/next match + đếm "1/15" | DỞ | Chỉ popup + click, chưa nhảy match kế |

## ✅ Đã xong (gần đây)
- ✅ AI chat + RAG (OpenAI streaming + embeddings + index SQLite) — PR #8/SP2.
- ✅ Tìm kiếm: index, snippet có dấu + in đậm, click→nhảy + highlight đúng từ khóa (iText) — PR #9.
- ✅ 4 chế độ view (Single/Continuous/Facing/Continuous Facing) + tách bìa — PR #10.
- ✅ Canh giữa trang (fix DPI), render nét khi zoom, zoom-fit khi mở — PR #10.
- ✅ Phím tắt: zoom, lật trang, đầu/cuối, fit (Ctrl+0), mở (Ctrl+O), find (Ctrl+F), go-to-page (Ctrl+G) — PR #10.

## Quy ước
- Mỗi feature: brainstorm → spec (`docs/superpowers/specs/`) → plan (`docs/superpowers/plans/`) → implement (subagent-driven) → PR.
- Cập nhật file này khi bắt đầu/hoàn thành một mục.
