# Workspace (đa tài liệu) — Thiết kế

**Ngày:** 2026-06-24
**Issue:** #25 (epic Taking Note Layer 3). Trạng thái: đã grill (grill-with-docs + domain-modeling), chờ review spec.
**Tách ra:** tab đa tài liệu → #32.

## Mục tiêu

Đưa app từ "per-document" sang **không gian nghiên cứu đa tài liệu** (Workspace): gom nhiều tài liệu vào một ngữ cảnh có tên, **notes scope theo workspace** (thấy toàn bộ ghi chú dự án ở một chỗ). Bản lề tới NotebookLM (#26 AI đa tài liệu, #27 citations dựa trên nền này).

## Domain model (ubiquitous language)

| Thuật ngữ | Nghĩa | Khóa |
|---|---|---|
| **Document (Source)** | Một PDF trong kho chung (library) | `documentId` = SHA256 nội dung |
| **Workspace** | Ngữ cảnh nghiên cứu có tên, tham chiếu **nhiều** Document (many-to-many); sở hữu Notes của nó | `id` = GUID |
| **Default workspace** | Workspace tự tạo cho mỗi Document (chứa đúng doc đó); backing notes của doc lẻ; **ẩn khỏi danh sách Workspaces, không xóa được** | `id` = GUID; `IsDefault=true`; `DefaultDocumentId` = doc của nó |
| **Membership** | "Document nào trong Workspace nào" | (workspaceId, documentId) |
| **Note** | `owner_key = workspaceId` (phạm vi); `document_id` = anchor (doc được neo) | (đã có) |
| **Active scope** | Workspace đang mở — quyết định notes hiển thị | activeWorkspaceId |

Lưu ý: code/UI dùng từ **"Workspace"**. "Default workspace" là khái niệm tường minh (có bản ghi thật, `IsDefault`), KHÔNG dùng documentId làm owner_key ngầm.

## Quyết định đã chốt (grill 2026-06-24)

1. **Document ↔ Workspace: many-to-many.** Document ở kho chung; workspace tham chiếu; một doc có thể ở nhiều workspace; notes/chat scope khác nhau từng workspace.
2. **Phạm vi #25 (Phương án A):** notes scope theo workspace; **chat vẫn per-document** (`documentId`, không đổi `chats.db`); RAG/chat xuyên-tài-liệu để **#26**.
3. **Migrate tường minh:** tạo default workspace cho mỗi document hiện có; chuyển notes `owner_key documentId→workspaceId(default)`; import mới tự tạo default WS.
4. **UX:** giữ lưới Document (mở = vào default WS của doc) + khu **Workspaces** riêng (chỉ WS tự tạo, default ẩn). Entry point: nút nav rail trái dưới "Thư viện", icon `FolderMultipleOutline`, ToolTip "Workspaces", lệnh `ShowWorkspacesViewCommand`.
5. **Notes panel trong WS:** hiện **mọi** notes của workspace; mỗi thẻ có **chip nhãn doc** (icon + tên rút gọn, **màu suy ra ổn định từ documentId**, hiện khi WS >1 doc); click note doc khác → **mở doc đó (thay thế doc đang mở) + nhảy trang**; highlight trên trang **lọc theo `document_id`** doc đang mở (sửa 2c). Highlight vẫn **vàng** (không đổi 2c).
6. **Tab đa tài liệu → #32** (ngoài #25). Cross-doc jump trong #25 = thay doc đang mở.
7. **CRUD Workspace:** tạo (bắt buộc tên), đổi tên, thêm/bớt tài liệu, xóa. **Xóa WS → xóa notes của nó** (owner_key=workspaceId); **default WS không xóa được**; bỏ doc khỏi WS → **giữ** note đã neo; xóa doc khỏi **library** → gỡ khỏi mọi WS + xóa notes per-doc của doc (mở rộng `RemoveLibraryItem`).
8. **Store: workspaces.db riêng** (`WorkspaceStore`, `user_version` riêng; dọn membership bằng code như notes/chat).

## Kiến trúc & thành phần

### A. Model

- `record Workspace(string Id, string Name, bool IsDefault, string? DefaultDocumentId, long CreatedAtUnixMs, long UpdatedAtUnixMs)`.
- Membership không cần record riêng (xử lý qua store API trả `IReadOnlyList<string> documentIds`).

### B. Store mới — `IWorkspaceStore` + `SqliteWorkspaceStore` (workspaces.db)

- `void EnsureSchema()` — bảng:
  - `workspace(id TEXT PK, name TEXT NOT NULL, is_default INTEGER NOT NULL, default_document_id TEXT, created_at INTEGER, updated_at INTEGER)`
  - `workspace_document(workspace_id TEXT NOT NULL, document_id TEXT NOT NULL, PRIMARY KEY(workspace_id, document_id))` + index trên `document_id`.
  - `PRAGMA user_version = 1`.
- `void Upsert(Workspace w)`; `void Rename(string id, string name, long nowUnixMs)`; `void Delete(string id)` (xóa workspace + memberships của nó).
- `IReadOnlyList<Workspace> GetAll(bool includeDefault)` — danh sách (sắp updated desc); UI Workspaces dùng `includeDefault:false`.
- `Workspace? Get(string id)`; `Workspace GetOrCreateDefaultForDocument(string documentId, string name, long nowUnixMs)` — trả default WS của doc (tạo nếu chưa có).
- `void AddDocument(string workspaceId, string documentId)`; `void RemoveDocument(string workspaceId, string documentId)`; `IReadOnlyList<string> GetDocumentIds(string workspaceId)`; `IReadOnlyList<string> GetWorkspaceIdsForDocument(string documentId)` (để dọn khi xóa doc khỏi library).
- Per-op connection `Pooling=False` + lock (như các store khác).

### C. Migration (một lần, lúc khởi tạo)

Khi dựng app: với mỗi `LibraryItem` chưa có default workspace → `GetOrCreateDefaultForDocument(documentId, item.Title, now)` (tạo workspace `IsDefault=true, DefaultDocumentId=documentId`, add membership), rồi `UPDATE note SET owner_key=$wsId WHERE owner_key=$documentId` (qua một method mới trên `INoteStore`: `void ReassignOwner(string oldKey, string newKey)`). Idempotent: chỉ chạy cho doc chưa có default WS. (Notes đã migrate sẽ không khớp owner_key=documentId nữa nên không chạy lại.)

### D. NotesViewModel

- `LoadFor(workspaceId)` đã có (owner_key). Bổ sung:
  - `Highlights` lọc khi vẽ theo doc đang mở: thực ra lọc nằm ở viewer; xem F.
  - Chip nhãn doc: thẻ note cần tên doc. Thêm tra cứu `Func<string?, string?> documentTitleResolver` (documentId→Title) inject vào NotesViewModel (từ library). Note model không đổi.
  - Cross-doc open: `Open(note)` — nếu `note.DocumentId` ≠ doc đang mở → gọi callback `Action<string> openDocument(documentId)` rồi nhảy trang; nếu cùng doc → set trang như cũ. Thêm callback vào ctor.

### E. MainViewModel

- Trạng thái: thêm `_activeWorkspaceId` (string?). Khi mở doc:
  - Mở doc lẻ (từ lưới Document/`OpenLibraryItem`): `_activeWorkspaceId = workspaceStore.GetOrCreateDefaultForDocument(documentId, title, now).Id`; `Notes.LoadFor(_activeWorkspaceId)`.
  - Mở doc qua một workspace W: `_activeWorkspaceId = W.Id`; `Notes.LoadFor(W.Id)`.
- Khu Workspaces: `[ObservableProperty] bool _showWorkspaces`; `ObservableCollection<Workspace> Workspaces`; lệnh `ShowWorkspacesView`, `CreateWorkspace(name)`, `RenameWorkspace`, `DeleteWorkspace(ws)`, `OpenWorkspace(ws)` (hiện danh sách doc của WS), `AddDocumentsToWorkspace(...)`, `RemoveDocumentFromWorkspace(...)`.
- Cross-doc open callback cho NotesViewModel: `documentId => LoadActiveDocument(library path của doc)` (giữ active workspace hiện tại).
- `RemoveLibraryItem`: mở rộng — gỡ doc khỏi mọi workspace + (đã có) xóa notes/chat per-doc; xóa luôn default WS của doc.

### F. PdfViewerControl (sửa lọc highlight 2c)

- `DrawSavedHighlights` hiện lọc `note.PageIndex == pageIndex`. Thêm điều kiện **`note.DocumentId == <documentId của trang đang vẽ>`**. Viewer cần biết documentId của doc đang mở → thêm DependencyProperty `ActiveDocumentId` (string), bind từ MainViewModel; lọc highlight theo nó. (Trong workspace nhiều doc, Highlights chứa note của nhiều doc; chỉ vẽ của doc đang mở.)

### G. XAML (MainWindow.xaml)

- Nav rail: thêm nút Workspaces (icon `FolderMultipleOutline`, ToolTip "Workspaces", `ShowWorkspacesViewCommand`) dưới nút "Thư viện".
- Khu Workspaces (Grid.Row tương tự lưới library, Visibility theo `ShowWorkspaces`): lưới thẻ WS (tên + số tài liệu + thumbnail doc đầu); nút "Tạo workspace" (dialog nhập tên); thẻ có nút đổi tên/xóa.
- Màn một workspace (khi `OpenWorkspace`): danh sách tài liệu của WS + nút "Thêm tài liệu" (chọn từ library, đa chọn) + bỏ tài liệu; mở 1 doc → đọc, active scope = WS này.
- Thẻ note: thêm chip nhãn doc (màu deterministic) khi `Note.DocumentId` khác doc đang... (luôn hiện trong WS >1 doc; ẩn ở default WS).
- `PdfViewer` thêm `ActiveDocumentId="{Binding ...}"`.

## Xử lý lỗi / edge

- Doc trong workspace bị xóa khỏi library: gỡ membership; note neo doc đó còn (quote/content), cross-doc jump báo nhẹ "tài liệu không còn".
- Tạo WS tên rỗng: chặn.
- Mở doc khi chưa migrate: migration chạy idempotent lúc khởi tạo nên luôn có default WS.
- Lỗi store: nuốt ở VM (như các store khác); store để lỗi nổi lên (test bắt).

## Kiểm thử

- `SqliteWorkspaceStore`: Upsert/Get/GetAll(includeDefault), Add/Remove/GetDocumentIds membership, GetWorkspaceIdsForDocument, Delete (xóa WS + membership), GetOrCreateDefaultForDocument (tạo 1 lần, idempotent), Rename; migrate user_version.
- `INoteStore.ReassignOwner(old,new)` (store thật): đổi owner_key đúng + cô lập.
- `NotesViewModel`: cross-doc `Open` gọi openDocument khi DocumentId khác; chip title qua resolver; Highlights vẫn đúng.
- `MainViewModel`: OpenLibraryItem set activeWorkspaceId = default WS; OpenWorkspace set = WS.Id; CreateWorkspace tên rỗng bị chặn; DeleteWorkspace xóa notes + chặn default; RemoveLibraryItem gỡ membership + xóa default WS. (Phần cần document thật → verify GUI.)
- Migration: dựng library + notes kiểu cũ (owner_key=documentId) → chạy migrate → mỗi doc có default WS + notes owner_key đã đổi; chạy lại không nhân đôi.
- Highlight lọc theo documentId (viewer) + cross-doc jump + UI Workspaces: verify GUI.

## Loại trừ (GitHub Issues)

- Tab đa tài liệu (#32) · RAG/chat xuyên-workspace (#26) · citations (#27) · note-làm-nguồn (#28) · "Add to workspace" trên thẻ document (để sau) · màu theo doc cho highlight trên trang (giữ vàng).
