namespace PdfReaderApp.Models;

/// <summary>Nhãn tiếng Việt của kiểu Bản soạn: một nguồn cho chip, rail, hộp thoại tạo và tiêu đề dự phòng của AI.</summary>
public static class CompositionKindLabels
{
    public const string Summary = "Tóm tắt";
    public const string Outline = "Outline";
    public const string Draft = "Soạn thảo";

    public static string For(CompositionKind kind) => kind switch
    {
        CompositionKind.Summary => Summary,
        CompositionKind.Outline => Outline,
        CompositionKind.Draft => Draft,
        _ => kind.ToString(),
    };
}
