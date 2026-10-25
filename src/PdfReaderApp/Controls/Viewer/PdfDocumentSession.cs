using System.IO;
using System.Windows;
using PdfiumViewer.Core;
using PdfReaderApp.Core;
using PdfReaderApp.Core.Commands;
using PdfReaderApp.Services;

namespace PdfReaderApp.Controls.Viewer;

/// <summary>
/// Sở hữu mọi thứ gắn với một tài liệu PDF đang mở trong viewer: document PDFium, vùng ảnh,
/// ghost mapping và ngăn undo. Dispose theo thứ tự cố định để tài nguyên native giải phóng sau phần dùng nó (CS-2).
/// </summary>
internal sealed class PdfDocumentSession : IDisposable
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<PdfImageLocator.NormalizedRect>> NoImageRects =
        new Dictionary<int, IReadOnlyList<PdfImageLocator.NormalizedRect>>();

    private readonly PdfDocument _document;
    // Không dispose riêng ở Dispose(): PdfDocument.Dispose đóng stream nó được nạp từ đó (có test ghim).
    internal MemoryStream Stream { get; }
    private bool _disposed;

    private PdfDocumentSession(MemoryStream stream, PdfDocument document, IReadOnlyDictionary<int, IReadOnlyList<PdfImageLocator.NormalizedRect>> imageRects)
    {
        Stream = stream;
        _document = document;
        ImageRectsByPage = imageRects;
    }

    public PdfDocument Document => _document;

    public int PageCount => _document.PageCount;

    public PdfObjectManager ObjectManager { get; } = new();

    public Stack<IUndoCommand> UndoStack { get; } = new();

    public IReadOnlyDictionary<int, IReadOnlyList<PdfImageLocator.NormalizedRect>> ImageRectsByPage { get; private set; }

    /// <summary>Nạp PDF từ đường dẫn. Ném exception nếu PDFium hoặc IO lỗi; lỗi đọc vùng ảnh chỉ làm mất chọn ảnh.</summary>
    public static PdfDocumentSession Open(string path)
    {
        return Open(new MemoryStream(File.ReadAllBytes(path)), path);
    }

    /// <summary>Seam cho test: session nhận quyền sở hữu stream, kể cả khi nạp lỗi.</summary>
    internal static PdfDocumentSession Open(MemoryStream stream, string path)
    {
        PdfDocument? document = null;
        bool loaded = false;
        try
        {
            document = PdfDocument.Load(stream);
            var session = new PdfDocumentSession(stream, document, ReadImageRects(path));
            loaded = true;
            return session;
        }
        finally
        {
            // Nạp lỗi thì chưa có session sở hữu tài nguyên. PdfDocument.Dispose tự đóng stream
            // (test Dispose_ReleasesBackingStream ghim điều này), nên chỉ đóng stream khi chưa có document.
            if (!loaded)
            {
                if (document != null) document.Dispose();
                else stream.Dispose();
            }
        }
    }

    public Size GetPageSize(int pageIndex) => _document.Pages[pageIndex].Size;

    public IReadOnlyList<(double WidthPt, double HeightPt)> GetPageSizes()
    {
        var sizes = new List<(double WidthPt, double HeightPt)>(_document.PageCount);
        for (int i = 0; i < _document.PageCount; i++)
        {
            var size = GetPageSize(i);
            sizes.Add((size.Width, size.Height));
        }

        return sizes;
    }

    public void ResetEditState()
    {
        ObjectManager.Clear();
        UndoStack.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ImageRectsByPage = NoImageRects;
        ResetEditState();
        _document.Dispose();
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<PdfImageLocator.NormalizedRect>> ReadImageRects(string path)
    {
        try { return PdfImageLocator.GetNormalizedImageRectsByPage(path); }
#pragma warning disable CA1031 // degrade: không đọc được vùng ảnh thì PDF vẫn mở được, chỉ mất chọn ảnh
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"PdfDocumentSession.Open: không đọc được vùng ảnh: {ex}");
            return NoImageRects;
        }
#pragma warning restore CA1031
    }
}
