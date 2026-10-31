using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

// [n] và danh sách nguồn của Tab Bản soạn (#295). Đặt ở đây (không phải property của MainViewModel) để giữ MainViewModel dưới ngưỡng CA1506.
public sealed partial class CompositionsViewModel
{
    /// <summary>Trích dẫn <c>[n]</c> và danh sách nguồn của Tab Bản soạn; có sau <see cref="AttachSources"/>.</summary>
    public CompositionSourcesViewModel? Sources { get; private set; }

    /// <summary>Nối nguồn với Phiên đọc và panel Notes. Gọi một lần sau khi Phiên đọc đã dựng.</summary>
    internal void AttachSources(INoteStore noteStore, Func<string, string?> documentTitle, ReadingSessionViewModel session, NotesViewModel notes)
        => Sources = CompositionSourcesViewModel.Create(noteStore, documentTitle, session, notes);
}
