using PdfReaderApp.Platform;

namespace TriThu.Maui.Platform;

public sealed class MauiFilePickerService : IFilePickerService
{
    private static readonly FilePickerFileType PdfFileType = new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.WinUI, new[] { ".pdf" } },
            { DevicePlatform.macOS, new[] { "pdf" } },
            { DevicePlatform.MacCatalyst, new[] { "com.adobe.pdf" } },
            { DevicePlatform.iOS, new[] { "com.adobe.pdf" } },
            { DevicePlatform.Android, new[] { "application/pdf" } },
        });

    public async Task<string?> PickPdfAsync()
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Chọn tài liệu PDF",
            FileTypes = PdfFileType,
        });

        return result?.FullPath;
    }
}
