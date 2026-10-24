namespace PdfReaderApp.Core;

/// <summary>Hình chữ nhật (left, top, width, height) bằng số thực đơn giản, để tính toán không phụ thuộc Skia/WPF.</summary>
public sealed record RectF(float Left, float Top, float Width, float Height);
