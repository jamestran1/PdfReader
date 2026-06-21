using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

/// <summary>
/// Tests that SearchText matches contiguous phrases, not AND-of-words.
/// "kinh hanh" must hit "kinh hanh" but NOT "kinh thanh".
/// </summary>
public class SqliteDocumentIndexPhraseSearchTests : IDisposable
{
    private readonly string _dir;
    private readonly SqliteDocumentIndex _idx;
    private const string DocId = "phrase-doc";

    public SqliteDocumentIndexPhraseSearchTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _idx = new SqliteDocumentIndex(
            Path.Combine(_dir, "t.db"),
            Path.Combine(AppContext.BaseDirectory, "vec0.dll"));
        _idx.EnsureSchema();
        _idx.WriteChunks(DocId, null, 2, new List<Chunk>
        {
            new(DocId, 0, 0, "Đi kinh hành trong sân thiền"),  // page 0
            new(DocId, 1, 0, "Vua ở trong kinh thành lớn")    // page 1
        });
    }

    [Fact]
    public void SearchText_PhraseKinhHanh_FindsPage0_NotPage1()
    {
        var results = _idx.SearchText(DocId, "kinh hành");
        var pages = results.Select(r => r.PageIndex).ToHashSet();
        Assert.Contains(0, pages);
        Assert.DoesNotContain(1, pages);
    }

    [Fact]
    public void SearchText_PhraseKinhThanh_FindsPage1_NotPage0()
    {
        var results = _idx.SearchText(DocId, "kinh thành");
        var pages = results.Select(r => r.PageIndex).ToHashSet();
        Assert.Contains(1, pages);
        Assert.DoesNotContain(0, pages);
    }

    [Fact]
    public void SearchText_UnaccentedKinhHanh_FindsPage0()
    {
        // "kinh hanh" (no accents) must still find page 0
        var results = _idx.SearchText(DocId, "kinh hanh");
        Assert.Contains(results, r => r.PageIndex == 0);
    }

    [Fact]
    public void SearchText_DoubleSpaceKinhHanh_FindsPage0()
    {
        // Extra whitespace in query must collapse and still match the phrase
        var results = _idx.SearchText(DocId, "kinh  hành");
        Assert.Contains(results, r => r.PageIndex == 0);
    }

    [Fact]
    public void SearchText_AbsentPhrase_ReturnsEmpty()
    {
        var results = _idx.SearchText(DocId, "ky sinh trung");
        Assert.Empty(results);
    }

    [Fact]
    public void SearchText_SingleWordThien_FindsPage0()
    {
        // Single-word search must still work after the phrase-search refactor
        var results = _idx.SearchText(DocId, "thiền");
        Assert.Contains(results, r => r.PageIndex == 0);
    }

    public void Dispose()
    {
        _idx.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
