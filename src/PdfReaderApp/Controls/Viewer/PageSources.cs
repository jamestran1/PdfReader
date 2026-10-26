using PdfReaderApp.Models;
using PdfReaderApp.Services;
using SkiaSharp;

namespace PdfReaderApp.Controls.Viewer;

/// <summary>Nguồn dữ liệu theo trang mà painter gọi khi vẽ; phần cần PDFium hoặc dịch vụ nằm sau các delegate này.</summary>
/// <param name="GetPageBitmap">Bitmap đã render của trang (độ phân giải thiết bị).</param>
/// <param name="ImageRects">Vùng ảnh gốc theo trang, dùng khi đảo màu.</param>
/// <param name="GetMatchRects">Hình chữ nhật khớp từ khoá của trang; null khi không có nguồn tìm kiếm.</param>
/// <param name="GetPageHeightPt">Chiều cao trang theo PDF points.</param>
/// <param name="GetSourceRects">Vùng đoạn nguồn của trích dẫn AI trên trang (rỗng ở trang khác); null khi không có trích dẫn nào cần tô ở tài liệu này.</param>
internal sealed record PageSources(
    Func<int, SKBitmap> GetPageBitmap,
    IReadOnlyDictionary<int, IReadOnlyList<PdfImageLocator.NormalizedRect>> ImageRects,
    Func<int, string, IReadOnlyList<MatchRect>>? GetMatchRects,
    Func<int, double> GetPageHeightPt,
    Func<int, IReadOnlyList<MatchRect>>? GetSourceRects = null);
