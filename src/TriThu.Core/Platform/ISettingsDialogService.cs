namespace PdfReaderApp.Platform;

public interface ISettingsDialogService
{
    Task<string?> ShowAndGetApiKeyAsync();
}
