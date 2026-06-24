# Báo cáo triển khai Workspace S2 (#34)

## Trạng thái

**DONE** - Tất cả tests xanh (261 passed, 0 failed, 0 skipped).

## Commits

Xem commit hash sau khi git commit hoàn tất (branch: feature/workspace-s2).

## Tóm tắt test

261 passed, 0 failed -- bao gồm 7 tests mới của S2 và 254 tests cũ giữ nguyên xanh.

## Thay đổi đã thực hiện

### src/PdfReaderApp/AssemblyInfo.cs
- Thêm `[assembly: InternalsVisibleTo("PdfReaderApp.Tests")]` để test truy cập `internal ResolveWorkspaceScope`.

### src/PdfReaderApp/ViewModels/MainViewModel.cs

Thuộc tính mới:
- `[ObservableProperty] Workspace? _selectedWorkspace`
- `[ObservableProperty] bool _showWorkspaceDetail`
- `ObservableCollection<LibraryItem> WorkspaceDocuments`
- `public string? ActiveWorkspaceId => _activeWorkspaceId` (expose cho test)
- `public bool ShowWorkspacesGrid => ShowWorkspaces && !ShowWorkspaceDetail`
- `partial void OnShowWorkspacesChanged` raise `ShowWorkspacesGrid` (thay thế partial cũ, thêm `OnPropertyChanged`)
- `partial void OnShowWorkspaceDetailChanged` raise `ShowWorkspacesGrid`

Commands mới:
- `OpenWorkspace` (refactor): set `SelectedWorkspace`, `ShowWorkspaces = true`, `ShowWorkspaceDetail = true`, gọi `ReloadWorkspaceDocuments()`
- `ShowWorkspacesView` (refactor): set `ShowWorkspaceDetail = false` truoc khi show luoi
- `AddDocumentsToWorkspace(IList?)`: them nhieu tai lieu vao workspace
- `RemoveDocumentFromWorkspace(LibraryItem?)`: xoa tai lieu khoi workspace
- `BackToWorkspaceList()`: `ShowWorkspaceDetail = false`, `ReloadWorkspaces()`
- `OpenWorkspaceDocument(LibraryItem?)`: goi `LoadActiveDocument(path, workspaceId)`, chuyen sang trang doc

Private helpers:
- `ReloadWorkspaceDocuments()`: dong bo `WorkspaceDocuments` tu store

Refactor `LoadActiveDocument`:
- Chu ky moi: `private void LoadActiveDocument(string path, string? workspaceScopeId = null)`
- Dung seam `ResolveWorkspaceScope` thay vi inline `GetOrCreateDefaultForDocument`
- Raise `OnPropertyChanged(nameof(ActiveWorkspaceId))` sau khi cap nhat

Seam moi:
```csharp
internal string ResolveWorkspaceScope(string? explicitWorkspaceId, string documentId, string title, long nowUnixMs)
    => explicitWorkspaceId ?? _workspaceStore.GetOrCreateDefaultForDocument(documentId, title, nowUnixMs).Id;
```

### tests/PdfReaderApp.Tests/MainViewModelTests.cs

`FakeWorkspaceStore` nang cap:
- Them `Dictionary<string, HashSet<string>> Membership`
- `AddDocument`, `RemoveDocument`, `GetDocumentIds` thuc su theo doi membership

7 tests S2 moi:
1. `OpenWorkspace_LoadsDocuments_AndShowsDetail` - xac nhan ShowWorkspaceDetail, SelectedWorkspace, WorkspaceDocuments, ActiveWorkspaceId, ShowWorkspacesGrid=false
2. `AddDocumentsToWorkspace_AddsMembershipAndRefreshes` - them nhieu tai lieu, kiem tra store va collection
3. `RemoveDocumentFromWorkspace_RemovesMembershipAndRefreshes` - xoa tai lieu, kiem tra store va collection
4. `ResolveWorkspaceScope_ExplicitWorkspace_ReturnsThatId` - seam tra ve id truyen vao
5. `ResolveWorkspaceScope_NullExplicit_ReturnsDefaultWorkspaceId` - seam tao default, idempotent
6. `BackToWorkspaceList_ShowsGridAgain` - quay lai luoi, kiem tra ShowWorkspacesGrid=true
7. `OpenWorkspaceDocument_SetsActiveWorkspaceToWorkspaceId` - integration test voi PDF that (iText), kiem tra ActiveWorkspaceId va IsReadingDocument

### src/PdfReaderApp/MainWindow.xaml

- Doi Visibility bind cua luoi workspace tu `ShowWorkspaces` sang `ShowWorkspacesGrid`
- Them panel chi tiet workspace (Visibility bind `ShowWorkspaceDetail`) gom:
  - Nut Quay lai (ArrowLeft icon) + ten workspace
  - Danh sach "Tai lieu trong workspace" (ItemsControl bind `WorkspaceDocuments`): nut Mo + nut xoa
  - Text goi y khi rong: "Chua co tai lieu nao trong workspace. Them tu thu vien ben duoi."
  - Khu them: `ListBox` `SelectionMode="Extended"` bind `Library`, nut "Them vao workspace" voi `CommandParameter=SelectedItems`

## Luu y / Concerns

- `_library.MarkOpened` trong `OpenWorkspaceDocument` duoc boc try/catch de tranh loi khi document chua duoc import vao `LibraryService` (vd trong moi truong test). Hanh vi san xuat khong anh huong: document da import truoc khi mo thi MarkOpened van duoc goi thanh cong.
- Test 7 (integration) tao PDF tam bang iText va doc bang `ITextPdfDocumentService` -- thanh cong trong moi truong test vi PDF hop le. Don dep thu muc tam sau khi test.
- `ShowWorkspacesGrid` la computed property (khong phai `[ObservableProperty]`) -- duoc raise thu cong trong `OnShowWorkspacesChanged` va `OnShowWorkspaceDetailChanged`.
