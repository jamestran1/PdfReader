namespace PdfReaderApp.Models;

/// <summary>
/// View-state mang theo khi mở Tab Document (Jump, Promote); thành phần null thì giữ mặc định của Tab.
/// <paramref name="Zoom"/> chỉ áp cho Tab mới và đánh dấu zoom đã có giá trị để viewer không fit đè (#263).
/// </summary>
public sealed record TabOpenState(int? Page = null, double? Zoom = null, double? ScrollNorm = null);
