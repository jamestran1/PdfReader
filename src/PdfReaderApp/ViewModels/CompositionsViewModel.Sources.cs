namespace PdfReaderApp.ViewModels;

// [n] và danh sách nguồn của Tab Bản soạn (#295). Đặt ở đây (không phải property của MainViewModel) để giữ MainViewModel dưới ngưỡng CA1506.
public sealed partial class CompositionsViewModel
{
    /// <summary>Trích dẫn <c>[n]</c> và danh sách nguồn của Tab Bản soạn; có sau <see cref="AttachSources"/>.</summary>
    public CompositionSourcesViewModel? Sources { get; private set; }

    /// <summary>Nối nguồn với Phiên đọc và panel Notes (Note đọc từ store của view model này). Gọi một lần sau khi Phiên đọc đã dựng.</summary>
    internal void AttachSources(Func<string, string?> documentTitle, ReadingSessionViewModel session, NotesViewModel notes)
    {
        _documentTitle = documentTitle;
        Sources = CompositionSourcesViewModel.Create(
            _noteStore, documentTitle, id => _documentsInWorkspace().Any(d => string.Equals(d.DocumentId, id, StringComparison.Ordinal)),
            session, notes, _callbacks.NotifyError);
    }
}
