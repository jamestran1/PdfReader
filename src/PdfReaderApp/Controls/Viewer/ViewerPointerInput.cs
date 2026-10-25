using System.Windows;
using System.Windows.Input;
using PdfReaderApp.Core;
using PdfReaderApp.Core.Commands;

namespace PdfReaderApp.Controls.Viewer;

/// <summary>
/// Chuột, phím, chọn text và sửa text tại chỗ của viewer. Tự đăng ký event trên canvas tương tác và trình sửa,
/// còn mọi thay đổi lên giao diện đi qua <see cref="IViewerHost"/> dưới dạng yêu cầu. Các method Handle* nhận giá trị
/// thuần (điểm, nút, phím) để test không cần dựng event WPF.
/// </summary>
internal sealed class ViewerPointerInput
{
    private readonly IViewerHost _host;
    private readonly TextSelectionController _selection;
    private PdfObjectManager.GhostText? _activeEditTarget;
    private UIElement? _surface;
    private UIElement? _keyScope;
    private MicroEditor? _editor;

    public ViewerPointerInput(IViewerHost host)
    {
        _host = host;
        _selection = new TextSelectionController(pageIndex => host.Session!.ObjectManager.GetPageTexts(pageIndex));
    }

    /// <summary>Trang (0-based) đang chọn text; -1 khi không có.</summary>
    public int SelectionPageIndex => _selection.PageIndex;

    /// <summary>Vùng chọn theo PDF points của <see cref="SelectionPageIndex"/>.</summary>
    public IReadOnlyList<Rect> SelectionRects => _selection.LineRects;

    /// <summary>Đăng ký event; <paramref name="surface"/> là canvas tương tác, <paramref name="keyScope"/> nhận phím.</summary>
    public void Attach(UIElement surface, UIElement keyScope, MicroEditor editor)
    {
        _surface = surface;
        _keyScope = keyScope;
        _editor = editor;
        surface.MouseDown += OnMouseDown;
        surface.MouseMove += OnMouseMove;
        surface.MouseLeftButtonUp += OnMouseLeftButtonUp;
        keyScope.PreviewKeyDown += OnPreviewKeyDown;
        editor.EditingFinished += OnEditingFinished;
        editor.EditingCancelled += OnEditingCancelled;
    }

    public void Detach()
    {
        if (_surface != null)
        {
            _surface.MouseDown -= OnMouseDown;
            _surface.MouseMove -= OnMouseMove;
            _surface.MouseLeftButtonUp -= OnMouseLeftButtonUp;
        }
        if (_keyScope != null) _keyScope.PreviewKeyDown -= OnPreviewKeyDown;
        if (_editor != null)
        {
            _editor.EditingFinished -= OnEditingFinished;
            _editor.EditingCancelled -= OnEditingCancelled;
        }
    }

    public void HandleMouseDown(Point position, MouseButton button, int clickCount)
    {
        if (_host.IsEditorVisible)
        {
            HideEditor();
        }

        if (clickCount == 2)
        {
            HandleDoubleClick(position);
            return;
        }

        // Bắt đầu chọn text khi single left-down
        if (button == MouseButton.Left && clickCount == 1 && _host.Session is { } session)
        {
            ClearSelection();
            if (TryPageHit(position, out var slot, out var pdf))
            {
                session.ObjectManager.MapPage(session.Document.Pages[slot.PageIndex], slot.PageIndex);
                if (_selection.Begin(slot.PageIndex, pdf))
                    _host.CapturePointer();
            }
        }
    }

    public void HandleMouseMove(Point position)
    {
        if (!_selection.IsSelecting) return;
        if (!TryPageHit(position, out var slot, out var pdf)) return;
        if (!_selection.Extend(slot.PageIndex, pdf)) return;
        _host.HideAddNoteButton();
        _host.RequestRepaint();
    }

