# EPIC — Taking Note → AI Research Workspace (NotebookLM-like)

**Ngày:** 2026-06-22
**Loại:** Tài liệu tầm nhìn (north star) + lộ trình. KHÔNG phải spec triển khai. Mỗi sub-project có spec + plan riêng.

## North Star

Đưa sản phẩm từ "PDF reader + AI theo từng quyển sách" lên **không gian nghiên cứu đa tài liệu** giống NotebookLM: người dùng gom nhiều tài liệu vào một ngữ cảnh, ghi chú xuyên tài liệu, hỏi AI trả lời trên toàn bộ nguồn (kèm trích dẫn source + trang), tổng hợp và chia sẻ.

## Khái niệm trung tâm: Workspace

**Workspace** (Notebook/Dự án) = một ngữ cảnh nghiên cứu có tên, gom nhiều **sources** (tài liệu) và chứa **note + chat + tổng hợp AI** ở phạm vi cả nhóm.

Thực thể và hướng tiến hóa:

| Thực thể | Hôm nay | Sau (North Star) |
|---|---|---|
| Document (source) | Đã có (library) | Là thành viên của workspace |
| Workspace | Chưa có | Gom N documents; là phạm vi mặc định của note/chat |
| Note | (đang xây Layer 1) thuộc document | Thuộc workspace; tùy chọn neo 1 document + trang + vùng |
| Chat thread | Per-book (PR #12) | Per-workspace; per-book = workspace 1-source |
| Index/RAG | Per-document | Trải mọi source trong workspace; trích dẫn source + trang |

## Lộ trình bắc cầu

- **Layer 0** (đã/đang): Library (PR #11), per-book chat + RAG (PR #8), ẩn/resize panel (PR #13).
- **Layer 1 — Nền tảng Notes (theo sách):** Notes store SQLite (`notes.db`, khóa `documentId`) + panel bên phải (liệt kê/tạo/sửa/xóa, note có anchor thì click nhảy tới trang). Note model thiết kế "sẵn sàng workspace". *(Sub-project được brainstorm chi tiết ngay sau epic này.)*
- **Layer 2 — Bắt ngữ cảnh (theo sách):** chọn text trên trang → note kèm trích dẫn neo trang; highlight tô màu (overlay Skia) lưu như annotation; "Lưu câu trả lời AI thành note".
- **Layer 3 — Workspace (bản lề):** thêm thực thể Workspace; gom nhiều documents; chuyển phạm vi note & chat từ document → workspace (workspace 1-source = hành vi hôm nay). Library bổ sung lớp gom theo workspace.
- **Layer 4 — AI đa tài liệu:** RAG/chat trải mọi source trong workspace, trích dẫn source + trang; tìm kiếm đa tài liệu.
- **Layer 5 — Tổng hợp & chia sẻ:** so sánh/tóm tắt xuyên tài liệu thành note; xuất/chia sẻ bundle workspace (sources + ngữ cảnh).

**Thứ tự & lý do:** giao giá trị note theo-sách trước (nhanh, độc lập), rồi đưa lớp Workspace vào khi note/chat đã tồn tại → migration là "thêm lớp gom + đổi phạm vi khóa", không phải viết lại.

## Nguyên tắc kiến trúc giữ đường tới North Star (làm sớm, chi phí thấp)

1. **Context scope, không hardcode documentId:** tầng service của note/chat bọc qua khái niệm "phạm vi ngữ cảnh" (một tập documentId). Hôm nay tập đó = 1 document; sau này = các source của workspace. Đổi sang workspace chỉ sửa cục bộ.
2. **RAG index giữ per-document:** index của workspace = hợp các index per-doc + fan-out truy vấn. Không rework.
3. **Note model linh hoạt từ ngày đầu:** bắt buộc (id, ownerKey, nội dung, thời gian); tùy chọn nullable (documentId, pageIndex, quote, rect, màu, tag). Một bảng phục vụ cả note tự do, note theo trang, annotation.
4. **Pattern store nhất quán:** SQLite per-feature DB, connection per-operation `Pooling=False` (như `library.db`/`chats.db`).
5. **Pattern panel nhất quán:** panel note tái dùng cơ chế ẩn-theo-thư-viện + resize (GridSplitter) vừa làm cho panel chat.

## Không thuộc epic này (để tránh phình)

- Cộng tác thời gian thực / multi-user đồng bộ (chia sẻ ở Layer 5 trước hết là xuất/nhập bundle).
- OCR cho PDF scan.
- Đồng bộ đám mây.

## Trạng thái

- Đã chốt vision (2026-06-22). Bắt đầu Layer 1.
- Backlog mục liên quan: `docs/BACKLOG.md` (annotation/markup P2 sẽ được epic này hấp thụ ở Layer 2-3).
