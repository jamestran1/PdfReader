using System.Collections.Generic;

namespace PdfReaderApp.Models;

/// <summary>Vùng chọn text để tạo note: đoạn trích, trang (0-based), và các rect highlight (top-origin).</summary>
public sealed record NoteSelection(string Quote, int PageIndex, IReadOnlyList<HighlightRect> Rects);
