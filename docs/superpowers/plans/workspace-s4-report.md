# Workspace S4 (#36) — Báo cáo hoàn thành

## Trạng thái: DONE

Commit: `d3dd87b`

## Tóm tắt test

285 passed, 0 failed, 0 skipped (bao gồm tất cả test S4 mới).

## Những gì đã thực hiện

### 1. INoteStore + SqliteNoteStore
- Thêm `DeleteForOwner(string ownerKey)`: xóa tất cả ghi chú có `owner_key=$o`, trả số dòng.
- Thêm `DeleteForDocument(string documentId)`: xóa tất cả ghi chú có `document_id=$d`, trả số dòng.

### 2. MainViewModel
- Thêm `private readonly INoteStore _noteStore` (gán từ tham số `noteStore`, tái dùng cùng instance cho `Notes` và logic cascade).
- Thêm `[ObservableProperty] private string _renameDraft` cho ô đổi tên trong UI.
- Thêm `DeleteWorkspaceCommand(Workspace? ws)`: chặn default WS; xóa notes -> xóa WS; reset active scope nếu cần; reload.
- Thêm `RenameWorkspaceCommand(string? newName)`: validate rỗng -> set `WorkspaceNameError`; gọi `_workspaceStore.Rename`; reload header và danh sách.
- Mở rộng `RemoveLibraryItem` cascade:
  - Với mỗi WS chứa tài liệu: default WS -> xóa notes owner + xóa WS; workspace dùng chung -> chỉ gỡ membership (giữ notes).
  - Xóa notes neo tới tài liệu (`DeleteForDocument`).
  - Dọn chat, index, library.
  - `ReloadWorkspaces()` sau cùng.

### 3. MainWindow.xaml
- Lưới workspace: thêm nút xóa (icon Delete) góc trên phải mỗi card, bind `DeleteWorkspaceCommand`, ToolTip "Xóa workspace".
- Chi tiết workspace: thêm hàng đổi tên (Row 1 mới) gồm `TextBox` bind `RenameDraft` + nút "Đổi tên" bind `RenameWorkspaceCommand` + `TextBlock` hiện `WorkspaceNameError` màu đỏ. Các row cũ dịch +1.

### 4. Tests mới (TDD)
Store:
- `SqliteWorkspaceStoreTests.Delete_RemovesWorkspaceAndMembership`
- `SqliteNoteStoreTests.DeleteForOwner_RemovesOnlyThatOwnersNotes`
- `SqliteNoteStoreTests.DeleteForDocument_RemovesOnlyNotesAnchoredToThatDocument`

MainViewModel (7 test S4):
- `DeleteWorkspace_DeletesItsNotes_AndRemovesWorkspace`
- `DeleteWorkspace_DefaultWorkspace_IsNotDeleted`
- `RenameWorkspace_EmptyName_SetsError_AndKeepsName`
- `RenameWorkspace_ValidName_UpdatesName`
- `RemoveDocumentFromWorkspace_KeepsAnchoredNotes`
- `RemoveLibraryItem_Cascades_RemovesMembership_DeletesDefaultWs_CleansNotesAndChat`

`NotesViewModelTests.FakeNoteStore` cung cấp `DeleteForOwner`/`DeleteForDocument` (interface update).
`FakeWorkspaceStore` trong `MainViewModelTests` cập nhật `GetWorkspaceIdsForDocument`, `Rename`, `Delete` cho đúng hành vi.

## Concerns

Không có concerns. Tất cả ràng buộc bắt buộc được tuân thủ:
- Diacritics tiếng Việt giữ nguyên trong mọi chuỗi UI và comment.
- Không dùng em dash.
- Build qua `dotnet build PdfReaderApp.slnx`, test qua `dotnet test`.
- Commit trên `feature/workspace-s4`, không có Co-Authored-By trailer.
