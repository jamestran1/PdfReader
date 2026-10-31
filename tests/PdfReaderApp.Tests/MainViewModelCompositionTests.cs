using System.IO;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.Tests.Fakes;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

/// <summary>Đường xuyên suốt Bản soạn ở Workspace có tên (#291): rail, sinh, lưu bền, mở thành Tab Bản soạn.</summary>
public sealed class MainViewModelCompositionTests : IDisposable
{
    private sealed class MemoryCompositionStore : ICompositionStore
    {
        private readonly List<Composition> _rows = [];
        public void EnsureSchema() { }
        public void Add(Composition composition) => _rows.Add(composition);
        public IReadOnlyList<Composition> GetForOwner(string ownerKey)
            => [.. _rows.Where(c => c.OwnerKey == ownerKey).OrderByDescending(c => c.CreatedAtUnixMs)];
        public int Update(string id, string title, string content, long updatedAtUnixMs)
        {
            int i = _rows.FindIndex(c => c.Id == id);
            if (i < 0) return 0;
            _rows[i] = _rows[i] with { Title = title, Content = content, UpdatedAtUnixMs = updatedAtUnixMs };
            return 1;
        }

        public int Delete(string id) => _rows.RemoveAll(c => c.Id == id);
        public int DeleteForOwner(string ownerKey) => _rows.RemoveAll(c => c.OwnerKey == ownerKey);
    }

    private sealed class MemoryNoteStore : INoteStore
    {
        public List<Note> Rows { get; } = [];
        public void EnsureSchema() { }
        public void Add(Note note) => Rows.Add(note);
        public int Update(string id, string content, long nowUnixMs) => 0;
        public int Delete(string id) => 0;
        public IReadOnlyList<Note> GetForOwner(string ownerKey) => [.. Rows.Where(n => n.OwnerKey == ownerKey)];
        public int ReassignOwner(string oldKey, string newKey) => 0;
        public int DeleteForOwner(string ownerKey) => Rows.RemoveAll(n => n.OwnerKey == ownerKey);
        public int DeleteForDocument(string documentId) => 0;
    }

    private sealed class RecordingGenerator : ICompositionGenerator
    {
        public List<CompositionContext> Received { get; } = [];

        public Task<GeneratedComposition> GenerateAsync(CompositionKind kind, CompositionContext context, CancellationToken cancellationToken)
        {
            Received.Add(context);
            return Task.FromResult(new GeneratedComposition("Tóm tắt AI", "Nội dung [1]", "model-x"));
        }
    }

    private readonly MemoryCompositionStore _compositions = new();
    private readonly MemoryNoteStore _notes = new();
    private readonly RecordingGenerator _generator = new();
    private readonly FakeWorkspaceStore _workspaces = new();
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly MainViewModel _vm;
    private readonly Workspace _ws = new("ws-1", "Dự án 1", false, null, 1, 1);

    public MainViewModelCompositionTests()
    {
        // Dữ liệu mặc định (Library, chat, index) nằm trong thư mục tạm; chỉ các store liên quan được thay bằng bộ nhớ.
        _vm = new MainViewModel(DefaultServices.Create(_dataDir) with
        {
            NoteStore = _notes,
            WorkspaceStore = _workspaces,
            DocumentLoader = new FakeDocumentLoader(),
            CompositionStore = _compositions,
            CompositionGenerator = _generator,
        });

        _workspaces.Upsert(_ws);
        foreach (string id in new[] { "docA", "docB" })
        {
            _workspaces.AddDocument(_ws.Id, id);
            _vm.Library.Add(new LibraryItem(id, $"Sách {id}", $"/{id}.pdf", null, 0, 1, 1));
        }
    }

    public void Dispose()
    {
        _vm.Dispose();
#pragma warning disable CA1031 // dọn thư mục tạm best-effort
        try { Directory.Delete(_dataDir, true); } catch (Exception) { /* SQLite có thể còn giữ file; hệ điều hành sẽ dọn */ }
#pragma warning restore CA1031
    }

    private void AddNote(string id, string? doc, int? page, string content)
        => _notes.Add(new Note(id, _ws.Id, doc, page, null, content, 1, 1));

    [Fact]
    public async Task CreateSummary_GeneratesFromAllNotes_ShowsOnRailAndOpensActiveCompositionTab()
    {
        AddNote("nB", "docB", 0, "ý B");
        AddNote("nA", "docA", 4, "ý A");
        _vm.OpenWorkspaceCommand.Execute(_ws);

        await _vm.Compositions.CreateSummaryCommand.ExecuteAsync(null);

        Assert.Equal(["nA", "nB"], _generator.Received.Single().SourceNoteIds);
        var composition = Assert.Single(_vm.Compositions.Items);
        Assert.Equal("Tóm tắt AI", composition.Title);
        var tab = Assert.IsType<CompositionTab>(_vm.Session.Tabs.ActiveTab);
        Assert.Equal(composition.Id, tab.CompositionId);
        Assert.True(_vm.Session.IsCompositionTabActive);
        Assert.False(_vm.IsSearchAvailable);
    }

