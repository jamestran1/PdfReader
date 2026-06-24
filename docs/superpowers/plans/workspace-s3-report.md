# Báo cáo Workspace S3 (#35)

## Trạng thái

DONE - tất cả 275 test xanh, build sạch.

## Các thay đổi

### File mới

- `src/PdfReaderApp/ViewModels/DocumentChip.cs` - Helper thuần: `ColorHexFor` (hash ổn định 8 màu) và `ShortLabel` (cắt + ellipsis).
- `tests/PdfReaderApp.Tests/ViewModels/DocumentChipTests.cs` - 8 unit test cho DocumentChip.

### File sửa

- `src/PdfReaderApp/ViewModels/NotesViewModel.cs`
  - Thêm tham số ctor `Action<string, int?>? openDocument = null` (tham số thứ 5, optional).
  - Sửa `Open(Note?)`: cross-doc -> gọi `_openDocument`; cùng doc hoặc null doc -> `_jumpToPageIndex`.
  - Thêm `[ObservableProperty] bool _showDocumentChips`.
  - Thêm `IReadOnlyDictionary<string,string> DocumentTitles` (readonly property).
  - Thêm `SetDocumentContext(titles, showChips)`.

- `src/PdfReaderApp/ViewModels/MainViewModel.cs`
  - Thêm `public string? CurrentDocumentId => _documentId`.
  - Gọi `OnPropertyChanged(nameof(CurrentDocumentId))` ở cả nhánh thành công và thất bại của `LoadActiveDocument`.
  - Wire `OpenDocumentForNote` làm tham số thứ 5 khi dựng `NotesViewModel`.
  - Thêm `private void OpenDocumentForNote(string, int?)`: tìm `LibraryItem` trong `Library`, gọi `LoadActiveDocument` giữ `_activeWorkspaceId`, nhảy trang.
  - Thêm `private void UpdateNotesDocumentContext()`: build `titles` từ `WorkspaceDocuments`, gọi `Notes.SetDocumentContext`.
  - Gọi `UpdateNotesDocumentContext()` trong: `LoadActiveDocument` (cả success/failure), `OpenWorkspace`, `AddDocumentsToWorkspace`, `RemoveDocumentFromWorkspace`.

- `src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs`
  - Thêm `DependencyProperty CurrentDocumentId` (string, default null); khi đổi -> `RepaintOverlay()`.
  - Trong `DrawSavedHighlights`: thêm filter `if (CurrentDocumentId != null && note.DocumentId != null && note.DocumentId != CurrentDocumentId) continue;`.

- `src/PdfReaderApp/MainWindow.xaml`
  - Đăng ký `DocumentIdToBrushConverter` và `DocumentIdToTitleConverter` trong `Window.Resources`.
  - Bind `CurrentDocumentId="{Binding CurrentDocumentId}"` trên `PdfViewerControl`.
  - Thêm chip Border (Row 0 mới) vào DataTemplate note card: `PackIcon FileDocumentOutline` + `TextBlock` dùng `MultiBinding` với `DocumentIdToTitleConverter`; visibility bind `Notes.ShowDocumentChips`; nền từ `DocumentIdToBrushConverter`.

- `src/PdfReaderApp/MainWindow.xaml.cs`
  - Thêm `using System.Collections.Generic`.
  - Thêm `DocumentIdToBrushConverter` (IValueConverter: DocumentId -> SolidColorBrush).
  - Thêm `DocumentIdToTitleConverter` (IMultiValueConverter: [DocumentId, DocumentTitles] -> nhãn rút gọn).

## Kết quả test

275 passed, 0 failed (bao gồm 16 test cũ + 11 test S3 mới: 8 DocumentChip + 7 NotesViewModel).

## Concerns

Không có.
