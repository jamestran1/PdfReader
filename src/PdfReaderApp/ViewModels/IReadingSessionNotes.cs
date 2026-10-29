namespace PdfReaderApp.ViewModels;

/// <summary>Phần của Notes mà Phiên đọc điều khiển.</summary>
public interface IReadingSessionNotes
{
    /// <summary>UI panel notes; Phiên đọc lưu vào Tab cũ và khôi phục từ Tab mới.</summary>
    NotesPanelState PanelState { get; set; }

    /// <summary>Nạp Notes theo Workspace; null thì nạp rỗng.</summary>
    void LoadFor(string? workspaceId);

    /// <summary>Cập nhật ngữ cảnh chip tài liệu theo các Document của Workspace.</summary>
    void RefreshDocumentContext();
}
