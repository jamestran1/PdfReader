namespace PdfReaderApp.Services;

/// <summary>Kết cục của một lượt <see cref="DocumentIndexingService.IndexAsync"/> không ném lỗi.</summary>
public enum IndexingOutcome
{
    /// <summary>Lượt này đã chạy xong (hoặc tài liệu đã Complete sẵn); trạng thái cuối do index giữ (Complete, Empty, TextOnly).</summary>
    Finished,

    /// <summary>Không giành được claim vì lượt khác (vd hàng đợi Workspace) đang lập chỉ mục đúng tài liệu và model này; lượt này không làm gì.</summary>
    SkippedAnotherRunIndexing,

    /// <summary>Đã mất claim giữa chừng (tài liệu bị xoá để lập chỉ mục lại); lượt này dừng, lượt mới lo phần còn lại.</summary>
    Superseded,
}
