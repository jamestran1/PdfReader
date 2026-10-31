using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ViewModels;

public sealed class NoteSelectionTreeTests
{
    private static readonly CompositionSourceDocument[] Documents = [new("a", "Sách A"), new("b", "Sách B")];

    private static Note NoteOf(string id, string? doc, int? page, string content, string? quote = null, long created = 0)
        => new(id, "ws", doc, page, quote, content, created, created);

    private static NoteSelectionTree FiveNotes() => new(
        [
            NoteOf("a1", "a", 0, "Ý một"),
            NoteOf("a2", "a", 4, "Ý hai"),
            NoteOf("a3", "a", 9, "Ý ba"),
            NoteOf("b1", "b", 2, "Ý bốn"),
            NoteOf("b2", "b", 3, "Ý năm"),
        ],
        Documents);

    [Fact]
    public void NewTree_SelectsEveryNote_AndCountsPerDocumentAndOverall()
    {
        var tree = FiveNotes();

        Assert.True(tree.IsChecked);
        Assert.Equal((5, 5), (tree.SelectedCount, tree.TotalCount));
        Assert.Equal(["3/3", "2/2"], [.. tree.Documents.Select(d => d.CountText)]);
        Assert.All(tree.Documents, d => Assert.True(d.IsChecked));
    }

    [Fact]
    public void UncheckingOneNote_MakesItsDocumentAndTheRootPartial_AndUpdatesCounts()
    {
        var tree = FiveNotes();

        tree.Documents[0].Notes[1].IsChecked = false;

        Assert.Null(tree.Documents[0].IsChecked);
        Assert.Null(tree.Documents[0].IsChecked);
        Assert.True(tree.Documents[1].IsChecked);
        Assert.Null(tree.IsChecked);
        Assert.Equal("2/3", tree.Documents[0].CountText);
        Assert.Equal(4, tree.SelectedCount);
        Assert.Equal(["a1", "a3", "b1", "b2"], [.. tree.SelectedNotes.Select(n => n.Id)]);
    }

    [Fact]
    public void UncheckingADocument_UnchecksAllItsNotes_AndRecheckingChecksThemAgain()
    {
        var tree = FiveNotes();

        tree.Documents[0].IsChecked = false;
        Assert.All(tree.Documents[0].Notes, n => Assert.False(n.IsChecked));
        Assert.Equal(["b1", "b2"], [.. tree.SelectedNotes.Select(n => n.Id)]);

        tree.Documents[0].IsChecked = true;
        Assert.Equal(5, tree.SelectedCount);
    }

    [Fact]
    public void UncheckingTheRoot_UnchecksEverything_AndPartialRootToggleSelectsAll()
    {
        var tree = FiveNotes();

        tree.IsChecked = false;
        Assert.Equal(0, tree.SelectedCount);
        Assert.False(tree.IsChecked);

        tree.Documents[1].Notes[0].IsChecked = true;
        Assert.Null(tree.IsChecked);

        // Ô ba trạng thái gửi null khi vừa đi qua trạng thái "một phần": coi như chọn hết.
        tree.IsChecked = null;
        Assert.Equal(5, tree.SelectedCount);
        Assert.True(tree.IsChecked);

        // Bấm vào ô đang chọn hết cũng gửi null (true, null, false): phải hiểu là bỏ hết, không kẹt ở "chọn hết".
        tree.IsChecked = null;
        Assert.Equal(0, tree.SelectedCount);
    }

    [Fact]
    public void ClickingACheckedDocumentBox_ThatSendsNull_UnchecksIt()
    {
        var tree = FiveNotes();

        tree.Documents[0].IsChecked = null;

        Assert.False(tree.Documents[0].IsChecked);
        Assert.Equal(2, tree.SelectedCount);
    }

    [Fact]
    public void Filtering_OnlyNarrowsWhatIsShown_AndClearingKeepsEveryTick()
    {
        var tree = FiveNotes();
        tree.Documents[0].Notes[0].IsChecked = false;
        tree.Documents[1].Notes[1].IsChecked = false;

        tree.FilterText = "ba";
        Assert.Equal(["a3"], [.. tree.Documents.SelectMany(d => d.Notes).Where(n => n.IsVisible).Select(n => n.Note.Id)]);
        Assert.False(tree.Documents[1].IsVisible);
        Assert.Equal(3, tree.SelectedCount);

        tree.FilterText = "";
        Assert.All(tree.Documents.SelectMany(d => d.Notes), n => Assert.True(n.IsVisible));
        Assert.All(tree.Documents, d => Assert.True(d.IsVisible));
        Assert.Equal(["a2", "a3", "b1"], [.. tree.SelectedNotes.Select(n => n.Id)]);
    }

    [Fact]
    public void Filter_MatchesQuoteAndDocumentTitle_IgnoringCaseAndDiacritics()
    {
        var tree = new NoteSelectionTree(
            [NoteOf("q", "a", 0, "ghi chú", quote: "Đoạn Trích Dẫn"), NoteOf("z", "b", 0, "khác")],
            Documents);

        tree.FilterText = "doan trich";
        Assert.Equal(["q"], [.. tree.Documents.SelectMany(d => d.Notes).Where(n => n.IsVisible).Select(n => n.Note.Id)]);

        tree.FilterText = "sách b";
        Assert.Equal(["z"], [.. tree.Documents.SelectMany(d => d.Notes).Where(n => n.IsVisible).Select(n => n.Note.Id)]);
    }

    [Fact]
    public void Note_ShowsOneLineOfContentAndItsPage_OrNoPage()
    {
        var tree = new NoteSelectionTree(
            [
                NoteOf("p", "a", 4, "Dòng đầu\nDòng hai"),
                NoteOf("f", null, null, "", quote: "Chỉ có trích dẫn"),
            ],
            Documents);

        var paged = tree.Documents[0].Notes[0];
        var free = tree.Documents[1].Notes[0];

        Assert.Equal(("Dòng đầu", "tr. 5"), (paged.Summary, paged.PageText));
        Assert.Equal(("Chỉ có trích dẫn", "không trang"), (free.Summary, free.PageText));
        Assert.Equal("Note không gắn Document", tree.Documents[1].Title);
    }

    [Fact]
    public void NotesWithoutText_AreNotListed_AndEmptyTreeHasNoSelection()
    {
        var tree = new NoteSelectionTree([NoteOf("blank", "a", 0, "  ")], Documents);

        Assert.Equal(0, tree.TotalCount);
        Assert.Empty(tree.Documents);
        Assert.False(tree.IsChecked);
    }

    [Fact]
    public void SelectionChange_RaisesSelectionChangedOncePerBulkAction()
    {
        var tree = FiveNotes();
        int raised = 0;
        tree.SelectionChanged += (_, _) => raised++;

        tree.Documents[0].IsChecked = false;

        Assert.Equal(1, raised);
    }
}
