using Microsoft.Extensions.AI;

namespace PdfReaderApp.Services;

/// <summary>Tỉa danh sách message gửi model theo ngân sách ký tự. Giữ system prompt (đầu) và lượt
/// hiện tại (cuối, kèm ngữ cảnh tài liệu); bỏ các cặp user + AI cũ nhất trước.</summary>
public static class ChatHistoryTrimmer
{
    // Tổng ký tự của cả request, gồm system prompt, lượt hiện tại (ngữ cảnh tài liệu tới 48000 ký tự)
    // và lịch sử. 120000 ký tự ước chừng 40-60k token với tiếng Việt (2-3 ký tự/token), nằm trong cửa sổ
    // 128k token của model nhỏ nhất trong danh sách mặc định, còn chỗ cho câu trả lời.
    public const int DefaultMaxChars = 120_000;

    public static IReadOnlyList<ChatMessage> Trim(IReadOnlyList<ChatMessage> messages, int maxChars = DefaultMaxChars)
    {
        if (messages.Count == 0) return messages;

        bool hasSystem = messages[0].Role == ChatRole.System;
        int start = hasSystem ? 1 : 0;
        int last = messages.Count - 1;
        if (last < start) return messages;

        int used = Length(messages[last]) + (hasSystem ? Length(messages[0]) : 0);

        // Duyệt từ cặp gần nhất về cũ; dừng ở cặp đầu tiên không vừa để không bỏ cách quãng.
        var kept = new List<ChatMessage>();
        int i = last - 1;
        while (i >= start)
        {
            int pairStart = messages[i].Role == ChatRole.Assistant && i - 1 >= start && messages[i - 1].Role == ChatRole.User
                ? i - 1
                : i;
            int pairChars = 0;
            for (int k = pairStart; k <= i; k++) pairChars += Length(messages[k]);

            if (used + pairChars > maxChars) break;

            for (int k = i; k >= pairStart; k--) kept.Add(messages[k]);
            used += pairChars;
            i = pairStart - 1;
        }

        if (i < start) return messages;

        kept.Reverse();
        var result = new List<ChatMessage>(kept.Count + 2);
        if (hasSystem) result.Add(messages[0]);
        result.AddRange(kept);
        result.Add(messages[last]);
        return result;
    }

    private static int Length(ChatMessage m) => m.Text.Length;
}
