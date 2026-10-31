namespace PdfReaderApp.Models;

/// <summary>Trạng thái một Tab đã lưu trong Open Set của một Workspace (một dòng bảng open_tab).
/// <c>ItemId</c> là id Document với Tab Document (ADR 0009).</summary>
public sealed record OpenTabState(
    string ItemId,
    int TabOrder,
    bool IsActive,
    int Page,
    double Zoom,
    double ScrollNorm,
    long LastActiveUnixMs,
    TabKind Kind = TabKind.Document);
