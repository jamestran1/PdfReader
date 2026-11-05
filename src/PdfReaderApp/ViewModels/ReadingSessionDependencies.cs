using System.Windows.Threading;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>Mọi bên cộng tác của Phiên đọc, truyền qua constructor (CS-11). Phiên đọc giữ thứ tự gọi, các bên chỉ thực thi.</summary>
/// <param name="Loader">Bộ nạp Document.</param>
/// <param name="WorkspaceStore">Lấy hoặc tạo Default Workspace và lưu Open Set.</param>
/// <param name="Search">Search.</param>
/// <param name="Chat">Chat.</param>
/// <param name="Notes">Notes.</param>
/// <param name="Indexing">Lập chỉ mục nền.</param>
/// <param name="Surface">Workspace Documents surface.</param>
/// <param name="NotifyError">Báo lỗi cho người dùng.</param>
/// <param name="DispatcherProvider">Dispatcher gộp ghi Open Set; null hoặc trả null thì lưu ngay (test, headless). Nơi dựng ứng dụng truyền dispatcher của app.</param>
/// <param name="MarkDocumentOpened">Ghi thời điểm mở của Document vào Library; null thì bỏ qua (best-effort).</param>
/// <param name="FindComposition">Tìm Bản soạn của Workspace theo id (khôi phục Open Set); null hoặc trả null thì Bản soạn coi như đã xoá.</param>
/// <param name="FindDocument">Tìm Document trong Library theo id (Jump, khôi phục Open Set); null hoặc trả null thì Document coi như đã gỡ.</param>
/// <param name="Clock">Đồng hồ cho thời điểm ghi Workspace mặc định; null thì dùng <see cref="TimeProvider.System"/>.</param>
public sealed record ReadingSessionDependencies(
    IDocumentLoader Loader,
    IWorkspaceStore WorkspaceStore,
    IReadingSessionSearch Search,
    IReadingSessionChat Chat,
    IReadingSessionNotes Notes,
    IReadingSessionIndexing Indexing,
    IReadingSessionSurface Surface,
    Action<string> NotifyError,
    Func<Dispatcher?>? DispatcherProvider = null,
    Action<string>? MarkDocumentOpened = null,
    LibraryDocumentLookup? FindDocument = null,
    TimeProvider? Clock = null,
    CompositionLookup? FindComposition = null);