    [Fact]
    public async Task Composition_SurvivesSwitchingWorkspaceAndBack_WithAllFields()
    {
        AddNote("nA", "docA", 0, "ý A");
        _vm.OpenWorkspaceCommand.Execute(_ws);
        await _vm.Compositions.CreateSummaryCommand.ExecuteAsync(null);
        var created = _vm.Compositions.Items[0];

        var other = new Workspace("ws-2", "Khác", false, null, 1, 1);
        _workspaces.Upsert(other);
        _vm.OpenWorkspaceCommand.Execute(other);
        Assert.Empty(_vm.Compositions.Items);

        _vm.OpenWorkspaceCommand.Execute(_ws);

        var restored = Assert.Single(_vm.Compositions.Items);
        Assert.Equal(created with { SourceNoteIds = [] }, restored with { SourceNoteIds = [] });
        Assert.Equal(["nA"], restored.SourceNoteIds);
        Assert.Equal(("Tóm tắt AI", "Nội dung [1]", "model-x", CompositionKind.Summary), (restored.Title, restored.Content, restored.ChatModel, restored.Kind));
        Assert.True(restored.CreatedAtUnixMs > 0);
    }

    [Fact]
    public async Task OpeningARailItemThatIsAlreadyATab_ActivatesItWithoutDuplicate()
    {
        AddNote("nA", "docA", 0, "ý A");
        _vm.OpenWorkspaceCommand.Execute(_ws);
        await _vm.Compositions.CreateSummaryCommand.ExecuteAsync(null);
        var composition = _vm.Compositions.Items[0];
        var documentTab = _vm.Session.Tabs.Tabs.OfType<DocumentTab>().First();
        _vm.ActivateTabCommand.Execute(documentTab);

        _vm.Compositions.OpenCommand.Execute(composition);

        Assert.Single(_vm.Session.Tabs.Tabs.OfType<CompositionTab>());
        Assert.IsType<CompositionTab>(_vm.Session.Tabs.ActiveTab);
        Assert.False(_vm.IsSearchAvailable);
    }

    [Fact]
    public async Task CreateSummary_WorkspaceWithoutNotes_CreatesNothingAndShowsMessage()
    {
        _vm.OpenWorkspaceCommand.Execute(_ws);

        await _vm.Compositions.CreateSummaryCommand.ExecuteAsync(null);

        Assert.Empty(_vm.Compositions.Items);
        Assert.Empty(_vm.Session.Tabs.Tabs.OfType<CompositionTab>());
        Assert.Equal(CompositionsViewModel.NoNotesMessage, _vm.Notifications.Current?.Message);
    }

    [Fact]
    public async Task ActivatingCompositionTab_KeepsNotesAndChatAnchoredToTheDocument()
    {
        AddNote("nA", "docA", 0, "ý A");
        _vm.OpenWorkspaceCommand.Execute(_ws);
        _vm.OpenWorkspaceDocumentCommand.Execute(_vm.Library[0]);
        string? documentBefore = _vm.Session.DocumentId;

        await _vm.Compositions.CreateSummaryCommand.ExecuteAsync(null);

        Assert.NotNull(documentBefore);
        Assert.Equal(documentBefore, _vm.Session.DocumentId);
    }

    [Fact]
    public void DeleteWorkspace_RemovesItsCompositions()
    {
        _compositions.Add(new Composition("c1", _ws.Id, "T", CompositionKind.Summary, "x", "m", [], 1));

        _vm.DeleteWorkspaceCommand.Execute(_ws);

        Assert.Empty(_compositions.GetForOwner(_ws.Id));
    }

    private CompositionTab OpenCompositionTab(string content = "Nội dung [1]")
    {
        _vm.OpenWorkspaceCommand.Execute(_ws);
        var composition = new Composition("c1", _ws.Id, "T", CompositionKind.Summary, content, "m", ["n1"], 1);
        _compositions.Add(composition);
        _vm.Compositions.LoadFor(_ws.Id);
        _vm.Compositions.OpenCommand.Execute(_vm.Compositions.Items.Single());
        return Assert.IsType<CompositionTab>(_vm.Session.Tabs.ActiveTab);
    }

    [Fact]
    public void ClosingATabWhileEditing_SavesTheTypedTextWithoutWaitingForAutoSave()
    {
        var tab = OpenCompositionTab();
        _vm.Compositions.ToggleEditCommand.Execute(tab);
        tab.EditText = "Gõ rồi đóng ngay";

        _vm.CloseTabCommand.Execute(tab);

        Assert.Equal("Gõ rồi đóng ngay", _compositions.GetForOwner(_ws.Id).Single().Content);
    }

    [Fact]
    public void SwitchingToAnotherTabWhileEditing_SavesTheTypedText()
    {
        var tab = OpenCompositionTab();
        _vm.Compositions.ToggleEditCommand.Execute(tab);
        tab.EditText = "Gõ rồi chuyển tab";

        _vm.Session.Activate(_vm.Session.OpenComposition(new Composition("c2", _ws.Id, "T2", CompositionKind.Summary, "y", "m", [], 2)));

        Assert.Equal("Gõ rồi chuyển tab", _compositions.GetForOwner(_ws.Id).Single(c => c.Id == "c1").Content);
    }

    [Fact]
    public void DeletingAnOpenComposition_ClosesItsTabAndActivatesThePreviousTab()
    {
        var tab = OpenCompositionTab();
        var other = _vm.Session.OpenComposition(new Composition("c2", _ws.Id, "T2", CompositionKind.Summary, "y", "m", [], 2));
        _vm.Session.Activate(tab);
        _vm.Compositions.ConfirmDelete = _ => true;

        _vm.Compositions.DeleteCommand.Execute(tab.Composition);

        Assert.Empty(_compositions.GetForOwner(_ws.Id));
        Assert.DoesNotContain(tab, _vm.Session.Tabs.Tabs);
        Assert.Same(other, _vm.Session.Tabs.ActiveTab);
    }
}
