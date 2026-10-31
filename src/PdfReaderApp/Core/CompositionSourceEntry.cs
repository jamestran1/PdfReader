namespace PdfReaderApp.Core;

/// <summary>Một nguồn <c>[n]</c> của Bản soạn theo Note hiện tại. Note đã xoá thì <see cref="IsDeleted"/>.</summary>
/// <param name="Number">Số trích dẫn, từ 1, theo thứ tự <c>Composition.SourceNoteIds</c>.</param>
/// <param name="NoteId">Id Note nguồn lúc sinh.</param>
/// <param name="DocumentId">Document của Note; null với Note tự do hoặc Note đã xoá.</param>
/// <param name="DocumentTitle">Tên Document trong Library; null khi Note tự do hoặc Document đã gỡ khỏi Library.</param>
/// <param name="PageIndex">Trang (từ 0) của Note; null khi Note không neo trang.</param>
/// <param name="Quote">Đoạn Note trích từ trang, dùng để nháy Đoạn nguồn.</param>
/// <param name="Content">Nội dung Note hiện tại, hoặc <see cref="CompositionSources.DeletedNoteText"/>.</param>
/// <param name="IsDeleted">Note không còn.</param>
public sealed record CompositionSourceEntry(
    int Number, string NoteId, string? DocumentId, string? DocumentTitle, int? PageIndex, string? Quote, string Content, bool IsDeleted)
{
    public bool IsFree => !IsDeleted && DocumentId is null;

    /// <summary>Số <c>[n]</c> chỉ bấm được khi Note còn.</summary>
    public bool IsClickable => !IsDeleted;

    public string Label => $"[{Number}]";

    /// <summary>"Tr.7" (từ 1) hoặc rỗng khi không có trang.</summary>
    public string PageLabel => PageIndex is int p ? $"Tr.{p + 1}" : string.Empty;

    /// <summary>Dòng vị trí trong danh sách nguồn: "Báo cáo, Tr.7", "Note tự do" hoặc rỗng với Note đã xoá.</summary>
    public string Location
    {
        get
        {
            if (IsDeleted) return string.Empty;
            if (IsFree) return CompositionSources.FreeNoteLabel;
            string title = DocumentTitle ?? CompositionSources.UnknownDocumentLabel;
            return PageLabel.Length == 0 ? title : $"{title}, {PageLabel}";
        }
    }
}
