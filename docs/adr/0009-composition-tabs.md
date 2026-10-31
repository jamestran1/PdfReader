---
status: accepted
---

# Tab có hai loại: Document và Bản soạn

Bản soạn (#28) mở thành tab ở vùng giữa, cạnh Tab Document (#252), nên mô hình tab của ADR 0003 không còn chỉ biết Document. Ta mở rộng nó thay vì dựng một mô hình tab thứ hai (#284).

## Quyết định

- **Một tập tab, hai loại.** `OpenTab` thành lớp cơ sở với `DocumentTab` và `CompositionTab`. Ở Workspace có tên, hai loại dùng chung `TabSetViewModel`, một tab active, một Tab Strip, xếp xen kẽ và kéo sắp lại được. Tab Bản soạn không giữ handle PDFium nên đứng ngoài hydrate/evict MRU.
- **Default Workspace giữ viewer standalone, thêm Tab Strip nhẹ.** Bản soạn thuộc cả Default Workspace (#248). Default Workspace không chuyển sang `TabSetViewModel`: Document vẫn hiển thị qua viewer standalone (`StandaloneDocumentSource`), vẫn chỉ có đúng một Tab Document (mở doc khác là chuyển sang Default Workspace của doc đó, như ADR 0004), và tab này không đóng được. Một Tab Strip nhẹ riêng gồm tab Document trỏ vào viewer standalone cộng các Tab Bản soạn; strip chỉ hiện khi có từ hai tab, không có nút "+". Đây không phải phương án "tập tab riêng cho Bản soạn" đã loại bên dưới: Default Workspace vẫn chỉ có một dải tab và một tab active, chỉ khác cách host tab Document.
- **Tab Bản soạn không đổi Phiên đọc.** Phiên đọc giữ Document của Tab Document active gần nhất: neo trích dẫn của Chat, panel Notes và bộ lọc "chỉ tài liệu này" vẫn bám Document đó. Open Set không còn Tab Document nào thì Phiên đọc rỗng: Chat không có neo trích dẫn, panel Notes không lọc theo doc. Search của viewer tắt khi đang ở Tab Bản soạn. Chat không đọc nội dung Bản soạn.
- **Khôi phục cả Tab Bản soạn, ở mọi Workspace.** Bảng `open_tab` thêm cột `tab_kind` và đổi `document_id` thành `item_id` (id Document hoặc id Bản soạn theo loại); khoá chính thành `(workspace_id, tab_kind, item_id)`; `page` và `zoom` vẫn `NOT NULL`, dòng Bản soạn ghi giá trị mặc định (`page = 1`, `zoom = 1`) để sau này Bản soạn có nhiều trang hay có zoom thì dùng luôn hai cột này, vị trí cuộn nằm ở `scroll_norm`. Tab Bản soạn nhớ thứ tự, tab active và vị trí cuộn; chế độ Sửa không nhớ. Default Workspace lưu Tab Bản soạn dưới `workspace_id` là id của chính dòng Default Workspace trong bảng `workspace` (`is_default=1`, `default_document_id` = Document đó, lấy qua `GetOrCreateDefaultForDocument`, #248), nên mỗi Document có Open Set riêng: đọc lẻ lại một Document thì các Tab Bản soạn của nó mở lại. Tab Document của Default Workspace không ghi vào `open_tab`: mở lẻ vẫn chỉ nạp lại Document như hiện nay, ADR này không đổi.
- **Sửa tự lưu.** Chế độ Sửa lưu sau một quãng ngắn không gõ và khi rời tab, nên đóng tab, đổi Workspace hay thoát app không mất gì và không có hộp thoại "Lưu thay đổi?". Không lặp lại hạn chế "âm thầm bỏ chỉnh sửa" của ADR 0003.

## Considered options

- **Mở Bản soạn thay chỗ viewer ở Default Workspace** (như mở một Document lẻ): đơn giản nhưng mất việc đối chiếu song song với Document; loại.
- **Chỉ cho mở Bản soạn ở Workspace có tên:** đi ngược việc Bản soạn thuộc cả Default Workspace (#248); loại.
- **Đưa Default Workspace sang `TabSetViewModel`:** một đường host tab duy nhất, nhưng phải bỏ viewer standalone và đổi hành vi đọc lẻ của ADR 0004 chỉ để chứa Tab Bản soạn; loại.
- **Tập tab riêng cho Bản soạn:** hai dải tab, hai tab active trong cùng một Workspace, khó trả lời "đang xem gì"; loại.
- **Promote mang Bản soạn sang Workspace mới, hoặc giữ ở cả hai nơi và đồng bộ:** phá quy tắc mỗi Note, mỗi Bản soạn thuộc đúng một Workspace, và kéo theo việc chia sẻ Note giữa các Workspace; loại khỏi ADR này, để ngỏ ở #286.

## Consequences

- Lật lại một phần ADR 0003 và ADR 0004: câu "Đọc lẻ (Default Workspace) luôn một tab" (ADR 0003), "KHÔNG Tab Strip" và "Tab chrome CHỈ xuất hiện ở chế độ này" (ADR 0004) nay chỉ đúng khi Default Workspace chưa mở Tab Bản soạn nào. Phần còn lại của hai ADR giữ nguyên: vẫn đúng một Tab Document, không "+", mở doc khác là thay thế.
- Hành vi code hiện có bị đổi:
  - `OpenSetPersistence` hiện bỏ qua Default Workspace (`!_isWorkspaceSession()` thì return) và chỉ đọc tab từ `TabSetViewModel`. Nó phải lưu cả Tab Bản soạn của Tab Strip nhẹ ở Default Workspace, tức cần thêm nguồn tab thứ hai; Tab Document ở đó vẫn không ghi vào `open_tab`.
  - `ReadingSessionViewModel.RestoreOpenSet` (đặt `IsWorkspaceSession = true`, `Tabs.Reset()`, seed Tab Document) giữ nguyên cho Workspace có tên và không dùng cho Default Workspace. Mở lẻ (`ReplaceCurrentDocument`) cần một đường khôi phục riêng: nạp Tab Bản soạn đã lưu của Default Workspace vào Tab Strip nhẹ, và trước khi đổi doc phải lưu Tab Bản soạn của Default Workspace cũ.
  - Bảng `open_tab` (`SqliteWorkspaceStore`) thêm cột `tab_kind`, `document_id` thành `item_id`, khoá chính thành `(workspace_id, tab_kind, item_id)`. Store hiện chỉ có `CREATE TABLE IF NOT EXISTS`; đổi khoá chính trong SQLite là dựng lại bảng, nên cần bước migration mới, dòng cũ thành Tab Document. `OpenTabState(DocumentId, ...)` và `IWorkspaceStore.SaveOpenTabs`/`GetOpenTabs` đổi theo.
  - `OpenTab` hiện là `sealed` và chỉ biết Document; tách thành lớp cơ sở cùng `DocumentTab` và `CompositionTab`.
  - `MainViewModel.IsDocumentTabStripVisible` (`IsWorkspaceSession && IsReadingDocument`) vẫn chỉ điều khiển Tab Strip của Workspace có tên; Tab Strip nhẹ có thuộc tính hiển thị riêng (từ hai tab).
- Default Workspace có hai đường host tab (viewer standalone cộng Tab Strip nhẹ) bên cạnh `TabSetViewModel`. Hai đường phải giữ cùng hành vi khi đóng tab, chuyển sang tab MRU và khôi phục; test của các hành vi này phải chạy trên cả hai đường.
- Mỗi Document có Default Workspace riêng, nên mở doc khác ở Default Workspace là đổi Workspace: mọi Tab Bản soạn đóng, và mở lại khi đọc lẻ lại doc cũ.
- Promote giữ nguyên Default Workspace: Note, Bản soạn và Tab Bản soạn ở lại đó; named Workspace mới chỉ có Tab Document mang theo trang và zoom.
- Xoá Bản soạn đang mở (từ thanh công cụ hay rail, sau khi xác nhận) thì đóng tab và chuyển sang tab MRU, hết tab thì như đóng tab cuối ở ADR 0003.
- Sinh lại mở Bản soạn mới thành tab active ngay bên phải tab cũ; bấm `[n]` là Cross-doc jump (activate-or-open Tab Document của Note).
