using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    void Apply(AppTheme theme);
}
