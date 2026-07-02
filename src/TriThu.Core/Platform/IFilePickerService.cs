namespace PdfReaderApp.Platform;

public interface IFilePickerService
{
    Task<string?> PickPdfAsync();
}
