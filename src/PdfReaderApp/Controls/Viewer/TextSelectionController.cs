using System.Windows;
using PdfReaderApp.Core;
using PdfReaderApp.Models;

namespace PdfReaderApp.Controls.Viewer;

/// <summary>
/// State chọn text trên một trang: điểm bắt đầu, kéo, kết thúc và đổi sang <see cref="NoteSelection"/>.
/// Chỉ giữ state và phép tính; control vẫn ghi DP, hiện nút "Thêm ghi chú" và vẽ overlay (CS-1).
/// </summary>
internal sealed class TextSelectionController
{
    private readonly Func<int, IReadOnlyList<PdfObjectManager.GhostText>> _pageTexts;
    private readonly List<Rect> _lineRects = []; // rect theo PDF points của PageIndex
    private int _anchorChar = -1;
    private string _text = string.Empty;

    public TextSelectionController(Func<int, IReadOnlyList<PdfObjectManager.GhostText>> pageTexts)
    {
        _pageTexts = pageTexts;
    }

    /// <summary>Trang (0-based) đang chọn; -1 khi không có.</summary>
    public int PageIndex { get; private set; } = -1;

    /// <summary>Đang giữ chuột kéo chọn.</summary>
    public bool IsSelecting { get; private set; }

    /// <summary>Có vùng chọn không rỗng để hiện nút ghi chú.</summary>
    public bool HasSelection => !string.IsNullOrEmpty(_text) && _lineRects.Count > 0;

    public IReadOnlyList<Rect> LineRects => _lineRects;

    /// <summary>Rect của dòng chọn cuối cùng (PDF points); chỉ gọi khi <see cref="HasSelection"/>.</summary>
    public Rect LastLineRect => _lineRects[^1];

    /// <summary>Bắt đầu chọn tại điểm PDF; false nếu không có ký tự nào gần điểm đó.</summary>
    public bool Begin(int pageIndex, Point pdfPoint)
    {
        int anchor = TextSelectionResolver.NearestCharIndex(BuildChars(pageIndex), pdfPoint);
        if (anchor < 0) return false;
        PageIndex = pageIndex;
        _anchorChar = anchor;
        IsSelecting = true;
        return true;
    }

    /// <summary>Kéo tới điểm PDF; false (không đổi gì) nếu chưa chọn, sai trang hoặc không có ký tự gần điểm.</summary>
    public bool Extend(int pageIndex, Point pdfPoint)
    {
        if (!IsSelecting || PageIndex < 0 || pageIndex != PageIndex) return false;
        var chars = BuildChars(PageIndex);
        int focus = TextSelectionResolver.NearestCharIndex(chars, pdfPoint);
        if (focus < 0) return false;
        var res = TextSelectionResolver.Resolve(chars, _anchorChar, focus);
        _text = res.Text;
        _lineRects.Clear();
        _lineRects.AddRange(res.LineRects);
        return true;
    }

    /// <summary>Nhả chuột; trả về true nếu có vùng chọn để hiện nút ghi chú.</summary>
    public bool Finish()
    {
        IsSelecting = false;
        return HasSelection;
    }

    public void Clear()
    {
        IsSelecting = false;
        PageIndex = -1;
        _anchorChar = -1;
        _text = string.Empty;
        _lineRects.Clear();
    }

    public NoteSelection? ToNoteSelection()
    {
        if (string.IsNullOrEmpty(_text) || PageIndex < 0) return null;
        var rects = _lineRects
            .Select(r => new HighlightRect(r.Left, r.Top, r.Width, r.Height))
            .ToList();
        return new NoteSelection(_text, PageIndex, rects);
    }

    private List<SelChar> BuildChars(int pageIndex)
    {
        var list = new List<SelChar>();
        foreach (var g in _pageTexts(pageIndex))
            list.Add(new SelChar(g.CharIndex, g.Text, g.Bounds));
        return list;
    }
}
