using System.IO;
using Microsoft.Extensions.AI;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.Tests.Fakes;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

/// <summary>Ghim hành vi lập chỉ mục nền và lập chỉ mục lại của MainViewModel trước khi tách IndexingViewModel.</summary>
public sealed class MainViewModelIndexingTests : IDisposable
{
    private readonly List<MainViewModel> _viewModels = [];
    private readonly List<string> _tempFiles = [];

    private sealed class FakeSettings : ISettingsService
    {
        public string? GetApiKey() => "key";
        public void SaveApiKey(string apiKey) { }
        public bool HasApiKey() => true;
        public AppTheme GetThemePreference() => AppTheme.Light;
        public void SaveThemePreference(AppTheme theme) { }
    }

    private sealed class FakeChatClientFactory : IChatClientFactory
    {
        public IChatClient Create(string apiKey) => throw new InvalidOperationException("không dùng chat");
    }

    private sealed class FakeDocumentService : IPdfDocumentService
    {
        public void LoadFile(string filePath) { }
        public IReadOnlyList<TextBlock> ExtractStructure() => [];
        public IReadOnlyList<PageText> ExtractPageTexts() => [];
        public IReadOnlyList<MatchRect> FindMatchRects(int pageIndex, string query) => [];
        public void Dispose() { }
    }

    private sealed class ThrowingEmbeddingFactory : IEmbeddingGeneratorFactory
    {
        public IEmbeddingGenerator<string, Embedding<float>> Create(string apiKey)
            => throw new InvalidOperationException("embedding không khả dụng");
    }

    private sealed class RecordingIndex : IDocumentIndex
    {
        public List<string> Deleted { get; } = [];
        public void EnsureSchema() { }
        public DocumentIndexStatus GetStatus(string documentId, string embeddingModel) => DocumentIndexStatus.None;
        public void DeleteDocument(string documentId) => Deleted.Add(documentId);
        public IReadOnlyList<long> WriteChunks(string documentId, string? filePath, int pageCount, IReadOnlyList<Chunk> chunks) => [];
        public void WriteEmbeddings(IReadOnlyList<(long ChunkId, float[] Vector)> embeddings) { }
        public void SetStatus(string documentId, DocumentIndexStatus status, string embeddingModel) { }
        public IReadOnlyList<SearchResult> SearchText(string documentId, string query, int limit = 50) => [];
        public IReadOnlyList<Chunk> RetrieveRelevant(string documentId, float[] queryVector, int k = 5) => [];
        public void Dispose() { }
    }

    private MainViewModel BuildVm(RecordingIndex index)
    {
        var vm = new MainViewModel(
            new FakeDocumentService(),
            new FakeSettings(),
            new FakeChatClientFactory(),
            index,
            new ThrowingEmbeddingFactory(),
            workspaceStore: new FakeWorkspaceStore());
        _viewModels.Add(vm);
        return vm;
    }

    private void OpenFakeDocument(MainViewModel vm)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
        File.WriteAllText(path, "nội dung giả " + Guid.NewGuid());
        _tempFiles.Add(path);
        vm.OpenLibraryItemCommand.Execute(
            new LibraryItem("docIdx", "Sách chỉ mục", path, ThumbPath: null, PageCount: 0, ImportedAtUnix: 1, LastOpenedAtUnix: 1));
    }

    public void Dispose()
    {
        foreach (var vm in _viewModels) vm.Dispose();
        foreach (string path in _tempFiles)
        {
            try { File.Delete(path); }
            catch (IOException) { /* dọn dẹp best-effort */ }
            catch (UnauthorizedAccessException) { /* dọn dẹp best-effort */ }
        }
    }

    [Fact]
    public void IndexingStatus_IsEmpty_BeforeAnyIndexing()
    {
        var vm = BuildVm(new RecordingIndex());

        Assert.Equal(string.Empty, vm.Indexing.StatusText);
    }

    [Fact]
    public void ReindexDocument_WithOpenDocument_DeletesOldIndexFirst()
    {
        var index = new RecordingIndex();
        var vm = BuildVm(index);
        OpenFakeDocument(vm);
        string documentId = vm.CurrentDocumentId!;
        index.Deleted.Clear();

        vm.Indexing.ReindexDocumentCommand.Execute(parameter: null);

        Assert.Equal([documentId], index.Deleted);
    }

    [Fact]
    public void ReindexDocument_WithoutDocument_DoesNothing()
    {
        var index = new RecordingIndex();
        var vm = BuildVm(index);

        vm.Indexing.ReindexDocumentCommand.Execute(parameter: null);

        Assert.Empty(index.Deleted);
    }
}
