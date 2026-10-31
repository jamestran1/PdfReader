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
        public List<CompositionKind> Kinds { get; } = [];
        public Exception? Failure { get; set; }

        public Task<GeneratedComposition> GenerateAsync(CompositionKind kind, CompositionContext context, CancellationToken cancellationToken)
        {
            Received.Add(context);
            Kinds.Add(kind);
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

    // Generator chờ cổng và tôn trọng token như AiCompositionGenerator thật.
    private sealed class GatedGenerator : ICompositionGenerator
    {
        private readonly TaskCompletionSource<GeneratedComposition> _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HonorsCancellation { get; init; } = true;
        public int Calls { get; private set; }
        public CompositionKind? LastKind { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public void Release() => _gate.SetResult(new GeneratedComposition("T", "C", "m"));

        public Task<GeneratedComposition> GenerateAsync(CompositionKind kind, CompositionContext context, CancellationToken cancellationToken)
        {
            Calls++;
            LastKind = kind;
            LastToken = cancellationToken;
            return HonorsCancellation ? _gate.Task.WaitAsync(cancellationToken) : _gate.Task;
        }
    }

    private static (CompositionsViewModel Vm, FakeCompositionStore Store, List<Composition> Opened, List<string> Errors, List<(Composition, string)> OpenedAfter)
        GatedVm(GatedGenerator generator)
    {
        var store = new FakeCompositionStore();
        var opened = new List<Composition>();
        var errors = new List<string>();
        var openedAfter = new List<(Composition, string)>();
        var vm = new CompositionsViewModel(
            store, new FakeNoteStore(NoteOf("n", "a", 0, "x"), NoteOf("m", "b", 0, "y")), generator,
            () => [new("a", "Sách A"), new("b", "Sách B")], opened.Add, errors.Add,
            openCompositionAfter: (c, after) => openedAfter.Add((c, after)));
        vm.LoadFor("ws");
        return (vm, store, opened, errors, openedAfter);
    }

    [Fact]
    public async Task Generating_ShowsGeneratingState_AndCancelCommandDiscardsTheRequestWithoutSavingOrReportingAnError()
    {
        var generator = new GatedGenerator();
        var (vm, store, opened, errors, _) = GatedVm(generator);

        var running = vm.CreateSummaryCommand.ExecuteAsync(null);
        Assert.True(vm.IsGenerating);

        vm.CancelGenerationCommand.Execute(null);
        await running;

        Assert.True(generator.LastToken.IsCancellationRequested);
        Assert.False(vm.IsGenerating);
        Assert.Empty(store.Rows);
        Assert.Empty(vm.Items);
        Assert.Empty(opened);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task SwitchingWorkspaceWhileGenerating_CancelsTheRequestAndSavesNothingEvenIfTheGeneratorIgnoresTheToken()
    {
        var generator = new GatedGenerator { HonorsCancellation = false };
        var (vm, store, opened, errors, _) = GatedVm(generator);

        var running = vm.CreateSummaryCommand.ExecuteAsync(null);
        vm.LoadFor("ws2");
        Assert.True(generator.LastToken.IsCancellationRequested);
        generator.Release();
        await running;

        Assert.Empty(store.Rows);
        Assert.Empty(vm.Items);
        Assert.Empty(opened);
        Assert.Empty(errors);
        Assert.False(vm.IsGenerating);
    }

    [Fact]
    public void ShowCreateDialog_OpensWithEveryNoteSelected()
    {
        var f = new Fixture(NoteOf("a1", "a", 0, "x"), NoteOf("a2", "a", 1, "y"), NoteOf("b1", "b", 0, "z"));

        f.Vm.ShowCreateDialogCommand.Execute(null);

        Assert.True(f.Vm.IsDialogOpen);
        Assert.Equal(3, f.Vm.Dialog!.Tree.SelectedCount);
        Assert.Empty(f.Generator.Received);
    }

    [Fact]
    public void ShowCreateDialog_WorkspaceWithoutNotes_OpensWithLockedCreate()
    {
        var f = new Fixture();

        f.Vm.ShowCreateDialogCommand.Execute(null);

        Assert.True(f.Vm.IsDialogOpen);
        Assert.False(f.Vm.Dialog!.CanCreate);
        Assert.Equal(CompositionsViewModel.NoNotesMessage, f.Vm.Dialog.WarningText);
    }

    [Fact]
    public void ShowCreateDialog_NoActiveWorkspace_DoesNothing()
    {
        var f = new Fixture(NoteOf("n", "a", 0, "x"));
        f.Vm.LoadFor(null);

        f.Vm.ShowCreateDialogCommand.Execute(null);

        Assert.False(f.Vm.IsDialogOpen);
    }

    [Fact]
    public void DialogCreate_GeneratesFromSelectedNotesOnly_WithChosenKindAndRequest_AndStoresTheRequest()
    {
        var f = new Fixture(NoteOf("a1", "a", 0, "một"), NoteOf("a2", "a", 1, "hai"), NoteOf("b1", "b", 0, "ba"), NoteOf("b2", "b", 1, "bốn"));
        f.Vm.ShowCreateDialogCommand.Execute(null);
        f.Vm.Dialog!.Tree.Documents[1].Notes[1].IsChecked = false;
        f.Vm.Dialog.Kind = CompositionKind.Outline;
        f.Vm.Dialog.AdditionalRequest = "viết bằng tiếng Anh";

        f.Vm.Dialog.CreateCommand.Execute(null);

        Assert.False(f.Vm.IsDialogOpen);
        Assert.Equal(CompositionKind.Outline, f.Generator.Kinds.Single());
        var context = f.Generator.Received.Single();
        Assert.Equal(["a1", "a2", "b1"], context.SourceNoteIds);
        Assert.Equal("viết bằng tiếng Anh", context.AdditionalRequest);
        Assert.DoesNotContain("bốn", context.Text, StringComparison.Ordinal);
        var created = Assert.Single(f.Store.Rows);
        Assert.Equal((CompositionKind.Outline, "viết bằng tiếng Anh"), (created.Kind, created.AdditionalRequest));
        Assert.Equal(["a1", "a2", "b1"], created.SourceNoteIds);
        Assert.Same(created, f.Opened.Single());
    }

    [Fact]
    public void DialogCancel_ClosesWithoutGenerating()
    {
        var f = new Fixture(NoteOf("a1", "a", 0, "x"));
        f.Vm.ShowCreateDialogCommand.Execute(null);

        f.Vm.Dialog!.CancelCommand.Execute(null);

        Assert.False(f.Vm.IsDialogOpen);
        Assert.Empty(f.Generator.Received);
    }

    [Fact]
    public async Task WhileGenerating_CreateAndRegenerateAreLockedUntilItEnds()
    {
        var generator = new GatedGenerator();
        var (vm, _, _, _, _) = GatedVm(generator);
        var existing = new Composition("c1", "ws", "T", CompositionKind.Summary, "x", "m", ["n"], 1);

        Assert.True(vm.CreateSummaryCommand.CanExecute(null));
        Assert.True(vm.RegenerateCommand.CanExecute(existing));

        var running = vm.CreateSummaryCommand.ExecuteAsync(null);
        Assert.False(vm.CreateSummaryCommand.CanExecute(null));
        Assert.False(vm.RegenerateCommand.CanExecute(existing));

        generator.Release();
        await running;
        Assert.True(vm.CreateSummaryCommand.CanExecute(null));
        Assert.True(vm.RegenerateCommand.CanExecute(existing));
    }

    [Fact]
    public async Task Regenerate_CreatesANewCompositionFromTheSameKindAndSourceNotes_OpensItRightOfTheOldOneAndKeepsTheOldOne()
    {
        var f = new Fixture(NoteOf("a1", "a", 0, "A"), NoteOf("b1", "b", 0, "B"), NoteOf("other", "b", 1, "O"));
        var old = new Composition("old", "ws", "Cũ", CompositionKind.Summary, "đã sửa tay", "m", ["a1", "b1"], 1);
        f.Store.Add(old);
        f.Vm.LoadFor("ws");
        List<(Composition, string)> openedAfter = [];
        var vm = new CompositionsViewModel(
            f.Store, new FakeNoteStore(NoteOf("a1", "a", 0, "A"), NoteOf("b1", "b", 0, "B"), NoteOf("other", "b", 1, "O")),
            f.Generator, () => f.Documents, f.Opened.Add, f.Errors.Add, new FixedClock(99),
            (c, after) => openedAfter.Add((c, after)));
        vm.LoadFor("ws");

        await vm.RegenerateCommand.ExecuteAsync(old);

        Assert.Equal(["a1", "b1"], f.Generator.Received.Single().SourceNoteIds);
        var created = vm.Items[0];
        Assert.NotEqual("old", created.Id, StringComparer.Ordinal);
        Assert.Equal((CompositionKind.Summary, 99L, "ws"), (created.Kind, created.CreatedAtUnixMs, created.OwnerKey));
        Assert.Equal("old", openedAfter.Single().Item2);
        Assert.Same(created, openedAfter.Single().Item1);
        Assert.Empty(f.Opened);
        Assert.Equal("đã sửa tay", f.Store.Rows.Single(c => c.Id == "old").Content);
        Assert.Equal(2, f.Store.Rows.Count);
    }

    [Fact]
    public async Task Regenerate_PassesTheOriginalAdditionalRequestAndKeepsTheKind()
    {
        var f = new Fixture(NoteOf("a1", "a", 0, "A"));
        var old = new Composition("old", "ws", "Cũ", CompositionKind.Outline, "x", "m", ["a1"], 1, "viết bằng tiếng Anh");

        await f.Vm.RegenerateCommand.ExecuteAsync(old);

        Assert.Equal(CompositionKind.Outline, f.Generator.Kinds.Single());
        Assert.Equal("viết bằng tiếng Anh", f.Generator.Received.Single().AdditionalRequest);
        var created = f.Store.Rows.Single();
        Assert.Equal((CompositionKind.Outline, "viết bằng tiếng Anh"), (created.Kind, created.AdditionalRequest));
    }

    [Fact]
    public async Task Regenerate_WhenAiFails_ShowsErrorAndSavesNothingNew()
    {
        var f = new Fixture(NoteOf("a1", "a", 0, "A"));
        f.Generator.Failure = new AiChatException(AiChatError.RateLimit, "x");
        var old = new Composition("old", "ws", "Cũ", CompositionKind.Summary, "x", "m", ["a1"], 1);

        await f.Vm.RegenerateCommand.ExecuteAsync(old);

        Assert.Equal([AiErrorMessages.Describe(AiChatError.RateLimit)], f.Errors);
        Assert.Empty(f.Store.Rows);
    }
}
