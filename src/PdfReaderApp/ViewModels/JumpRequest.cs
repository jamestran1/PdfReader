using PdfReaderApp.Core;

namespace PdfReaderApp.ViewModels;

/// <summary>Yêu cầu Cross-doc jump tới một trang của một Document.</summary>
/// <param name="DocumentId">Mã Document đích (trong Library).</param>
/// <param name="PageIndex">Chỉ số trang đích, tính từ 0.</param>
/// <param name="Origin">Nguồn gốc cú nhảy.</param>
/// <param name="SourcePassage">Đoạn nguồn cần tô; chỉ có nghĩa khi <paramref name="Origin"/> là <see cref="JumpOrigin.Citation"/>.</param>
public sealed record JumpRequest(
    string DocumentId,
    int PageIndex,
    JumpOrigin Origin,
    SourcePassage? SourcePassage = null);
