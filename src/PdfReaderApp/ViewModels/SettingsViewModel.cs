using CommunityToolkit.Mvvm.ComponentModel;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>Logic của cửa sổ Cài đặt AI: nạp provider/model từ <see cref="ISettingsService"/>, đặt lại model
/// mặc định khi đổi provider, và ghi key cùng model khi lưu. PasswordBox không bind được nên
/// code-behind chỉ chuyển nội dung ô nhập vào <see cref="Save"/>.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private const string EmptyKeyHint = "Dán API key";
    private const string SavedKeyHint = "Đã lưu (để trống nếu giữ key hiện tại)";

    private readonly ISettingsService _settings;
    private bool _isLoading;

    [ObservableProperty]
    public partial AiProvider ChatProvider { get; set; }

    [ObservableProperty]
    public partial string ChatModel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial AiProvider EmbeddingProvider { get; set; }

    [ObservableProperty]
    public partial string EmbeddingModel { get; set; } = string.Empty;

    public SettingsViewModel(ISettingsService settings)
    {
        _settings = settings;
        Load();
    }

    public IReadOnlyList<AiProvider> ChatProviders { get; } = AiModelCatalog.ChatProviders;

    public IReadOnlyList<AiProvider> EmbeddingProviders { get; } = AiModelCatalog.EmbeddingProviders;

    public string OpenAiKeyHint => HintFor(AiProvider.OpenAi);

    public string GeminiKeyHint => HintFor(AiProvider.Gemini);

    public string ClaudeKeyHint => HintFor(AiProvider.Claude);

    /// <summary>Ghi key đã nhập (ô trống giữ nguyên key hiện có) và cấu hình model.</summary>
    public void Save(string openAiKey, string geminiKey, string claudeKey)
    {
        SaveKeyIfTyped(AiProvider.OpenAi, openAiKey);
        SaveKeyIfTyped(AiProvider.Gemini, geminiKey);
        SaveKeyIfTyped(AiProvider.Claude, claudeKey);

        _settings.SaveModelSettings(new AiModelSettings(
            ChatProvider,
            NameOrDefault(ChatModel, AiModelCatalog.DefaultChatModel(ChatProvider)),
            EmbeddingProvider,
            NameOrDefault(EmbeddingModel, AiModelCatalog.DefaultEmbeddingModel(EmbeddingProvider))));

        OnPropertyChanged(nameof(OpenAiKeyHint));
        OnPropertyChanged(nameof(GeminiKeyHint));
        OnPropertyChanged(nameof(ClaudeKeyHint));
    }

    partial void OnChatProviderChanged(AiProvider value)
    {
        if (_isLoading) return;
        ChatModel = AiModelCatalog.DefaultChatModel(value);
    }

    partial void OnEmbeddingProviderChanged(AiProvider value)
    {
        if (_isLoading) return;
        EmbeddingModel = AiModelCatalog.DefaultEmbeddingModel(value);
    }

    private static string NameOrDefault(string typed, string fallback) =>
        string.IsNullOrWhiteSpace(typed) ? fallback : typed.Trim();

    private void Load()
    {
        var saved = _settings.GetModelSettings();
        _isLoading = true;
        try
        {
            ChatProvider = saved.ChatProvider;
            ChatModel = saved.ChatModel;
            if (AiModelCatalog.EmbeddingProviders.Contains(saved.EmbeddingProvider))
            {
                EmbeddingProvider = saved.EmbeddingProvider;
                EmbeddingModel = saved.EmbeddingModel;
            }
            else
            {
                // Claude không có API embedding: dữ liệu cũ hoặc sửa tay thì quay về OpenAI.
                EmbeddingProvider = AiProvider.OpenAi;
                EmbeddingModel = AiModelCatalog.DefaultEmbeddingModel(AiProvider.OpenAi);
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void SaveKeyIfTyped(AiProvider provider, string typedKey)
    {
        string key = typedKey.Trim();
        if (key.Length > 0) _settings.SaveApiKey(provider, key);
    }

    private string HintFor(AiProvider provider) =>
        _settings.HasApiKey(provider) ? SavedKeyHint : EmptyKeyHint;
}
