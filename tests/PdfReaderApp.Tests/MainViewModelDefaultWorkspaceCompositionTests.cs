using System.IO;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.Tests.Fakes;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

/// <summary>Bản soạn khi đọc lẻ (Default Workspace, #297): rail, sinh từ Note, Tab Strip nhẹ, đổi Document, promote.</summary>
public sealed class MainViewModelDefaultWorkspaceCompositionTests : IDisposable
{
    // Sinh xong ngay, hoặc treo đến khi bị huỷ (BlockUntilCancelled) để thử huỷ khi đổi Document.
    private sealed class ScriptedGenerator : ICompositionGenerator
    {
        public bool BlockUntilCancelled { get; set; }
        public TaskCompletionSource Started { get; } = new();
        public CancellationToken Token { get; private set; }

        public async Task<GeneratedComposition> GenerateAsync(CompositionKind kind, CompositionContext context, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Started.TrySetResult();
            if (BlockUntilCancelled) await Task.Delay(Timeout.Infinite, cancellationToken);
            return new GeneratedComposition("Tóm tắt", "Nội dung", "m");
        }
    }

    private readonly MainViewModelCompositionTests.MemoryCompositionStore _compositions = new();
    private readonly MainViewModelCompositionTests.MemoryNoteStore _notes = new();
    private readonly FakeWorkspaceStore _workspaces = new();
    private readonly ScriptedGenerator _generator = new();
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly MainViewModel _vm;

    public MainViewModelDefaultWorkspaceCompositionTests()
    {
        _vm = new MainViewModel(DefaultServices.Create(_dataDir) with
        {
            NoteStore = _notes,
            WorkspaceStore = _workspaces,
            DocumentLoader = new FakeDocumentLoader(),
            CompositionStore = _compositions,
            CompositionGenerator = _generator,
        });
        foreach (string id in new[] { "docA", "docB" })
            _vm.Library.Add(new LibraryItem(id, $"Sách {id}", $"/{id}.pdf", null, 0, 1, 1));
    }

    public void Dispose()
    {
        _vm.Dispose();
#pragma warning disable CA1031 // dọn thư mục tạm best-effort
        try { Directory.Delete(_dataDir, true); } catch (Exception) { /* SQLite có thể còn giữ file */ }
#pragma warning restore CA1031
    }

    private string ReadStandalone(int libraryIndex)
    {
        _vm.OpenLibraryItemCommand.Execute(_vm.Library[libraryIndex]);
        return _vm.Session.ActiveWorkspaceId!;
    }

    [Fact]
    public void ReadingStandalone_RailShowsCompositionsOfTheDefaultWorkspace_AndSwitchesWithTheDocument()
    {
        string wsA = ReadStandalone(0);
        _compositions.Add(new Composition("cA", wsA, "Của A", CompositionKind.Summary, "x", "m", [], 1));

        string wsB = ReadStandalone(1);
        Assert.Empty(_vm.Compositions.Items);
        Assert.Equal(wsB, _vm.Compositions.OwnerKey);

        ReadStandalone(0);
        Assert.Equal("cA", Assert.Single(_vm.Compositions.Items).Id);
        Assert.True(_vm.IsRailVisible);
    }

    [Fact]
    public async Task CreateSummary_WhileReadingStandalone_UsesNotesOfTheDefaultWorkspaceAndShowsLightTabStrip()
    {
        string ws = ReadStandalone(0);
        _notes.Add(new Note("n1", ws, "/docA.pdf", 0, null, "ý A", 1, 1));
        Assert.False(_vm.IsTabStripVisible);

        await _vm.Compositions.CreateSummaryCommand.ExecuteAsync(null);

        var composition = Assert.Single(_vm.Compositions.Items);
        Assert.Equal(ws, composition.OwnerKey);
        Assert.IsType<CompositionTab>(_vm.Session.Tabs.ActiveTab);
        Assert.Equal(2, _vm.Session.Tabs.Tabs.Count);
        Assert.True(_vm.IsTabStripVisible);
        Assert.False(_vm.Session.IsWorkspaceSession);
    }

    [Fact]
    public async Task OpeningAnotherDocument_CancelsRunningGenerationAndSavesNothing()
    {
        string wsA = ReadStandalone(0);
        _notes.Add(new Note("n1", wsA, "/docA.pdf", 0, null, "ý A", 1, 1));
        _generator.BlockUntilCancelled = true;
        var running = _vm.Compositions.CreateSummaryCommand.ExecuteAsync(null);
        await _generator.Started.Task;

        ReadStandalone(1);
        await running;

        Assert.True(_generator.Token.IsCancellationRequested);
        Assert.False(_vm.Compositions.IsGenerating);
        Assert.Empty(_vm.Compositions.Items);
        Assert.Empty(_compositions.GetForOwner(wsA));
        Assert.Empty(_vm.Session.Tabs.Tabs);
        Assert.False(_vm.IsTabStripVisible);
    }

    [Fact]
    public async Task Promote_KeepsCompositionInDefaultWorkspace_AndNewWorkspaceRailIsEmpty()
    {
        string defaultWs = ReadStandalone(0);
        _notes.Add(new Note("n1", defaultWs, "/docA.pdf", 0, null, "ý A", 1, 1));
        await _vm.Compositions.CreateSummaryCommand.ExecuteAsync(null);

        _vm.PromoteToWorkspaceCommand.Execute("Luận văn");

        Assert.True(_vm.Session.IsWorkspaceSession);
        Assert.NotEqual(defaultWs, _vm.Session.ActiveWorkspaceId, StringComparer.Ordinal);
        Assert.Empty(_vm.Compositions.Items);
        Assert.Single(_vm.Session.Tabs.Tabs);
        Assert.Single(_compositions.GetForOwner(defaultWs));
    }
}
