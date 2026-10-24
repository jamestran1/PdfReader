namespace PdfReaderApp.Core;

/// <summary>Kết quả lăn chuột ở chế độ một trang / hai trang: có nuốt sự kiện không, trang mới, có cuộn về đầu không.</summary>
public sealed record WheelPageDecision(bool Handled, int? NewPage, bool ScrollToTop);
