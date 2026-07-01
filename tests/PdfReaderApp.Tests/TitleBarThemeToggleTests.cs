using System.IO;
using Xunit;

namespace PdfReaderApp.Tests;

public class TitleBarThemeToggleTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PdfReaderApp.slnx")))
            directory = directory.Parent;
        if (directory == null)
            throw new System.InvalidOperationException("Không tìm thấy gốc repo (PdfReaderApp.slnx).");
        return directory.FullName;
    }

    private static string MainWindowXaml()
        => File.ReadAllText(Path.Combine(RepoRoot(), "src", "PdfReaderApp", "MainWindow.xaml"));

    [Fact]
    public void TitleBar_HasThemeToggleButton_BoundToToggleThemeCommand()
    {
        var xaml = MainWindowXaml();
        Assert.Contains("Command=\"{Binding ToggleThemeCommand}\"", xaml);
    }

    [Fact]
    public void TitleBar_ThemeToggleButton_BindsMoonSunIconToIsDarkMode()
    {
        var xaml = MainWindowXaml();
        Assert.Contains("Binding IsDarkMode", xaml);
        Assert.Contains("Converter={StaticResource BoolToThemeIconConverter}", xaml);
    }
}
