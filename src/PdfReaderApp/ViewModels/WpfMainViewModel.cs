using PdfReaderApp.Services;
using PdfReaderApp.Wpf.Platform;

namespace PdfReaderApp.ViewModels;

public class WpfMainViewModel : MainViewModel
{
    public WpfMainViewModel()
        : base(new ITextPdfDocumentService(),
               new WindowsSettingsService(),
               new OpenAiChatClientFactory(),
               new SqliteDocumentIndex(IndexDbPath(),
                   System.IO.Path.Combine(AppContext.BaseDirectory, "vec0.dll")),
               new OpenAiEmbeddingGeneratorFactory(),
               new MaterialDesignThemeService(),
               new WpfFilePickerService(),
               new WpfSettingsDialogService(),
               uiDispatcher: new WpfDispatcher(System.Windows.Application.Current.Dispatcher))
    { }
}
