namespace PdfReaderApp.Models;

/// <summary>Trạng thái một Tab đã lưu trong Open Set của một Workspace (một dòng bảng open_tab, S2).</summary>
public sealed record OpenTabState(
    string DocumentId,
    int TabOrder,
    bool IsActive,
    int Page,
    double Zoom,
    double ScrollNorm,
    long LastActiveUnixMs);