    public void HandleMouseLeftButtonUp()
    {
        if (!_selection.IsSelecting) return;
        bool hasSelection = _selection.Finish();
        _host.ReleasePointer();
        if (!hasSelection) return;
        // Đặt nút nổi gần cuối vùng chọn (đổi PDF -> screen của trang đang chọn)
        var slot = _host.Slots.FirstOrDefault(s => s.PageIndex == _selection.PageIndex);
        if (slot == null) return;
        float scale = (float)_host.ZoomLevel;
        var last = _selection.LastLineRect;
        _host.ShowAddNoteButton(new Point(slot.X + last.Right * scale, slot.Y + last.Bottom * scale + 2));
    }

    public void HandleKeyDown(Key key)
    {
        if (key == Key.Escape)
            ClearSelection();
    }

    /// <summary>Đẩy vùng chọn hiện tại cho command "Thêm ghi chú" rồi xoá vùng chọn.</summary>
    public void AddNoteFromSelection()
    {
        var sel = _selection.ToNoteSelection();
        if (sel != null && _host.AddNoteFromSelectionCommand?.CanExecute(sel) == true)
            _host.AddNoteFromSelectionCommand.Execute(sel);
        ClearSelection();
    }

    public void ClearSelection()
    {
        _selection.Clear();
        _host.HideAddNoteButton();
        _host.RequestRepaint();
    }

    public void HandleEditingFinished(string newText)
    {
        if (_activeEditTarget != null && _activeEditTarget.Text != newText)
        {
            var command = new EditTextCommand(_activeEditTarget.PageIndex, _activeEditTarget.CharIndex, _activeEditTarget.Text, newText);
            command.Execute();
            _host.Session?.UndoStack.Push(command); // session có thể đã đóng khi người dùng sửa dở lúc đổi tài liệu

            _activeEditTarget.Text = newText;
        }
        HideEditor();
    }

    public void HandleEditingCancelled() => HideEditor();

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
        => HandleMouseDown(e.GetPosition(_surface), e.ChangedButton, e.ClickCount);

    private void OnMouseMove(object sender, MouseEventArgs e)
        => HandleMouseMove(e.GetPosition(_surface));

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => HandleMouseLeftButtonUp();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        => HandleKeyDown(e.Key);

    private void OnEditingFinished(object? sender, string newText)
        => HandleEditingFinished(newText);

    private void OnEditingCancelled(object? sender, EventArgs e)
        => HandleEditingCancelled();

    private bool TryPageHit(Point screenPoint, out PageSlot slot, out Point pdfPoint)
    {
        if (SlotHitTester.AtPoint(_host.Slots, screenPoint) is { } hit)
        {
            slot = hit;
            pdfPoint = SlotHitTester.ToPdfPoint(hit, screenPoint, _host.ZoomLevel);
            return true;
        }
        slot = default!;
        pdfPoint = default;
        return false;
    }

    private void HandleDoubleClick(Point screenPoint)
    {
        if (_host.Session is not { } session) return;

        float scale = (float)_host.ZoomLevel;

        if (SlotHitTester.AtPoint(_host.Slots, screenPoint) is not { } slot) return;

        var hit = session.ObjectManager.HitTest(slot.PageIndex, SlotHitTester.ToPdfPoint(slot, screenPoint, scale));
        if (hit != null)
        {
            ShowEditor(hit, new Point(slot.X, slot.Y), scale);
        }
    }

    private void ShowEditor(PdfObjectManager.GhostText hit, Point pageOrigin, float scale)
    {
        _activeEditTarget = hit;
        _host.ShowEditor(
            new Point(pageOrigin.X + hit.Bounds.Left * scale - 5, pageOrigin.Y + hit.Bounds.Top * scale - 3),
            new Size(hit.Bounds.Width * scale, hit.Bounds.Height * scale),
            hit.Text);
    }

    private void HideEditor()
    {
        _host.HideEditor();
        _activeEditTarget = null;
    }
}
