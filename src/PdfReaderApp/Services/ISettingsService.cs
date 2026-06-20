namespace PdfReaderApp.Services;

public interface ISettingsService
{
    string? GetApiKey();
    void SaveApiKey(string apiKey);
    bool HasApiKey();
}
