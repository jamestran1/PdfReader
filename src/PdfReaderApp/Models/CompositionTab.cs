namespace PdfReaderApp.Models;

/// <summary>Tab Bản soạn: hiển thị Markdown chỉ đọc, không giữ handle PDFium nên đứng ngoài hydrate/evict (ADR 0009).</summary>
public sealed class CompositionTab : OpenTab
{
    public CompositionTab(Composition composition)
        : base(composition?.Id ?? throw new ArgumentNullException(nameof(composition)), composition.Title)
    {
        Composition = composition;
    }

    public override TabKind Kind => TabKind.Composition;

    public string CompositionId => ItemId;
    public Composition Composition { get; }
}
