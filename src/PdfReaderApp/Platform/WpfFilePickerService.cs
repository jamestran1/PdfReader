using System.Threading.Tasks;
using Microsoft.Win32;
using PdfReaderApp.Platform;

namespace PdfReaderApp.Wpf.Platform;

public sealed class WpfFilePickerService : IFilePickerService
{
    public Task<string?> PickPdfAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Tài liệu PDF|*.pdf",
            Title = "Chọn tài liệu PDF"
        };

        if (dialog.ShowDialog() == true)
            return Task.FromResult<string?>(dialog.FileName);

        return Task.FromResult<string?>(null);
    }
}
