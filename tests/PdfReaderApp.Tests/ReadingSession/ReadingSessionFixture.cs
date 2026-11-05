using PdfReaderApp.Models;
using PdfReaderApp.Tests.Fakes;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ReadingSession;

/// <summary>
/// Dựng Phiên đọc với bộ nạp giả và fake của từng interface hẹp, dùng chung cho mọi file test intent
/// (<c>ReadingSessionActivateTests</c>, <c>ReadingSessionCloseTests</c>...). Mọi lời gọi ghi vào <see cref="Log"/>
/// theo đúng thứ tự để test kiểm thứ tự mà Phiên đọc quyết định.
/// </summary>
internal sealed class ReadingSessionFixture
{
    public const string WorkspaceId = "ws-1";

    public ReadingSessionFixture()
    {
        Loader = new FakeDocumentLoader { Log = Log };
        Search = new FakeSessionSearch(Log);
        Chat = new FakeSessionChat(Log);
        Notes = new FakeSessionNotes(Log);
        Indexing = new FakeSessionIndexing(Log);
        Surface = new FakeSessionSurface(Log);
        Session = new ReadingSessionViewModel(new ReadingSessionDependencies(
            Loader, Store, Search, Chat, Notes, Indexing, Surface, Errors.Add,
            DispatcherProvider: () => null,   // null: lưu Open Set ngay, tất định
            MarkDocumentOpened: RecordOpened,
            FindDocument: id => Library.GetValueOrDefault(id),
            FindComposition: (_, id) => Compositions.GetValueOrDefault(id)));
    }

    /// <summary>Id Document đã được ghi thời điểm mở vào Library.</summary>
    public List<string> Opened { get; } = [];

    /// <summary>Bắt việc ghi thời điểm mở ném lỗi (best-effort không được chặn việc mở).</summary>
    public bool FailMarkOpened { get; set; }

    private void RecordOpened(string documentId)
    {
        if (FailMarkOpened) throw new InvalidOperationException("Library không ghi được");
        Opened.Add(documentId);
    }

    public CallLog Log { get; } = new();
    public FakeDocumentLoader Loader { get; }
    public FakeWorkspaceStore Store { get; } = new();
    public FakeSessionSearch Search { get; }
    public FakeSessionChat Chat { get; }
    public FakeSessionNotes Notes { get; }
    public FakeSessionIndexing Indexing { get; }
    public FakeSessionSurface Surface { get; }
    public List<string> Errors { get; } = [];
    public Dictionary<string, LibraryItem> Library { get; } = [];

    /// <summary>Bản soạn còn tồn tại theo id (Bản soạn đã xoá thì không có trong đây).</summary>
    public Dictionary<string, Composition> Compositions { get; } = [];
    public ReadingSessionViewModel Session { get; }

    public static string PathOf(string documentId) => $"/docs/{documentId}.pdf";

    /// <summary>Vào named Workspace <see cref="WorkspaceId"/> (Open Set trống).</summary>
    public ReadingSessionFixture InWorkspace()
    {
        Session.RestoreOpenSet(WorkspaceId);   // Open Set trống: chỉ vào phiên Workspace
        return this;
    }

    /// <summary>Mở một Tab cho <paramref name="documentId"/> và kích hoạt nó; xoá nhật ký để test chỉ thấy phần sau đó.</summary>
    public OpenTab OpenTab(string documentId, int page = 1)
    {
        Loader.Returns(PathOf(documentId), new LoadedDocument(documentId, [], []));
        // Mở qua Tab Set với initialPage (phiên Workspace đã có từ InWorkspace) để Document nạp thẳng tại trang đích,
        // giống đường Jump; gán Page sau khi nạp sẽ không tái hiện đúng thứ tự viewer thấy.
        var tab = Session.Tabs.OpenOrActivate(documentId, documentId, PathOf(documentId), initialPage: page);
        Opened.Clear();
        Log.Clear();
        return tab;
    }

    /// <summary>Đưa <paramref name="documentId"/> vào Library để Jump tìm thấy, kèm kết quả nạp của nó.</summary>
    public ReadingSessionFixture InLibrary(string documentId)
    {
        Loader.Returns(PathOf(documentId), new LoadedDocument(documentId, [], []));
        Library[documentId] = new LibraryItem(documentId, documentId, PathOf(documentId), ThumbPath: null, PageCount: 10, ImportedAtUnix: 0, LastOpenedAtUnix: 0);
        return this;
    }
}
