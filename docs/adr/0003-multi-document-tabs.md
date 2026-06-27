---
status: accepted
---

# Tab đa tài liệu: viewer-per-tab, phạm vi theo Workspace, khôi phục lười + LRU eviction

Để mở nhiều Document song song (#32, tách từ #25), ta thêm khái niệm **Tab** = một Document đang mở kèm view-state riêng. Quyết định nền:

- **Phạm vi theo Workspace, không global.** Open Set là tập con của membership trong **một** Workspace; đổi Workspace là đổi cả tập tab. Đọc lẻ (Default Workspace) luôn một tab — mở doc thứ hai từ Library thì **thay thế** (giữ hành vi cũ), muốn nhiều tab phải vào Workspace có tên. Nhất quán với việc notes/highlight đã scope theo Workspace (ADR 0001).
- **Một viewer cho mỗi Tab, chỉ tab active render.** `PdfViewerControl` (đã `IDisposable`) tạo theo từng tab. Tab inactive bỏ bitmap cache; giữ handle PDFium cho active + ~5 tab MRU, vượt thì dispose handle (còn header + view-state) rồi re-hydrate khi kích hoạt.
- **Khôi phục Open Set theo từng Workspace, nạp lười.** Persist (trong `workspaces.db`, theo ADR 0002) danh sách tab có thứ tự, tab active, và per-tab view-state (trang + zoom chính xác, scroll best-effort). Khi vào Workspace, dựng lại header mọi tab nhưng chỉ nạp doc của tab active; tab khác hydrate khi click. Lần đầu chưa có gì → một tab = `DefaultDocumentId`.

## Considered options

- **Tab global (một dải tab phẳng cho mọi doc):** đơn giản hơn nhưng trộn doc giữa các ngữ cảnh nghiên cứu và làm mờ ngữ nghĩa cross-doc-jump-trong-workspace; loại.
- **Một viewer swap nội dung:** bộ nhớ thấp nhất nhưng mỗi lần đổi tab phải reload PDFium (giật) và scroll restore kém; loại vì giá trị cốt lõi của tab là chuyển ngữ cảnh tức thì.
- **Viewer-per-tab giữ sống tất cả (không evict):** nhanh nhất nhưng bộ nhớ phình theo số tab + PDF lớn; loại, thay bằng LRU cap.

## Consequences

- **Cross-doc jump đổi nghĩa:** từ "thay thế doc đang mở" (trong #25) thành "activate-or-open Tab"; jump luôn điều hướng tới trang đích, ghi đè vị trí đã nhớ cho lần đó.
- **Đóng Tab ≠ gỡ khỏi Workspace.** Đóng chỉ rời Open Set; gỡ/xóa doc là luồng S4 riêng. Doc bị gỡ/xóa khi đang mở → tự đóng tab, fallback sang tab MRU hoặc Thumbnail Gallery. Đóng tab cuối → hiện Thumbnail Gallery (vẫn ở trong Workspace).
- **Chat per-tab nhưng chưa persist.** _(Thay thế bởi ADR 0004: chat đổi thành per-Workspace, RAG xuyên member docs.)_ Chat thành per-Document trong bộ nhớ (đổi theo tab, hợp ADR 0001); schema tab chừa chỗ cho chat-thread nhưng lịch sử chat bền + scroll restore để dành #26 (AIService hiện là placeholder).
- **Notes giữ nguyên model (ADR 0001).** "Per-tab note" chỉ là UI state của panel (scroll, note đang chọn, mặc định lọc theo doc của tab); không đổi quyền sở hữu note.
- **Hạn chế đã biết:** chưa có lưu chỉnh sửa PDF (không có dirty-tracking) → đóng hoặc evict một Tab sẽ **âm thầm bỏ chỉnh sửa trong phiên**. Chấp nhận tạm cho đến khi có tính năng save tài liệu.
