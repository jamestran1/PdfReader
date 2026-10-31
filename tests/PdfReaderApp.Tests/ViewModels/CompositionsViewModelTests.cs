using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ViewModels;

public sealed class CompositionsViewModelTests
{
    private sealed class FakeCompositionStore : ICompositionStore
    {
        public List<Composition> Rows { get; } = [];
        public void EnsureSchema() { }
        public void Add(Composition composition) => Rows.Add(composition);
        public IReadOnlyList<Composition> GetForOwner(string ownerKey)
            => [.. Rows.Where(c => c.OwnerKey == ownerKey).OrderByDescending(c => c.CreatedAtUnixMs)];
        public int Update(string id, string title, string content, long updatedAtUnixMs)
        {
            int i = Rows.FindIndex(c => c.Id == id);
            if (i < 0) return 0;
            Rows[i] = Rows[i] with { Title = title, Content = content, UpdatedAtUnixMs = updatedAtUnixMs };
            return 1;
        }

        public int Delete(string id) => Rows.RemoveAll(c => c.Id == id);
        public int DeleteForOwner(string ownerKey) => Rows.RemoveAll(c => c.OwnerKey == ownerKey);
    }

    private sealed class FakeNoteStore(params Note[] notes) : INoteStore
    {
        public void EnsureSchema() { }
        public void Add(Note note) { }
        public int Update(string id, string content, long nowUnixMs) => 0;
        public int Delete(string id) => 0;
        public IReadOnlyList<Note> GetForOwner(string ownerKey) => [.. notes.Where(n => n.OwnerKey == ownerKey)];
        public int ReassignOwner(string oldKey, string newKey) => 0;
        public int DeleteForOwner(string ownerKey) => 0;
        public int DeleteForDocument(string documentId) => 0;
    }

    private sealed class FakeGenerator : ICompositionGenerator
    {
        public List<CompositionContext> Received { get; } = [];
        public Exception? Failure { get; set; }

        public Task<GeneratedComposition> GenerateAsync(CompositionKind kind, CompositionContext context, CancellationToken cancellationToken)
        {
            Received.Add(context);
            if (Failure is not null) throw Failure;
            return Task.FromResult(new GeneratedComposition("Tiêu đề AI", "Nội dung [1]", "model-x"));
        }
    }

    private sealed class Fixture
    {
        public FakeCompositionStore Store { get; } = new();
        public FakeGenerator Generator { get; } = new();
        public List<Composition> Opened { get; } = [];
        public List<string> Errors { get; } = [];
        public List<CompositionSourceDocument> Documents { get; } = [new("a", "Sách A"), new("b", "Sách B")];
        public CompositionsViewModel Vm { get; }

        public Fixture(params Note[] notes)
        {
            Vm = new CompositionsViewModel(
                Store, new FakeNoteStore(notes), Generator, () => Documents, Opened.Add, Errors.Add,
                new FixedClock(1234));
            Vm.LoadFor("ws");
        }
    }

