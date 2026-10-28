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
            FindLibraryItem: id => Library.FirstOrDefault(i => i.DocumentId == id),
            MarkDocumentOpened: RecordOpened));
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
    public List<LibraryItem> Library { get; } = [];
    public ReadingSessionViewModel Session { get; }

    public static string PathOf(string documentId) => $"/docs/{documentId}.pdf";

    /// <summary>Thêm Document vào Library giả (đường dẫn <see cref="PathOf"/>) và đăng ký cho bộ nạp.</summary>
    public LibraryItem AddToLibrary(string documentId)
    {
        var item = new LibraryItem(documentId, documentId, PathOf(documentId), null, 1, 0, 0);
        Library.Add(item);
        Loader.Returns(item.StoredPath, new LoadedDocument(documentId, [], []));
        return item;
    }

    /// <summary>Vào named Workspace <see cref="WorkspaceId"/> (đến khi các intent mở Workspace chuyển vào Phiên đọc).</summary>
    public ReadingSessionFixture InWorkspace()
    {
        Session.ActiveWorkspaceId = WorkspaceId;
        Session.IsWorkspaceSession = true;
        return this;
    }

    /// <summary>Mở một Tab cho <paramref name="documentId"/> và kích hoạt nó; xoá nhật ký để test chỉ thấy phần sau đó.</summary>
    public OpenTab OpenTab(string documentId, int page = 1)
    {
        Loader.Returns(PathOf(documentId), new LoadedDocument(documentId, [], []));
        var tab = Session.Tabs.OpenOrActivate(documentId, documentId, PathOf(documentId), page);
        Log.Clear();
        return tab;
    }
}
