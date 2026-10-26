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

**Phạm vi** (`DocumentScope`):
Một truy vấn của người dùng áp dụng cho **Tài liệu này** (Document đang mở) hoặc **Toàn Workspace** (mọi Document thuộc Workspace membership). Dùng chung cho Search (#106) và Chat (#107); mặc định Tài liệu này. Toàn Workspace chỉ dùng được những Document đã có Vector Index `Complete` cho Embedding Model đang chọn; Document còn thiếu được lập chỉ mục nền và bị loại khỏi câu trả lời hiện tại.
_Avoid_: Search scope / chat scope (một khái niệm, một từ), mode.

**Nguồn trích dẫn** (`RagSource`):
Một dòng của bảng nguồn do RAG trả về (cả phạm vi Tài liệu này lẫn Toàn Workspace): số tham chiếu `[n]` trong ngữ cảnh và câu trả lời của AI ↔ (Document, Title, trang, Đoạn nguồn). AI chỉ chép số `[n]`, không chép `documentId` (hash dài); #108 biến `[n]` thành link nhảy sang Document và trang đó, #27 tô Đoạn nguồn trên trang.
_Avoid_: Citation (dành cho phần hiển thị ở #108), reference.

**Đoạn nguồn** (`RagSource.Passage`, tô qua `SourceHighlight`):
Chữ của chunk mà AI dựa vào cho một trích dẫn `[n]`. Bấm trích dẫn thì đoạn này được tô màu cam trên trang (khác vàng của kết quả tìm), giữ tới khi mở trích dẫn khác. Nguồn lưu từ bản trước #27 không có Đoạn nguồn: vẫn nhảy trang, không tô.
_Avoid_: span, highlight (dành cho ghi chú người dùng tô).

## AI

**AI Provider**:
Nhà cung cấp mô hình AI: OpenAI, Gemini hoặc Claude (`AiProvider`). Mỗi provider một API key riêng. Claude chỉ dùng cho Chat (ADR 0008).
_Avoid_: Vendor, backend.

**Chat Model** / **Embedding Model**:
Tên model cụ thể người dùng chọn trong Settings cho Chat và cho lập chỉ mục RAG, mỗi cái đi kèm một AI Provider.
_Avoid_: Engine, LLM (khi nói về một model cụ thể).

**Vector Index**:
Tập vector của các chunk của một Document theo **một** Embedding Model, kèm trạng thái lập chỉ mục riêng. Một Document có thể có nhiều Vector Index; đổi Embedding Model không xoá index cũ (ADR 0008).
_Avoid_: Embedding table, index (trơn, dễ lẫn với chỉ mục FTS tìm kiếm).

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
Surface hai vùng cho Tài liệu của Workspace: (1) **thành viên** — thumbnail member doc, click → mở/kích hoạt Tab, kèm nút gỡ (**gỡ = xóa membership và đóng Tab nếu đang mở** — giữ bất biến Open Set ⊆ membership); (2) **thêm từ Library** — Add = thêm membership **và** mở Tab active. Header surface mang tên Workspace + đổi tên inline (bút chì). Cùng MỘT component ở **2 bối cảnh host**: modal nút "+" (DialogHost) và inline trong canvas khi Open Set rỗng (gộp empty-state lẫn lưới re-open). Chỉ ở named Workspace.
_Avoid_: Thumbnail Gallery, document picker, doc grid, empty state.

## Phát hành (Release)

**Release**:
Một phiên bản Trí Thư đánh dấu bằng git tag `vX.Y.Z` (SemVer, tag là nguồn sự thật duy nhất của version), đóng gói MSIX x64 self-contained và phân phối tới người dùng **chỉ qua Microsoft Store**. Không có kênh tải trực tiếp.
_Avoid_: Build, deploy, bản phát hành GitHub (GitHub Release chỉ là nơi lưu artifact + notes cho dev, không phải kênh tới user).

**Package Identity**:
Danh tính MSIX (Package Name + Publisher) do Partner Center cấp khi reserve tên app — thứ Windows/Store dùng để nhận diện app, độc lập với tên hiển thị "Trí Thư".
_Avoid_: App ID, tên app.

**Submission**:
Một lần nộp MSIX + listing lên Partner Center để Microsoft certification. Đạt thì Store tự ký package và phân phối; app không tự ký, không tự update (Store lo cả hai).
_Avoid_: Upload, publish (mơ hồ giữa nộp và đã lên Store).

**MSIX smoke test**:
Bước kiểm tra bắt buộc trước mỗi Submission: cài bản MSIX thật trên máy local (dev cert) và chạy checklist ngắn — vì app trong MSIX chạy khác app từ `bin/` (AppData ảo hóa, package identity), test suite không bắt được lớp lỗi này.
_Avoid_: QA, manual test (chung chung).

**Hydration / Eviction**:
**Hydrate** = nạp PDFium + render cho một Tab khi kích hoạt. **Evict** = giải phóng để giới hạn bộ nhớ. Chính sách: tab active render đầy đủ; mọi tab inactive bỏ bitmap cache; giữ handle PDFium cho active + ~5 tab MRU, quá thì dispose handle (còn header + view-state) và re-hydrate khi click. Khôi phục khi vào Workspace là **lười**: chỉ tab active nạp doc.
_Avoid_: Load/unload, cache.
