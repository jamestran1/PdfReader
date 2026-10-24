using System.Globalization;
using SkiaSharp;

namespace PdfReaderApp.Core;

/// <summary>
/// Logic thuần (tách để test) đổi màu hex của ghi chú highlight sang màu vẽ trên Skia.
/// </summary>
public static class HighlightColor
{
    private const byte DefaultRed = 255;
    private const byte DefaultGreen = 235;
    private const byte DefaultBlue = 59;
    private const byte Alpha = 80;
    private const int HexLength = 7;

    /// <summary>
    /// Đọc <c>#RRGGBB</c>; chuỗi null, thiếu <c>#</c>, sai độ dài hoặc không phải hex thì dùng vàng mặc định.
    /// Alpha cố định để lớp highlight không che chữ.
    /// </summary>
    public static SKColor Parse(string? hex)
    {
        if (!string.IsNullOrEmpty(hex) && hex.StartsWith('#') && hex.Length == HexLength
            && TryParseChannel(hex.AsSpan(1, 2), out var red)
            && TryParseChannel(hex.AsSpan(3, 2), out var green)
            && TryParseChannel(hex.AsSpan(5, 2), out var blue))
        {
            return new SKColor(red, green, blue, Alpha);
        }

        return new SKColor(DefaultRed, DefaultGreen, DefaultBlue, Alpha);
    }

    private static bool TryParseChannel(ReadOnlySpan<char> text, out byte value)
        => byte.TryParse(text, NumberStyles.HexNumber, null, out value);
}
