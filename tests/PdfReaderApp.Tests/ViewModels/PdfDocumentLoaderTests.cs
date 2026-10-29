using System.IO;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ViewModels;

public class PdfDocumentLoaderTests
{
    private sealed class StubPdfService(IReadOnlyList<TextBlock> blocks, IReadOnlyList<PageText> pages) : IPdfDocumentService
    {
        public string? LoadedPath { get; private set; }
        public void LoadFile(string filePath) => LoadedPath = filePath;
        public IReadOnlyList<TextBlock> ExtractStructure() => blocks;
        public IReadOnlyList<PageText> ExtractPageTexts() => pages;
        public IReadOnlyList<MatchRect> FindMatchRects(int pageIndex, string query) => [];
        public IReadOnlyList<MatchRect> FindPassageRects(int pageIndex, IReadOnlyList<string> fragments) => [];
        public PassageHits FindPassageHits(int pageIndex, IReadOnlyList<string> fragments) => new([], []);
        public void Dispose() { }
    }

    [Fact]
    public void Load_ReturnsStructureAndPageTextsOfSharedService_AndExposesItAsMatchSource()
    {
        var block = new TextBlock("Chữ", 0, 0, 1, 1, 12, 0, "P");
        var pages = new[] { new PageText(0, "Chữ") };
        var service = new StubPdfService([block], pages);
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
        File.WriteAllBytes(path, [1, 2, 3]);
        try
        {
            var loaded = new PdfDocumentLoader(service).Load(path);

            Assert.Equal(path, service.LoadedPath);
            Assert.Equal([block], loaded.Blocks);
            Assert.Equal(pages, loaded.PageTexts);
            Assert.Equal(DocumentId.FromBytes([1, 2, 3]), loaded.DocumentId);
            Assert.Same(service, new PdfDocumentLoader(service).MatchSource);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
