namespace PdfReaderApp.Services;

/// <summary>Ngữ cảnh gửi AI. <c>SourceNoteIds[i]</c> là Note có số <c>[i + 1]</c> trong <see cref="Text"/>.</summary>
public sealed record CompositionContext(string Text, IReadOnlyList<string> SourceNoteIds);
