using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.Fakes;

/// <summary>
/// Bộ nạp Document giả: không mở PDF nào. Mặc định trả một <see cref="LoadedDocument"/> rỗng có DocumentId
/// bằng chính đường dẫn; test có thể đặt kết quả riêng theo đường dẫn hoặc bắt nó ném lỗi.
/// </summary>
internal sealed class FakeDocumentLoader : IDocumentLoader
{
    private readonly Dictionary<string, LoadedDocument> _byPath = [];

    public List<string> LoadedPaths { get; } = [];

    public Exception? ThrowOnLoad { get; set; }

    public IPdfDocumentService MatchSource { get; init; } = new NoMatchSource();

    public void Returns(string path, LoadedDocument document) => _byPath[path] = document;

    public LoadedDocument Load(string path)
    {
        LoadedPaths.Add(path);
        if (ThrowOnLoad is not null) throw ThrowOnLoad;
        return _byPath.TryGetValue(path, out var document) ? document : new LoadedDocument(path, [], []);
    }

    private sealed class NoMatchSource : IPdfDocumentService
    {
        public void LoadFile(string path) { }
        public IReadOnlyList<TextBlock> ExtractStructure() => [];
        public IReadOnlyList<PageText> ExtractPageTexts() => [];
        public IReadOnlyList<MatchRect> FindMatchRects(int pageIndex, string query) => [];
        public IReadOnlyList<MatchRect> FindPassageRects(int pageIndex, IReadOnlyList<string> fragments) => [];
        public PassageHits FindPassageHits(int pageIndex, IReadOnlyList<string> fragments) => new([], [.. fragments.Select(_ => false)]);
        public void Dispose() { }
    }
}
