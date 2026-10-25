using System.Windows;
using System.Windows.Input;

namespace PdfReaderApp.Controls.Viewer;

/// <summary>
/// Những gì các lớp xử lý input (chuột, phím, sau này là con lăn) cần từ viewer: đọc trạng thái hiện tại và YÊU CẦU
/// control làm việc. Control vẫn là nơi duy nhất ghi DependencyProperty, cuộn và gọi <c>InvalidateVisual</c>;
/// interface này cố ý không có setter hay method cuộn, để input không thể tự ghi.
/// </summary>
internal interface IViewerHost
{
    /// <summary>Session của tài liệu đang mở; null khi chưa mở hoặc đã đóng.</summary>
    PdfDocumentSession? Session { get; }

    /// <summary>Vị trí các trang trên canvas (DIP).</summary>
    IReadOnlyList<Core.PageSlot> Slots { get; }

    /// <summary>Số pixel trên một PDF point.</summary>
    double ZoomLevel { get; }

    /// <summary>Command nhận <c>NoteSelection</c> khi người dùng bấm "Thêm ghi chú"; null nếu chưa gắn.</summary>
    ICommand? AddNoteFromSelectionCommand { get; }

    /// <summary>Trình sửa text tại chỗ đang hiện.</summary>
    bool IsEditorVisible { get; }

    /// <summary>Đã nạp tài liệu vào viewer.</summary>
    bool HasDocument { get; }

    Core.PdfViewMode ViewMode { get; }

    bool ShowCover { get; }

    /// <summary>Trang hiện tại (1-based).</summary>
    int CurrentPage { get; }

    int TotalPages { get; }

    double ScrollHorizontalOffset { get; }

    double ScrollVerticalOffset { get; }

    double ScrollableHeight { get; }

    /// <summary>Yêu cầu đổi zoom rồi dựng lại layout ngay, để offset cuộn tính tiếp theo đúng với kích thước canvas mới.</summary>
    void RequestZoom(double zoom);

    /// <summary>Yêu cầu chuyển tới trang (1-based).</summary>
    void RequestPage(int page);

    void RequestScroll(double horizontalOffset, double verticalOffset);

    void RequestScrollToTop();

    /// <summary>Yêu cầu vẽ lại lớp Skia.</summary>
    void RequestRepaint();

    /// <summary>Yêu cầu hiện trình sửa text tại vị trí trên canvas tương tác (DIP).</summary>
    void ShowEditor(Point topLeft, Size size, string text);

    /// <summary>Yêu cầu ẩn trình sửa text và trả focus về canvas vẽ.</summary>
    void HideEditor();

    /// <summary>Yêu cầu hiện nút "Thêm ghi chú" tại vị trí trên canvas tương tác (DIP).</summary>
    void ShowAddNoteButton(Point topLeft);

    void HideAddNoteButton();

    /// <summary>Yêu cầu canvas tương tác giữ chuột trong lúc kéo chọn.</summary>
    void CapturePointer();

    void ReleasePointer();
}
