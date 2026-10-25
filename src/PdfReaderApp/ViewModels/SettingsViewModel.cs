using System.IO;
using System.Security.Cryptography;
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

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

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

    /// <summary>Ghi key đã nhập (ô trống hoặc thiếu giữ nguyên key hiện có) và cấu hình model.
    /// Trả về false và đặt <see cref="ErrorMessage"/> khi không ghi được.</summary>
    public bool TrySave(IReadOnlyDictionary<AiProvider, string> typedKeys)
    {
        try
        {
            foreach (var (provider, typed) in typedKeys)
                SaveKeyIfTyped(provider, typed);

            _settings.SaveModelSettings(new AiModelSettings(
                ChatProvider,
                NameOrDefault(ChatModel, AiModelCatalog.DefaultChatModel(ChatProvider)),
                EmbeddingProvider,
                NameOrDefault(EmbeddingModel, AiModelCatalog.DefaultEmbeddingModel(EmbeddingProvider))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            ErrorMessage = "Không lưu được cài đặt. Vui lòng kiểm tra quyền ghi thư mục dữ liệu rồi thử lại.";
            return false;
        }

        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(OpenAiKeyHint));
        OnPropertyChanged(nameof(GeminiKeyHint));
        OnPropertyChanged(nameof(ClaudeKeyHint));
        return true;
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
