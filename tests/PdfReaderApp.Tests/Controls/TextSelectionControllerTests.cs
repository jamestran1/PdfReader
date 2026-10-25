using System.Windows;
using PdfReaderApp.Controls.Viewer;
using PdfReaderApp.Core;

namespace PdfReaderApp.Tests.Controls;

public class TextSelectionControllerTests
{
    // Trang 0: hàng 1 "AB" ở y=0, hàng 2 "CD" ở y=20, mỗi ký tự 10x10. Trang 1: "E" ở (0,0).
    private static IReadOnlyList<PdfObjectManager.GhostText> Texts(int page) => page switch
    {
        0 =>
        [
            Ghost(0, 0, "A", new Rect(0, 0, 10, 10)),
            Ghost(0, 1, "B", new Rect(10, 0, 10, 10)),
            Ghost(0, 2, "C", new Rect(0, 20, 10, 10)),
            Ghost(0, 3, "D", new Rect(10, 20, 10, 10)),
        ],
        1 => [Ghost(1, 0, "E", new Rect(0, 0, 10, 10))],
        _ => [],
    };

    private static PdfObjectManager.GhostText Ghost(int page, int index, string text, Rect bounds)
        => new() { PageIndex = page, CharIndex = index, Text = text, Bounds = bounds };

    private static TextSelectionController Create() => new(Texts);

    [Fact]
    public void Begin_OnCharacter_StartsSelectingOnThatPage()
    {
        var c = Create();
        Assert.True(c.Begin(0, new Point(2, 2)));
        Assert.True(c.IsSelecting);
        Assert.Equal(0, c.PageIndex);
        Assert.False(c.HasSelection);
    }

    [Fact]
    public void Begin_OnPageWithoutText_DoesNotStart()
    {
        var c = Create();
        Assert.False(c.Begin(5, new Point(2, 2)));
        Assert.False(c.IsSelecting);
        Assert.Equal(-1, c.PageIndex);
    }

    [Fact]
    public void Extend_AcrossLines_SelectsTextAndNotesRects()
    {
        var c = Create();
        c.Begin(0, new Point(2, 2));
        Assert.True(c.Extend(0, new Point(12, 22)));
        Assert.True(c.Finish());

        var note = c.ToNoteSelection();
        Assert.NotNull(note);
        Assert.Equal("ABCD", note.Quote);
        Assert.Equal(0, note.PageIndex);
        Assert.Equal(2, note.Rects.Count);
        Assert.Equal(c.LineRects[0].Left, note.Rects[0].X);
        Assert.Equal(c.LineRects[0].Top, note.Rects[0].Y);
        Assert.Equal(c.LineRects[0].Width, note.Rects[0].W);
        Assert.Equal(c.LineRects[0].Height, note.Rects[0].H);
        Assert.Equal(c.LineRects[^1], c.LastLineRect);
    }

    [Fact]
    public void Extend_OnAnotherPage_IsIgnored()
    {
        var c = Create();
        c.Begin(0, new Point(2, 2));
        Assert.False(c.Extend(1, new Point(2, 2)));
        Assert.False(c.Finish());
        Assert.Null(c.ToNoteSelection());
    }

    [Fact]
    public void Extend_WithoutBegin_IsIgnored()
    {
        var c = Create();
        Assert.False(c.Extend(0, new Point(2, 2)));
    }

    [Fact]
    public void Finish_ClickWithoutDrag_HasNoSelection()
    {
        var c = Create();
        c.Begin(0, new Point(2, 2));
        Assert.False(c.Finish());
        Assert.False(c.IsSelecting);
    }

    [Fact]
    public void Clear_ResetsEverything()
    {
        var c = Create();
        c.Begin(0, new Point(2, 2));
        c.Extend(0, new Point(12, 2));
        c.Finish();
        c.Clear();

        Assert.False(c.IsSelecting);
        Assert.False(c.HasSelection);
        Assert.Equal(-1, c.PageIndex);
        Assert.Empty(c.LineRects);
        Assert.Null(c.ToNoteSelection());
    }
}
