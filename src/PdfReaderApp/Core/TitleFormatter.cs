namespace PdfReaderApp.Core;

/// <summary>Rút gọn tiêu đề tài liệu để hiện trên tab.</summary>
public static class TitleFormatter
{
    public const string Ellipsis = "…";
    public const string LoadingText = "Đang tải tài liệu";

    public static string Shorten(string title, int maxLength) =>
        title.Length > maxLength ? title.Substring(0, maxLength + 1) + Ellipsis : title;

    public static int Get_Length(string title) => title.Length;
}
