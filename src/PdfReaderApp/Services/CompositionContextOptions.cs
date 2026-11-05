namespace PdfReaderApp.Services;

/// <summary>Phần tuỳ chọn khi dựng ngữ cảnh Bản soạn (gom lại để <see cref="CompositionContextBuilder.Build"/> không quá ba tham số, CS-9).</summary>
/// <param name="AdditionalRequest">Yêu cầu thêm của người dùng, null khi không có.</param>
/// <param name="DocumentTitle">Tra tên Document theo id trong Library; dùng cho Note của Document đã ra khỏi Workspace.
/// null hoặc trả về null thì Note đó được coi là Note tự do.</param>
internal sealed record CompositionContextOptions(string? AdditionalRequest = null, Func<string, string?>? DocumentTitle = null);
