using System.Windows;

namespace PdfReaderApp.Core;

public readonly record struct SelChar(int CharIndex, string Text, Rect Bounds);
