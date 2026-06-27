# PdfReader — Ngôn ngữ miền (Domain Language)

Glossary cho ứng dụng đọc/chú thích PDF + AI (hướng NotebookLM). Giữ thuật ngữ thống nhất giữa code, UI (tiếng Việt) và tài liệu thiết kế. Đây là từ điển, không phải spec.

## Tài liệu & Workspace

**Document**:
Một tệp PDF đã import vào Library, có `documentId` và đường dẫn lưu trữ. Đơn vị nội dung để đọc/chú thích.
_Avoid_: File, paper, book (UI có thể gọi "tài liệu").

**Workspace**:
Một ngữ cảnh nghiên cứu tham chiếu **nhiều Document** theo quan hệ many-to-many (xem ADR 0001). Notes thuộc về Workspace, không thuộc Document.
_Avoid_: Notebook, project, folder.

**Default Workspace**:
Workspace ẩn, tạm, chứa đúng một Document — giữ hành vi "đọc một tài liệu lẻ" (`IsWorkspaceSession=false`). KHÔNG Tab Strip, KHÔNG "+"; mở doc khác từ Library thì thay thế. Không liệt kê ở màn Workspaces; muốn đa tài liệu phải tạo Workspace có tên (promote — ADR 0004).
_Avoid_: Implicit workspace, single-doc mode.

**Workspace membership**:
Tập **tất cả** Document thuộc một Workspace (bảng `workspace_document`). Khác với Open Set: membership ≠ đang mở.
_Avoid_: Document list (mơ hồ với Open Set).

**Note**:
Ghi chú/highlight thuộc về Workspace (`owner_key = workspaceId`); `document_id` chỉ là anchor để neo lên trang. Panel notes gộp mọi Note của Workspace kèm chip nhãn doc, auto-focus doc active + toggle "chỉ tài liệu này"; highlight TRÊN TRANG luôn lọc theo `document_id` active.
_Avoid_: Annotation, comment.

**Chat thread**:
Một luồng hội thoại AI cho mỗi **Workspace**, RAG xuyên mọi Document thành viên; Document active là neo trích dẫn mặc định. Đổi Tab KHÔNG đổi luồng chat (ADR 0004). Lịch sử bền + RAG thật: #26.
_Avoid_: Chat per-tab, per-document chat.

## Tab đa tài liệu (multi-document tabs)

**Tab**:
Một Document đang mở để đọc trong phiên Workspace hiện tại, kèm view-state riêng (trang, zoom, scroll). Tab chỉ tồn tại trong Workspace có tên; đọc lẻ luôn một tab.
_Avoid_: Window, pane, view.

**Open Set**:
Tập Document hiện đang mở thành Tab trong một Workspace — một **tập con** của membership. Mở/đóng Tab thay đổi Open Set, không đụng membership.
_Avoid_: Open documents, active list.

**Per-tab view-state**:
Trạng thái hiển thị riêng của mỗi Tab: trang + zoom (chính xác) và scroll (best-effort, chuẩn hóa trong trang), cộng UI panel notes (scroll + note đang chọn + bộ lọc-doc) — giữ trong bộ nhớ. Chat KHÔNG thuộc per-tab (xem **Chat thread** — theo Workspace). Search cũng **không** thuộc view-state: transient, xóa khi đổi tab.
_Avoid_: Tab settings.

**Cross-doc jump**:
Hành vi mở từ một Note/highlight của Document khác trong Workspace. Quy tắc "activate-or-open": nếu đã mở thì kích hoạt Tab đó, nếu chưa thì mở Tab mới (lười); luôn điều hướng tới trang đích, ghi đè vị trí đã nhớ cho lần nhảy đó. (Trong #25 trước đây cross-doc jump = thay thế doc đang mở.)
_Avoid_: Navigate, link.

**Tab Strip**:
Dải tab ngang ở đầu vùng nội dung chính (giữa toolbar và viewer). Tràn thì cuộn ngang; kéo để sắp lại; nút "+" mở Workspace Documents surface. Chỉ hiện trong named Workspace (không có ở Default Workspace).
_Avoid_: Tab bar (dùng được nhưng thống nhất "Tab Strip").

**Workspace Documents surface**:
Surface hai vùng cho Tài liệu của Workspace: (1) **thành viên** — thumbnail member doc, click → mở/kích hoạt Tab, kèm nút gỡ; (2) **thêm từ Library** — add vào membership (Add = thêm membership **và** mở Tab active). Cùng component xuất hiện ở 3 nơi: modal nút "+", canvas empty-state khi đóng Tab cuối, và lưới re-open. Chỉ ở named Workspace.
_Avoid_: Thumbnail Gallery, document picker, doc grid, empty state.

**Hydration / Eviction**:
**Hydrate** = nạp PDFium + render cho một Tab khi kích hoạt. **Evict** = giải phóng để giới hạn bộ nhớ. Chính sách: tab active render đầy đủ; mọi tab inactive bỏ bitmap cache; giữ handle PDFium cho active + ~5 tab MRU, quá thì dispose handle (còn header + view-state) và re-hydrate khi click. Khôi phục khi vào Workspace là **lười**: chỉ tab active nạp doc.
_Avoid_: Load/unload, cache.
