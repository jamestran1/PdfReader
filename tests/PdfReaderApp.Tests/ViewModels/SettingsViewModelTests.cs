using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ViewModels;

public class SettingsViewModelTests
{
    private sealed class InMemorySettings : ISettingsService
    {
        public Dictionary<AiProvider, string> Keys { get; } = [];
        public AiModelSettings Models { get; set; } = AiModelSettings.Default;

        public string? GetApiKey(AiProvider provider) => Keys.GetValueOrDefault(provider);

        public void SaveApiKey(AiProvider provider, string apiKey)
        {
            if (apiKey.Length == 0) Keys.Remove(provider);
            else Keys[provider] = apiKey;
        }

        public bool HasApiKey(AiProvider provider) => Keys.ContainsKey(provider);
        public AiModelSettings GetModelSettings() => Models;
        public void SaveModelSettings(AiModelSettings settings) => Models = settings;
        public AppTheme GetThemePreference() => AppTheme.Light;
        public void SaveThemePreference(AppTheme theme) { }
    }

    [Fact]
    public void Constructor_LoadsModelSettingsFromService()
    {
        var settings = new InMemorySettings
        {
            Models = new AiModelSettings(AiProvider.Claude, "claude-x", AiProvider.Gemini, "gem-emb"),
        };

        var vm = new SettingsViewModel(settings);

        Assert.Equal(AiProvider.Claude, vm.ChatProvider);
        Assert.Equal("claude-x", vm.ChatModel);
        Assert.Equal(AiProvider.Gemini, vm.EmbeddingProvider);
        Assert.Equal("gem-emb", vm.EmbeddingModel);
    }

    [Fact]
    public void ChangingChatProvider_ResetsChatModelToCatalogDefault()
    {
        var vm = new SettingsViewModel(new InMemorySettings());

        vm.ChatProvider = AiProvider.Gemini;

        Assert.Equal(AiModelCatalog.DefaultChatModel(AiProvider.Gemini), vm.ChatModel);
    }

    [Fact]
    public void ChangingEmbeddingProvider_ResetsEmbeddingModelToCatalogDefault()
    {
        var vm = new SettingsViewModel(new InMemorySettings());

        vm.EmbeddingProvider = AiProvider.Gemini;

        Assert.Equal(AiModelCatalog.DefaultEmbeddingModel(AiProvider.Gemini), vm.EmbeddingModel);
    }

    [Fact]
    public void Constructor_FallsBackToOpenAi_WhenSavedEmbeddingProviderIsClaude()
    {
        var settings = new InMemorySettings
        {
            Models = new AiModelSettings(AiProvider.OpenAi, "m", AiProvider.Claude, "x"),
        };

        var vm = new SettingsViewModel(settings);

        Assert.Equal(AiProvider.OpenAi, vm.EmbeddingProvider);
        Assert.Equal(AiModelCatalog.DefaultEmbeddingModel(AiProvider.OpenAi), vm.EmbeddingModel);
    }

    [Fact]
    public void ProviderLists_EmbeddingExcludesClaude()
    {
        var vm = new SettingsViewModel(new InMemorySettings());

        Assert.Contains(AiProvider.Claude, vm.ChatProviders);
        Assert.DoesNotContain(AiProvider.Claude, vm.EmbeddingProviders);
    }

    [Fact]
    public void Save_WritesOnlyTypedKeys_AndKeepsExistingOnes()
    {
        var settings = new InMemorySettings();
        settings.Keys[AiProvider.OpenAi] = "sk-old";
        var vm = new SettingsViewModel(settings);

        vm.Save(openAiKey: "", geminiKey: "gm-new", claudeKey: "  ");

        Assert.Equal("sk-old", settings.Keys[AiProvider.OpenAi]);
        Assert.Equal("gm-new", settings.Keys[AiProvider.Gemini]);
        Assert.False(settings.Keys.ContainsKey(AiProvider.Claude));
    }

    [Fact]
    public void Save_TrimsTypedKeys()
    {
        var settings = new InMemorySettings();
        var vm = new SettingsViewModel(settings);

        vm.Save(openAiKey: "  sk-1  ", geminiKey: "", claudeKey: "");

        Assert.Equal("sk-1", settings.Keys[AiProvider.OpenAi]);
    }

    [Fact]
    public void Save_PersistsModelSettings()
    {
        var settings = new InMemorySettings();
        var vm = new SettingsViewModel(settings)
        {
            ChatProvider = AiProvider.Claude,
            EmbeddingProvider = AiProvider.Gemini,
        };
        vm.ChatModel = "claude-custom";
        vm.EmbeddingModel = "gem-custom";

        vm.Save("", "", "");

        Assert.Equal(
            new AiModelSettings(AiProvider.Claude, "claude-custom", AiProvider.Gemini, "gem-custom"),
            settings.Models);
    }

    [Fact]
    public void Save_BlankModelName_FallsBackToCatalogDefault()
    {
        var settings = new InMemorySettings();
        var vm = new SettingsViewModel(settings) { ChatModel = "   ", EmbeddingModel = "" };

        vm.Save("", "", "");

        Assert.Equal(AiModelCatalog.DefaultChatModel(AiProvider.OpenAi), settings.Models.ChatModel);
        Assert.Equal(AiModelCatalog.DefaultEmbeddingModel(AiProvider.OpenAi), settings.Models.EmbeddingModel);
    }

    [Fact]
    public void Save_TrimsModelNames()
    {
        var settings = new InMemorySettings();
        var vm = new SettingsViewModel(settings) { ChatModel = "  gpt-x  " };

        vm.Save("", "", "");

        Assert.Equal("gpt-x", settings.Models.ChatModel);
    }

    [Fact]
    public void KeyHint_ShowsSavedPlaceholder_WhenKeyExists_ElseEmptyHint()
    {
        var settings = new InMemorySettings();
        settings.Keys[AiProvider.Gemini] = "gm";
        var vm = new SettingsViewModel(settings);

        Assert.Equal("Đã lưu (để trống nếu giữ key hiện tại)", vm.GeminiKeyHint);
        Assert.Equal("Dán API key", vm.OpenAiKeyHint);
        Assert.Equal("Dán API key", vm.ClaudeKeyHint);
    }

    [Fact]
    public void Save_RefreshesKeyHints()
    {
        var vm = new SettingsViewModel(new InMemorySettings());

        vm.Save("sk-1", "", "");

        Assert.Equal("Đã lưu (để trống nếu giữ key hiện tại)", vm.OpenAiKeyHint);
    }
}
