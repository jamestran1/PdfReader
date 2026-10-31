using PdfReaderApp.Core;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ViewModels;

/// <summary>[n] bấm được và danh sách nguồn của Tab Bản soạn (#295).</summary>
public sealed class CompositionSourcesViewModelTests
{
    private sealed class FakeNoteStore(params Note[] notes) : INoteStore
    {
        public List<Note> Rows { get; } = [.. notes];
        public void EnsureSchema() { }
        public void Add(Note note) => Rows.Add(note);
        public int Update(string id, string content, long nowUnixMs) => 0;
        public int Delete(string id) => Rows.RemoveAll(n => n.Id == id);
        public IReadOnlyList<Note> GetForOwner(string ownerKey) => [.. Rows.Where(n => n.OwnerKey == ownerKey)];
        public int ReassignOwner(string oldKey, string newKey) => 0;
        public int DeleteForOwner(string ownerKey) => 0;
        public int DeleteForDocument(string documentId) => 0;
    }

    private static Note NoteOf(string id, string? docId, int? page) => new(id, "ws", docId, page, null, $"nội dung {id}", 1, 1);

    private sealed class Fixture
    {
        public FakeNoteStore Notes { get; }
        public List<CompositionSourceEntry> DocumentJumps { get; } = [];
        public List<CompositionSourceEntry> FreeSelections { get; } = [];
        public CompositionSourcesViewModel Vm { get; }

        public Fixture(params Note[] notes)
        {
            Notes = new FakeNoteStore(notes);
            Vm = new CompositionSourcesViewModel(Notes, id => id == "docB" ? "Báo cáo" : "Hợp đồng", DocumentJumps.Add, FreeSelections.Add);
        }

        public static CompositionTab TabWith(string content, params string[] noteIds)
            => new(new Composition("c1", "ws", "Tóm tắt", CompositionKind.Summary, content, "model-x", noteIds, 0));

        public static CitationSpan LinkOf(CompositionTab tab, string label)
            => tab.RenderedBlocks.SelectMany(b => b.Spans).First(s => s.Source is not null && s.Text == label);
    }

    [Fact]
    public void Refresh_FillsSourcesAndTurnsNumbersIntoLinks()
    {
        var f = new Fixture(NoteOf("n1", "docA", 3), NoteOf("n2", "docB", 6));
        var tab = Fixture.TabWith("Ý chính [2].", "n1", "n2");

        f.Vm.Refresh(tab);

        Assert.Equal(2, tab.Sources.Count);
        Assert.Equal("Báo cáo, Tr.7", tab.Sources[1].Location);
        Assert.NotNull(Fixture.LinkOf(tab, "[2]"));
    }

    [Fact]
    public void Refresh_DeletedNote_ListedAsDeleted_ContentUnchanged_NumberNotClickable()
    {
        var f = new Fixture(NoteOf("n1", "docA", 3));
        var tab = Fixture.TabWith("Có [1] và [2].", "n1", "gone");

        f.Vm.Refresh(tab);

        Assert.True(tab.Sources[1].IsDeleted);
        Assert.Equal(CompositionSources.DeletedNoteText, tab.Sources[1].Content);
        Assert.DoesNotContain(tab.RenderedBlocks.SelectMany(b => b.Spans), s => s.Source is not null && s.Text == "[2]");
        Assert.Equal("Có [1] và [2].", tab.Composition.Content);
    }

    [Fact]
    public void Refresh_AfterNoteDeleted_NumberStopsBeingClickable()
    {
        var f = new Fixture(NoteOf("n1", "docA", 3));
        var tab = Fixture.TabWith("Có [1].", "n1");
        f.Vm.Refresh(tab);
        f.Notes.Delete("n1");

        f.Vm.Refresh(tab);

        Assert.DoesNotContain(tab.RenderedBlocks.SelectMany(b => b.Spans), s => s.Source is not null);
    }

    [Fact]
    public void OpenCitation_DocumentNote_AsksHostToJumpToDocumentPage()
    {
        var f = new Fixture(NoteOf("n2", "docB", 6));
        var tab = Fixture.TabWith("Ý [1].", "n2");
        f.Vm.Refresh(tab);

        f.Vm.OpenCitationCommand.Execute(Fixture.LinkOf(tab, "[1]"));

        var jump = Assert.Single(f.DocumentJumps);
        Assert.Equal("docB", jump.DocumentId);
        Assert.Equal(6, jump.PageIndex);
        Assert.Empty(f.FreeSelections);
    }

    [Fact]
    public void OpenCitation_EditedNote_JumpsToCurrentNote()
    {
        var f = new Fixture(NoteOf("n1", "docA", 3));
        var tab = Fixture.TabWith("Ý [1].", "n1");
        f.Vm.Refresh(tab);
        f.Notes.Rows[0] = f.Notes.Rows[0] with { PageIndex = 9, Content = "đã sửa" };
        f.Vm.Refresh(tab);

        f.Vm.OpenCitationCommand.Execute(Fixture.LinkOf(tab, "[1]"));

        Assert.Equal(9, Assert.Single(f.DocumentJumps).PageIndex);
        Assert.Equal("đã sửa", tab.Sources[0].Content);
    }

    [Fact]
    public void OpenCitation_FreeNote_SelectsItInNotesPanelWithoutJumping()
    {
        var f = new Fixture(NoteOf("n3", null, null));
        var tab = Fixture.TabWith("Ý [1].", "n3");
        f.Vm.Refresh(tab);

        f.Vm.OpenCitationCommand.Execute(Fixture.LinkOf(tab, "[1]"));

        Assert.Equal("n3", Assert.Single(f.FreeSelections).NoteId);
        Assert.Empty(f.DocumentJumps);
    }

    [Fact]
    public void RenderedBlocks_BeforeRefresh_ShowNumbersAsPlainText()
    {
        var tab = Fixture.TabWith("Ý [1].", "n1");

        var block = Assert.Single(tab.RenderedBlocks);
        Assert.All(block.Spans, s => Assert.Null(s.Source));
        Assert.Equal("Ý [1].", string.Concat(block.Spans.Select(s => s.Text)));
    }

    [Fact]
    public void GeneratedText_ShowsChatModel()
    {
        var tab = Fixture.TabWith("x");

        Assert.StartsWith("Sinh bởi model-x lúc ", tab.GeneratedText, StringComparison.Ordinal);
    }
}
