using System.IO;
using System.Windows;
using System.Windows.Input;
using PdfReaderApp.Controls.Viewer;
using PdfReaderApp.Core;
using PdfReaderApp.Models;

namespace PdfReaderApp.Tests.Controls;

public sealed class ViewerPointerInputTests : IDisposable
{
    private readonly PdfDocumentSession _session = PdfDocumentSession.Open(SamplePath());
    private readonly FakeHost _host;
    private readonly ViewerPointerInput _input;

    public ViewerPointerInputTests()
    {
        // Zoom 1 và slot ở gốc toạ độ: điểm màn hình trùng điểm PDF.
        _host = new FakeHost(_session, [new PageSlot(0, 0, 0, 595, 842)], zoom: 1.0);
        _input = new ViewerPointerInput(_host);
        _session.ObjectManager.MapPage(_session.Document.Pages[0], 0);
    }

    public void Dispose() => _session.Dispose();

    private static string SamplePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PdfReaderApp.slnx")))
            directory = directory.Parent;
        if (directory == null)
            throw new InvalidOperationException("Không tìm thấy gốc repo (PdfReaderApp.slnx).");
        return Path.Combine(directory.FullName, "scripts", "assets", "snapshot-sample.pdf");
    }

    private sealed class FakeCommand : ICommand
    {
        public List<object?> Executed { get; } = [];

#pragma warning disable CS0067 // ICommand buộc có event, test không dùng
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => Executed.Add(parameter);
    }

    private sealed class FakeHost(PdfDocumentSession? session, IReadOnlyList<PageSlot> slots, double zoom) : IViewerHost
    {
        public PdfDocumentSession? Session { get; set; } = session;
        public IReadOnlyList<PageSlot> Slots { get; } = slots;
        public double ZoomLevel { get; } = zoom;
        public ICommand? AddNoteFromSelectionCommand { get; set; }
        public bool IsEditorVisible { get; private set; }
        public int Repaints { get; private set; }
        public int Captures { get; private set; }
        public int Releases { get; private set; }
        public string? EditorText { get; private set; }
        public Point? AddNoteButtonAt { get; private set; }

        public void RequestRepaint() => Repaints++;

        public void ShowEditor(Point topLeft, Size size, string text)
        {
            IsEditorVisible = true;
            EditorText = text;
        }

        public Action? OnHideEditor { get; set; }

        public void HideEditor()
        {
            IsEditorVisible = false;
            OnHideEditor?.Invoke();
        }

        public void ShowAddNoteButton(Point topLeft) => AddNoteButtonAt = topLeft;

        public void HideAddNoteButton() => AddNoteButtonAt = null;

        public void CapturePointer() => Captures++;

        public void ReleasePointer() => Releases++;
    }

    private IReadOnlyList<PdfObjectManager.GhostText> PageTexts() => _session.ObjectManager.GetPageTexts(0);

    private static Point Center(PdfObjectManager.GhostText ghost)
        => new(ghost.Bounds.Left + ghost.Bounds.Width / 2, ghost.Bounds.Top + ghost.Bounds.Height / 2);

    private PdfObjectManager.GhostText FirstGlyph() => PageTexts().First(g => g.Bounds.Width > 0 && g.Bounds.Height > 0);

    [Fact]
    public void MouseDown_LeftSingleClickOnText_BeginsSelectionAndCapturesPointer()
    {
        _input.HandleMouseDown(Center(FirstGlyph()), MouseButton.Left, 1);

        Assert.Equal(1, _host.Captures);
        Assert.Equal(0, _input.SelectionPageIndex);
    }

    [Fact]
    public void MouseDown_RightButton_DoesNotBeginSelection()
    {
        _input.HandleMouseDown(Center(FirstGlyph()), MouseButton.Right, 1);

        Assert.Equal(0, _host.Captures);
        Assert.Equal(-1, _input.SelectionPageIndex);
    }

    [Fact]
    public void MouseDown_OutsideAnyPage_DoesNotBeginSelection()
    {
        _input.HandleMouseDown(new Point(-50, -50), MouseButton.Left, 1);

        Assert.Equal(0, _host.Captures);
    }

    [Fact]
    public void MouseDown_WithoutSession_DoesNothing()
    {
        _host.Session = null;

        _input.HandleMouseDown(Center(FirstGlyph()), MouseButton.Left, 1);

        Assert.Equal(0, _host.Captures);
    }

    [Fact]
    public void DragThenRelease_WithTextSelected_ShowsAddNoteButtonAndReleasesPointer()
    {
        var glyphs = PageTexts().Where(g => g.Bounds.Width > 0 && g.Bounds.Height > 0).ToList();
        _input.HandleMouseDown(Center(glyphs[0]), MouseButton.Left, 1);
        _input.HandleMouseMove(Center(glyphs[^1]));

        Assert.NotEmpty(_input.SelectionRects);
        Assert.Null(_host.AddNoteButtonAt);

        _input.HandleMouseLeftButtonUp();

        Assert.Equal(1, _host.Releases);
        Assert.NotNull(_host.AddNoteButtonAt);
    }

    [Fact]
    public void MouseUp_WithoutDragStart_DoesNothing()
    {
        _input.HandleMouseLeftButtonUp();

        Assert.Equal(0, _host.Releases);
        Assert.Null(_host.AddNoteButtonAt);
    }

    [Fact]
    public void KeyDown_Escape_ClearsSelectionAndHidesAddNoteButton()
    {
        var glyphs = PageTexts().Where(g => g.Bounds.Width > 0 && g.Bounds.Height > 0).ToList();
        _input.HandleMouseDown(Center(glyphs[0]), MouseButton.Left, 1);
        _input.HandleMouseMove(Center(glyphs[^1]));
        _input.HandleMouseLeftButtonUp();

        _input.HandleKeyDown(Key.Escape);

        Assert.Equal(-1, _input.SelectionPageIndex);
        Assert.Empty(_input.SelectionRects);
        Assert.Null(_host.AddNoteButtonAt);
    }

    [Fact]
    public void KeyDown_OtherKey_KeepsSelection()
    {
        _input.HandleMouseDown(Center(FirstGlyph()), MouseButton.Left, 1);

        _input.HandleKeyDown(Key.A);

        Assert.Equal(0, _input.SelectionPageIndex);
    }

    [Fact]
    public void AddNoteFromSelection_WithSelection_ExecutesCommandWithNoteSelectionAndClears()
    {
        var command = new FakeCommand();
        _host.AddNoteFromSelectionCommand = command;
        var glyphs = PageTexts().Where(g => g.Bounds.Width > 0 && g.Bounds.Height > 0).ToList();
        _input.HandleMouseDown(Center(glyphs[0]), MouseButton.Left, 1);
        _input.HandleMouseMove(Center(glyphs[^1]));
        _input.HandleMouseLeftButtonUp();

        _input.AddNoteFromSelection();

        Assert.IsType<NoteSelection>(Assert.Single(command.Executed));
        Assert.Equal(-1, _input.SelectionPageIndex);
        Assert.Null(_host.AddNoteButtonAt);
    }

    [Fact]
    public void AddNoteFromSelection_WithoutSelection_DoesNotExecuteCommand()
    {
        var command = new FakeCommand();
        _host.AddNoteFromSelectionCommand = command;

        _input.AddNoteFromSelection();

        Assert.Empty(command.Executed);
    }

    [Fact]
    public void DoubleClick_OnText_ShowsEditorWithThatText()
    {
        var glyph = FirstGlyph();

        _input.HandleMouseDown(Center(glyph), MouseButton.Left, 2);

        Assert.True(_host.IsEditorVisible);
        Assert.Equal(glyph.Text, _host.EditorText);
    }

    [Fact]
    public void DoubleClick_OnEmptyArea_DoesNotShowEditor()
    {
        _input.HandleMouseDown(new Point(-50, -50), MouseButton.Left, 2);

        Assert.False(_host.IsEditorVisible);
    }

    [Fact]
    public void MouseDown_WhileEditorVisible_HidesEditor()
    {
        _input.HandleMouseDown(Center(FirstGlyph()), MouseButton.Left, 2);

        _input.HandleMouseDown(new Point(-50, -50), MouseButton.Left, 1);

        Assert.False(_host.IsEditorVisible);
    }

    [Fact]
    public void EditingFinished_WithNewText_PushesUndoCommandUpdatesGhostAndHidesEditor()
    {
        var glyph = FirstGlyph();
        _input.HandleMouseDown(Center(glyph), MouseButton.Left, 2);

        _input.HandleEditingFinished("#");

        Assert.Single(_session.UndoStack);
        Assert.Equal("#", glyph.Text);
        Assert.False(_host.IsEditorVisible);
    }

    [Fact]
    public void EditingFinished_WithSameText_DoesNotPushUndoCommand()
    {
        var glyph = FirstGlyph();
        _input.HandleMouseDown(Center(glyph), MouseButton.Left, 2);

        _input.HandleEditingFinished(glyph.Text);

        Assert.Empty(_session.UndoStack);
        Assert.False(_host.IsEditorVisible);
    }

    [Fact]
    public void EditingFinished_AfterSessionClosed_DoesNotThrowAndStillUpdatesGhost()
    {
        var glyph = FirstGlyph();
        _input.HandleMouseDown(Center(glyph), MouseButton.Left, 2);
        _host.Session = null;

        _input.HandleEditingFinished("#");

        Assert.Equal("#", glyph.Text);
        Assert.False(_host.IsEditorVisible);
    }

    [Fact]
    public void EditingCancelled_WhenHideEditorRaisesLostFocus_DoesNotCommitEditedText()
    {
        var glyph = FirstGlyph();
        string original = glyph.Text;
        _input.HandleMouseDown(Center(glyph), MouseButton.Left, 2);
        // Giả lập LostFocus bắn đồng bộ khi host ẩn editor và trả focus.
        _host.OnHideEditor = () => _input.HandleEditingFinished("#");

        _input.HandleEditingCancelled();

        Assert.Empty(_session.UndoStack);
        Assert.Equal(original, glyph.Text);
    }

    [Fact]
    public void EditingCancelled_HidesEditorWithoutUndoCommand()
    {
        _input.HandleMouseDown(Center(FirstGlyph()), MouseButton.Left, 2);

        _input.HandleEditingCancelled();

        Assert.False(_host.IsEditorVisible);
        Assert.Empty(_session.UndoStack);
    }
}
