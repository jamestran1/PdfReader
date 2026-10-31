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
Workspace ẩn, riêng cho từng Document, chứa đúng một Document: giữ hành vi "đọc một tài liệu lẻ" (`IsWorkspaceSession=false`). Document hiển thị qua viewer đọc lẻ, chỉ một Tab Document, không đóng được, KHÔNG "+"; mở doc khác từ Library là chuyển sang Default Workspace của doc đó. Vẫn mở thêm được Tab Bản soạn, và chúng được nhớ lại (ADR 0009). Không liệt kê ở màn Workspaces; muốn đa tài liệu phải tạo Workspace có tên (promote, ADR 0004). Promote không lấy gì đi: Note và Bản soạn ở lại Default Workspace.
_Avoid_: Implicit workspace, single-doc mode.

**Workspace membership**:
Tập **tất cả** Document thuộc một Workspace (bảng `workspace_document`). Khác với Open Set: membership ≠ đang mở.
_Avoid_: Document list (mơ hồ với Open Set).

**Note**:
Ghi chú/highlight thuộc về Workspace (`owner_key = workspaceId`); `document_id` chỉ là anchor để neo lên trang. Panel notes gộp mọi Note của Workspace kèm chip nhãn doc, auto-focus doc active + toggle "chỉ tài liệu này"; highlight TRÊN TRANG luôn lọc theo `document_id` active.
_Avoid_: Annotation, comment.

