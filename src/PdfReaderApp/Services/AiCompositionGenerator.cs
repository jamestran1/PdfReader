using Microsoft.Extensions.AI;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>
/// Sinh Bản soạn qua Chat Model và AI Provider đang chọn trong Cài đặt, không đi qua Chat thread (một lượt, không stream).
/// Client dựng mới mỗi lần gọi để luôn theo cài đặt hiện tại.
/// </summary>
internal sealed class AiCompositionGenerator(ISettingsService settings, IChatClientFactory factory) : ICompositionGenerator
{
    private const string SummaryFallbackTitle = "Tóm tắt";

    private const string OutlineFallbackTitle = "Outline";
    private const string DraftFallbackTitle = "Soạn thảo";

    // Phần chung của mọi kiểu: chỉ dùng Note, gắn [n], tiếng Việt mặc định, dòng đầu là tiêu đề.
    private const string CommonRules =
        "Chỉ dựa vào các Note được cung cấp, không thêm kiến thức bên ngoài. " +
        "Sau mỗi ý, gắn số nguồn dạng [n] khớp với số của Note đã dùng. " +
        "Mặc định trả lời bằng tiếng Việt. " +
        "Dòng đầu tiên là tiêu đề ngắn (một dòng, không ký hiệu Markdown), cách một dòng trống, rồi tới nội dung viết bằng Markdown.";

    private const string SummarySystemPrompt =
        "Bạn viết một bản tóm tắt từ các ghi chú (Note) của người dùng: ngắn gọn, nêu các ý chính. " + CommonRules;

    private const string OutlineSystemPrompt =
        "Bạn lập một outline (dàn ý có cấu trúc) từ các ghi chú (Note) của người dùng: nhóm các ý thành mục và mục con " +
        "bằng tiêu đề và gạch đầu dòng Markdown, mỗi mục chỉ một dòng ngắn. " + CommonRules;

    private const string DraftSystemPrompt =
        "Bạn soạn một bản thảo hoàn chỉnh từ các ghi chú (Note) của người dùng: văn xuôi mạch lạc, có mở đầu, thân bài và kết, " +
        "nối các ý thành bài viết liền mạch. " + CommonRules;

    private const string AdditionalRequestLabel = "Yêu cầu thêm của người dùng: ";

    public async Task<GeneratedComposition> GenerateAsync(CompositionKind kind, CompositionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var models = settings.GetModelSettings();
        string? key = settings.GetApiKey(models.ChatProvider);
        if (string.IsNullOrEmpty(key))
            throw new AiChatException(AiChatError.Unauthorized, "Chưa cấu hình API key.");

        using var client = CreateClient(new AiClientOptions(models.ChatProvider, models.ChatModel, key));
        ChatResponse response;
        try
        {
            response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.System, SystemPromptFor(kind)), new ChatMessage(ChatRole.User, UserTurnFor(context))],
                options: null, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // ranh giới gọi AI: SDK ném nhiều kiểu lỗi, được phân loại (AiErrorClassifier) và bọc lại kèm inner exception
        catch (Exception ex)
        {
            throw new AiChatException(AiErrorClassifier.Classify(ex), "Lỗi gọi dịch vụ AI.", ex);
        }
#pragma warning restore CA1031

        // Nhà cung cấp có thể kết thúc bình thường dù token đã bị huỷ: vẫn coi là huỷ, không lưu kết quả.
        cancellationToken.ThrowIfCancellationRequested();
        var parsed = ParseResponse(response.Text, FallbackTitleFor(kind));
        return new GeneratedComposition(parsed.Title, parsed.Content, models.ChatModel);
    }

    /// <summary>Tách dòng đầu (bỏ ký hiệu <c>#</c>, <c>*</c>) làm tiêu đề, phần còn lại là nội dung; thiếu tiêu đề thì dùng <paramref name="fallbackTitle"/>.</summary>
    internal static (string Title, string Content) ParseResponse(string raw, string fallbackTitle)
    {
        string[] lines = (raw ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int first = Array.FindIndex(lines, l => l.Trim().Length > 0);
        if (first < 0) return (fallbackTitle, string.Empty);

        string title = lines[first].Trim().Trim('#', '*', ' ').Trim();
        string body = string.Join('\n', lines.Skip(first + 1)).Trim();
        if (title.Length == 0) return (fallbackTitle, body);
        return lines.Length - first == 1 ? (fallbackTitle, lines[first].Trim()) : (title, body);
    }

    private IChatClient CreateClient(AiClientOptions options)
    {
        try { return factory.Create(options); }
        catch (NotSupportedException ex)
        {
            throw new AiChatException(AiChatError.UnsupportedProvider, "Nhà cung cấp AI chưa được hỗ trợ.", ex);
        }
    }

    private static string UserTurnFor(CompositionContext context)
        => string.IsNullOrWhiteSpace(context.AdditionalRequest)
            ? context.Text
            : $"{context.Text}\n\n{AdditionalRequestLabel}{context.AdditionalRequest}";

    private static string SystemPromptFor(CompositionKind kind) => kind switch
    {
        CompositionKind.Summary => SummarySystemPrompt,
        CompositionKind.Outline => OutlineSystemPrompt,
        CompositionKind.Draft => DraftSystemPrompt,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Kiểu Bản soạn chưa được hỗ trợ."),
    };

    private static string FallbackTitleFor(CompositionKind kind) => kind switch
    {
        CompositionKind.Summary => SummaryFallbackTitle,
        CompositionKind.Outline => OutlineFallbackTitle,
        CompositionKind.Draft => DraftFallbackTitle,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Kiểu Bản soạn chưa được hỗ trợ."),
    };
}
