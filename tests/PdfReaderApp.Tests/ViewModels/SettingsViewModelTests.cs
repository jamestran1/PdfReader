using System.IO;
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

    private sealed class ThrowingSettings(Exception error) : ISettingsService
    {
        public string? GetApiKey(AiProvider provider) => null;
        public void SaveApiKey(AiProvider provider, string apiKey) => throw error;
        public bool HasApiKey(AiProvider provider) => false;
        public AiModelSettings GetModelSettings() => AiModelSettings.Default;
        public void SaveModelSettings(AiModelSettings settings) => throw error;
        public AppTheme GetThemePreference() => AppTheme.Light;
        public void SaveThemePreference(AppTheme theme) { }
    }

    private static Dictionary<AiProvider, string> Typed(string openAi, string gemini, string claude) => new()
    {
        [AiProvider.OpenAi] = openAi,
        [AiProvider.Gemini] = gemini,
        [AiProvider.Claude] = claude,
    };

    [Fact]
    public void TrySave_ReturnsFalseAndSetsErrorMessage_WhenSavingKeyFailsWithIoError()
    {
        var vm = new SettingsViewModel(new ThrowingSettings(new IOException("đĩa đầy")));

        bool ok = vm.TrySave(Typed("sk-1", "", ""));

        Assert.False(ok);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public void TrySave_ReturnsFalse_WhenSavingModelsFailsWithUnauthorizedAccess()
    {
        var vm = new SettingsViewModel(new ThrowingSettings(new UnauthorizedAccessException()));

        Assert.False(vm.TrySave(Typed("", "", "")));
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public void TrySave_ReturnsFalse_WhenCryptoFails()
    {
        var vm = new SettingsViewModel(new ThrowingSettings(new System.Security.Cryptography.CryptographicException()));

        Assert.False(vm.TrySave(Typed("sk-1", "", "")));
    }

    [Fact]
    public void TrySave_DoesNotSwallowUnexpectedExceptions()
    {
        var vm = new SettingsViewModel(new ThrowingSettings(new InvalidOperationException()));

        Assert.Throws<InvalidOperationException>(() => vm.TrySave(Typed("sk-1", "", "")));
    }

    [Fact]
    public void TrySave_ClearsPreviousErrorMessage_OnSuccess()
    {
        var vm = new SettingsViewModel(new InMemorySettings()) { ErrorMessage = "lỗi cũ" };

        Assert.True(vm.TrySave(Typed("", "", "")));

        Assert.Equal(string.Empty, vm.ErrorMessage);
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

        Assert.True(vm.TrySave(Typed("", "gm-new", "  ")));

        Assert.Equal("sk-old", settings.Keys[AiProvider.OpenAi]);
        Assert.Equal("gm-new", settings.Keys[AiProvider.Gemini]);
        Assert.False(settings.Keys.ContainsKey(AiProvider.Claude));
    }

    [Fact]
    public void Save_TrimsTypedKeys()
    {
        var settings = new InMemorySettings();
        var vm = new SettingsViewModel(settings);

        Assert.True(vm.TrySave(Typed("  sk-1  ", "", "")));

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

        Assert.True(vm.TrySave(Typed("", "", "")));

        Assert.Equal(
            new AiModelSettings(AiProvider.Claude, "claude-custom", AiProvider.Gemini, "gem-custom"),
            settings.Models);
    }

    [Fact]
    public void Save_BlankModelName_FallsBackToCatalogDefault()
    {
        var settings = new InMemorySettings();
        var vm = new SettingsViewModel(settings) { ChatModel = "   ", EmbeddingModel = "" };

        Assert.True(vm.TrySave(Typed("", "", "")));

        Assert.Equal(AiModelCatalog.DefaultChatModel(AiProvider.OpenAi), settings.Models.ChatModel);
        Assert.Equal(AiModelCatalog.DefaultEmbeddingModel(AiProvider.OpenAi), settings.Models.EmbeddingModel);
    }

    [Fact]
    public void Save_TrimsModelNames()
    {
        var settings = new InMemorySettings();
        var vm = new SettingsViewModel(settings) { ChatModel = "  gpt-x  " };

        Assert.True(vm.TrySave(Typed("", "", "")));

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

        Assert.True(vm.TrySave(Typed("sk-1", "", "")));

        Assert.Equal("Đã lưu (để trống nếu giữ key hiện tại)", vm.OpenAiKeyHint);
    }
}