**Bản soạn** (`Composition`):
Văn bản AI sinh từ một tập Note do người dùng chọn, thuộc về Workspace giống Note nhưng là thực thể riêng (không hiện trong panel notes). Có ba kiểu: **Tóm tắt**, **Outline**, **Soạn thảo**. Mỗi lần sinh tạo một Bản soạn mới, là ảnh chụp tại thời điểm sinh; người dùng sửa tay và đổi tiêu đề được. Nhớ các Note nguồn, yêu cầu người dùng gõ và Chat Model đã dùng (#28).
_Avoid_: Draft (trùng nghĩa với kiểu Soạn thảo), sản phẩm AI, kết quả AI, Studio output.

**Chat thread**:
Một luồng hội thoại AI cho mỗi **Workspace**, RAG xuyên mọi Document thành viên; Document active là neo trích dẫn mặc định. Đổi Tab KHÔNG đổi luồng chat (ADR 0004). Lịch sử bền + RAG thật: #26.
_Avoid_: Chat per-tab, per-document chat.

**Phạm vi** (`DocumentScope`):
Một truy vấn của người dùng áp dụng cho **Tài liệu này** (Document đang mở) hoặc **Toàn Workspace** (mọi Document thuộc Workspace membership). Dùng chung cho Search (#106) và Chat (#107); mặc định Tài liệu này. Toàn Workspace chỉ dùng được những Document đã có Vector Index `Complete` cho Embedding Model đang chọn; Document còn thiếu được lập chỉ mục nền và bị loại khỏi câu trả lời hiện tại.
_Avoid_: Search scope / chat scope (một khái niệm, một từ), mode.

**Nguồn trích dẫn** (`RagSource`):
Một dòng của bảng nguồn do RAG trả về (cả phạm vi Tài liệu này lẫn Toàn Workspace): số tham chiếu `[n]` trong ngữ cảnh và câu trả lời của AI ↔ (Document, Title, trang, Đoạn nguồn). AI chỉ chép số `[n]`, không chép `documentId` (hash dài); #108 biến `[n]` thành link nhảy sang Document và trang đó, #27 tô Đoạn nguồn trên trang.
_Avoid_: Citation (dành cho phần hiển thị ở #108), reference.

**Ngữ cảnh trả lời** (`AnswerContext`):
Phần chữ gửi kèm một câu hỏi cho AI, dựng theo **Phạm vi**, cùng bảng **Nguồn trích dẫn** và các thông báo cho người dùng về những gì câu trả lời chưa gồm (tài liệu đang lập chỉ mục, thiếu key, chỉ dùng được các trang quanh trang đang xem).
_Avoid_: RAG context, prompt context.

**Ảnh chụp tài liệu** (`DocumentSnapshot`):
Trạng thái của Document đang mở ở thời điểm hỏi (định danh, tên, khối chữ, trang đang xem), làm đầu vào cho Ngữ cảnh trả lời.
_Avoid_: ChatContext, document state.

**Đoạn nguồn** (`RagSource.Passage`, tô qua `SourcePassage`):
Chữ của chunk mà AI dựa vào cho một trích dẫn `[n]`. Bấm trích dẫn thì viewer nhảy tới trang và cuộn tới đúng đoạn này, tô màu cam (khác vàng của kết quả tìm), giữ tới khi mở trích dẫn khác, đổi Document đang xem hoặc bấm Esc. Nguồn lưu từ bản trước #27 không có Đoạn nguồn: vẫn nhảy trang, không tô.
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

**Phiên đọc**:
Document đang được đọc cùng những gì suy ra từ nó (cấu trúc, chữ từng trang, Đoạn nguồn) và ngữ cảnh đi kèm (UI panel notes của Tab, trạng thái Search). Mỗi lúc chỉ có một Phiên đọc, giống nhau ở Default Workspace và named Workspace. Kích hoạt Tab Document, Cross-doc jump, khôi phục Open Set hay mở lẻ một Document đều là thay Document của Phiên đọc. Kích hoạt Tab Bản soạn không đổi Phiên đọc: nó vẫn giữ Document của Tab Document active gần nhất; không còn Tab Document nào thì Phiên đọc rỗng. Phiên đọc không phải Workspace, cũng không phải Open Set.
_Avoid_: Session (trơn), active document state, "vào phiên đọc" theo nghĩa rời màn Workspaces.

**Tab**:
Một Document hoặc một Bản soạn đang mở trong phiên Workspace hiện tại, kèm view-state riêng. Có hai loại: **Tab Document** (trang, zoom, scroll) và **Tab Bản soạn** (scroll). Hai loại nằm chung một Tab Strip, xếp xen kẽ. Default Workspace có đúng một Tab Document và có thể thêm Tab Bản soạn (ADR 0009).
_Avoid_: Window, pane, view.

**Open Set**:
Tập Tab đang mở trong một Workspace: Document trong đó là **tập con** của membership, Bản soạn trong đó thuộc Workspace ấy. Mở/đóng Tab thay đổi Open Set, không đụng membership. Open Set được nhớ lại khi mở lại Workspace có tên. Ở Default Workspace chỉ Tab Bản soạn được nhớ lại, khi đọc lẻ lại Document đó.
_Avoid_: Open documents, active list.

**Per-tab view-state**:
Trạng thái hiển thị riêng của mỗi Tab, giữ trong bộ nhớ. Tab Document: trang + zoom (chính xác) và scroll (best-effort, chuẩn hóa trong trang), cộng UI panel notes (scroll + note đang chọn + bộ lọc-doc). Tab Bản soạn: chỉ vị trí cuộn; chế độ Sửa không thuộc view-state. Chat KHÔNG thuộc per-tab (xem **Chat thread**: theo Workspace). Search cũng **không** thuộc view-state: transient, xóa khi đổi tab.
_Avoid_: Tab settings.

**Cross-doc jump**:
Hành vi mở từ một Note/highlight của Document khác trong Workspace. Quy tắc "activate-or-open": nếu đã mở thì kích hoạt Tab đó, nếu chưa thì mở Tab mới (lười); luôn điều hướng tới trang đích, ghi đè vị trí đã nhớ cho lần nhảy đó. (Trong #25 trước đây cross-doc jump = thay thế doc đang mở.)
_Avoid_: Navigate, link.

**Tab Strip**:
Dải tab ngang ở đầu vùng nội dung chính (giữa toolbar và viewer). Tràn thì cuộn ngang; kéo để sắp lại; nút "+" mở Workspace Documents surface. Hiện trong named Workspace; ở Default Workspace chỉ hiện khi có Tab Bản soạn mở (từ hai tab trở lên), không có "+" (ADR 0009).
_Avoid_: Tab bar (dùng được nhưng thống nhất "Tab Strip").

**Workspace Documents surface**:
Surface hai vùng cho Tài liệu của Workspace: (1) **thành viên**: thumbnail member doc, click → mở/kích hoạt Tab, kèm nút gỡ (**gỡ = xóa membership và đóng Tab nếu đang mở**, giữ bất biến Open Set ⊆ membership); (2) **thêm từ Library**: Add = thêm membership **và** mở Tab active. Header surface mang tên Workspace + đổi tên inline (bút chì). Cùng MỘT component ở **2 bối cảnh host**: modal nút "+" (DialogHost) và inline trong canvas khi không còn Tab nào (gộp empty-state lẫn lưới re-open). Chỉ ở named Workspace.
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
