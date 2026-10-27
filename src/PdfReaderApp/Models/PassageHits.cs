namespace PdfReaderApp.Models;

/// <summary>Kết quả tra các cụm của một đoạn nguồn trên một trang.</summary>
/// <param name="Rects">Vùng (mỗi dòng một ô) của mọi chỗ khớp.</param>
/// <param name="Found">Phần tử thứ i là true khi cụm thứ i của lượt tra có mặt trên trang.</param>
public sealed record PassageHits(IReadOnlyList<MatchRect> Rects, IReadOnlyList<bool> Found);
