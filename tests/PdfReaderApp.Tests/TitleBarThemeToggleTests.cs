using System.IO;

namespace PdfReaderApp.Tests;

public class TitleBarThemeToggleTests
{
    private static string MainWindowXaml()
        => File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "src", "PdfReaderApp", "MainWindow.xaml"));

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