    private sealed class FixedClock(long unixMs) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
    }

    private static Note NoteOf(string id, string? doc, int? page, string content, long created = 0)
        => new(id, "ws", doc, page, null, content, created, created);

    [Fact]
    public async Task CreateSummary_GeneratesFromAllNotesInOrder_StoresAndOpensAtTopOfRail()
    {
        var f = new Fixture(NoteOf("b1", "b", 0, "B"), NoteOf("a2", "a", 1, "A2"), NoteOf("free", null, null, "F", created: 5));
        f.Vm.Items.Add(new Composition("old", "ws", "Cũ", CompositionKind.Summary, "x", "m", [], 1));

        await f.Vm.CreateSummaryCommand.ExecuteAsync(null);

        Assert.Equal(["a2", "b1", "free"], f.Generator.Received.Single().SourceNoteIds);
        var created = f.Vm.Items[0];
        Assert.Equal(("Tiêu đề AI", "Nội dung [1]", "model-x", CompositionKind.Summary, "ws", 1234L),
            (created.Title, created.Content, created.ChatModel, created.Kind, created.OwnerKey, created.CreatedAtUnixMs));
        Assert.Equal(["a2", "b1", "free"], created.SourceNoteIds);
        Assert.Equal("old", f.Vm.Items[1].Id);
        Assert.Same(created, f.Store.Rows.Single());
        Assert.Same(created, f.Opened.Single());
    }

    [Fact]
    public async Task CreateSummary_WorkspaceWithoutNotes_CreatesNothingAndTellsTheUser()
    {
        var f = new Fixture();

        await f.Vm.CreateSummaryCommand.ExecuteAsync(null);

        Assert.Empty(f.Generator.Received);
        Assert.Empty(f.Store.Rows);
        Assert.Empty(f.Vm.Items);
        Assert.Empty(f.Opened);
        Assert.Equal(["Workspace này chưa có Note nào để làm nguồn. Hãy thêm Note trước khi tạo Bản soạn."], f.Errors);
    }

    [Fact]
    public async Task CreateSummary_NoActiveWorkspace_DoesNothing()
    {
        var f = new Fixture(NoteOf("n", "a", 0, "x"));
        f.Vm.LoadFor(null);

        await f.Vm.CreateSummaryCommand.ExecuteAsync(null);

        Assert.Empty(f.Generator.Received);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public async Task CreateSummary_AiFailure_ShowsErrorAndSavesNothing()
    {
        var f = new Fixture(NoteOf("n", "a", 0, "x"));
        f.Generator.Failure = new AiChatException(AiChatError.RateLimit, "x");

        await f.Vm.CreateSummaryCommand.ExecuteAsync(null);

        Assert.Equal([AiErrorMessages.Describe(AiChatError.RateLimit)], f.Errors);
        Assert.Empty(f.Store.Rows);
        Assert.Empty(f.Vm.Items);
        Assert.Empty(f.Opened);
    }

    [Fact]
    public void LoadFor_ShowsStoredCompositionsNewestFirst_AndSwitchingWorkspaceReloadsThem()
    {
        var f = new Fixture();
        f.Store.Add(new Composition("c-old", "ws", "Cũ", CompositionKind.Summary, "x", "m", [], 10));
        f.Store.Add(new Composition("c-new", "ws", "Mới", CompositionKind.Summary, "x", "m", [], 20));
        f.Store.Add(new Composition("c-other", "ws2", "Khác", CompositionKind.Summary, "x", "m", [], 30));

        f.Vm.LoadFor("ws");
        Assert.Equal(["c-new", "c-old"], [.. f.Vm.Items.Select(c => c.Id)]);

        f.Vm.LoadFor("ws2");
        Assert.Equal(["c-other"], [.. f.Vm.Items.Select(c => c.Id)]);

        f.Vm.LoadFor(null);
        Assert.Empty(f.Vm.Items);
    }

    [Fact]
    public void Open_RaisesOpenCallbackForTheClickedComposition()
    {
        var f = new Fixture();
        var composition = new Composition("c1", "ws", "T", CompositionKind.Summary, "x", "m", [], 1);

        f.Vm.OpenCommand.Execute(composition);

        Assert.Same(composition, f.Opened.Single());
    }

    [Fact]
    public async Task CreateSummary_WorkspaceSwitchedWhileGenerating_StoresButDoesNotShowOrOpenInTheNewWorkspace()
    {
        var f = new Fixture(NoteOf("n", "a", 0, "x"));
        var gate = new TaskCompletionSource<GeneratedComposition>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new CompositionsViewModel(
            f.Store, new FakeNoteStore(NoteOf("n", "a", 0, "x")), new GatedGenerator(gate.Task), () => f.Documents, f.Opened.Add, f.Errors.Add);
        vm.LoadFor("ws");

        var running = vm.CreateSummaryCommand.ExecuteAsync(null);
        vm.LoadFor("ws2");
        gate.SetResult(new GeneratedComposition("T", "C", "m"));
        await running;

        Assert.Single(f.Store.GetForOwner("ws"));
        Assert.Empty(vm.Items);
        Assert.Empty(f.Opened);
    }

    private sealed class GatedGenerator(Task<GeneratedComposition> result) : ICompositionGenerator
    {
        public Task<GeneratedComposition> GenerateAsync(CompositionKind kind, CompositionContext context, CancellationToken cancellationToken) => result;
    }
}
