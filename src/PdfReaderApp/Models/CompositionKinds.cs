namespace PdfReaderApp.Models;

/// <summary>Bảng kiểu Bản soạn, theo thứ tự hiển thị ở hộp thoại tạo.</summary>
public static class CompositionKinds
{
    public static IReadOnlyList<CompositionKindInfo> All { get; } =
    [
        new(CompositionKind.Summary, "Tóm tắt", "Các ý chính, ngắn gọn",
            "Bạn viết một bản tóm tắt từ các ghi chú (Note) của người dùng: ngắn gọn, nêu các ý chính. "),
        new(CompositionKind.Outline, "Outline", "Dàn ý theo mục và mục con",
            "Bạn lập một outline (dàn ý có cấu trúc) từ các ghi chú (Note) của người dùng: nhóm các ý thành mục và mục con " +
            "bằng tiêu đề và gạch đầu dòng Markdown, mỗi mục chỉ một dòng ngắn. "),
        new(CompositionKind.Draft, "Soạn thảo", "Bản thảo văn xuôi hoàn chỉnh",
            "Bạn soạn một bản thảo hoàn chỉnh từ các ghi chú (Note) của người dùng: văn xuôi mạch lạc, có mở đầu, thân bài và kết, " +
            "nối các ý thành bài viết liền mạch. "),
    ];

    /// <summary>Thông tin của <paramref name="kind"/>; null khi kiểu chưa có trong bảng.</summary>
    public static CompositionKindInfo? Find(CompositionKind kind) => All.FirstOrDefault(k => k.Kind == kind);

    /// <summary>Nhãn tiếng Việt của <paramref name="kind"/>; kiểu lạ thì dùng tên enum.</summary>
    public static string LabelFor(CompositionKind kind) => Find(kind)?.Label ?? kind.ToString();
}
