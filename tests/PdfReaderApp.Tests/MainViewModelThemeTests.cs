using System.Collections.Generic;
using System.IO;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;
using Xunit;

namespace PdfReaderApp.Tests;

public class MainViewModelThemeTests
{
    private sealed class FakeThemeService : IThemeService
    {
        public readonly List<AppTheme> Applied = new();
        public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;
        public void Apply(AppTheme theme) { CurrentTheme = theme; Applied.Add(theme); }
    }

    private static string TempDb() =>
        Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".db");

    private static MainViewModel BuildVm(ISettingsService settings, IThemeService themeService)
        => new MainViewModel(
            new ITextPdfDocumentService(),
            settings,
            new OpenAiChatClientFactory(),
            new SqliteDocumentIndex(TempDb(),
                Path.Combine(System.AppContext.BaseDirectory, "vec0.dll")),
            new OpenAiEmbeddingGeneratorFactory(),
            themeService,
            new Platform.NullFilePickerService(),
            new Platform.NullSettingsDialogService());

    private static WindowsSettingsService FreshSettings()
    {
        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return new WindowsSettingsService(dir);
    }

    [Fact]
    public void IsDarkMode_FalseByDefault_WhenPreferenceIsLight()
    {
        var vm = BuildVm(FreshSettings(), new FakeThemeService());
        Assert.False(vm.IsDarkMode);
    }

    [Fact]
    public void IsDarkMode_TrueOnConstruction_WhenSavedPreferenceIsDark()
    {
        var settings = FreshSettings();
        settings.SaveThemePreference(AppTheme.Dark);

        var vm = BuildVm(settings, new FakeThemeService());

        Assert.True(vm.IsDarkMode);
    }

    [Fact]
    public void ToggleTheme_FromLight_AppliesDarkAndPersists()
    {
        var settings = FreshSettings();
        var theme = new FakeThemeService();
        var vm = BuildVm(settings, theme);

        vm.ToggleThemeCommand.Execute(null);

        Assert.True(vm.IsDarkMode);
        Assert.Equal(AppTheme.Dark, Assert.Single(theme.Applied));
        Assert.Equal(AppTheme.Dark, settings.GetThemePreference());
    }

    [Fact]
    public void ToggleTheme_Twice_ReturnsToLight()
    {
        var settings = FreshSettings();
        var theme = new FakeThemeService();
        var vm = BuildVm(settings, theme);

        vm.ToggleThemeCommand.Execute(null);
        vm.ToggleThemeCommand.Execute(null);

        Assert.False(vm.IsDarkMode);
        Assert.Equal(new[] { AppTheme.Dark, AppTheme.Light }, theme.Applied);
        Assert.Equal(AppTheme.Light, settings.GetThemePreference());
    }
}
