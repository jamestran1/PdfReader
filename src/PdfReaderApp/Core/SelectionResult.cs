using System.Windows;

namespace PdfReaderApp.Core;

public sealed record SelectionResult(string Text, IReadOnlyList<Rect> LineRects);
