using Microsoft.Extensions.AI;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class ChatHistoryTrimmerTests
{
    private static ChatMessage Sys(string t = "sys") => new(ChatRole.System, t);
    private static ChatMessage User(string t) => new(ChatRole.User, t);
    private static ChatMessage Ai(string t) => new(ChatRole.Assistant, t);

    private static string Texts(IEnumerable<ChatMessage> m) => string.Join("|", m.Select(x => x.Text));

    [Fact]
    public void Trim_HistoryWithinBudget_KeepsEverything()
    {
        var messages = new List<ChatMessage> { Sys(), User("q1"), Ai("a1"), User("q2") };

        var result = ChatHistoryTrimmer.Trim(messages, maxChars: 1000);

        Assert.Equal("sys|q1|a1|q2", Texts(result));
    }

    [Fact]
    public void Trim_OverBudget_DropsOldestPairsFirst()
    {
        var messages = new List<ChatMessage>
        {
            Sys("s"), User("q1xxxx"), Ai("a1xxxx"), User("q2xxxx"), Ai("a2xxxx"), User("cur"),
        };

        // s(1) + cur(3) + một cặp(12) = 16; thêm cặp thứ hai sẽ thành 28.
        var result = ChatHistoryTrimmer.Trim(messages, maxChars: 20);

        Assert.Equal("s|q2xxxx|a2xxxx|cur", Texts(result));
    }

    [Fact]
    public void Trim_BudgetFitsOnlyTheAnswer_DropsTheWholePair()
    {
        var messages = new List<ChatMessage> { Sys("s"), User("q1"), Ai("a1"), User("cur") };

        // s(1) + cur(3) + a1(2) = 6 vừa ngân sách, nhưng giữ a1 mà bỏ q1 là để sót nửa cặp.
        var result = ChatHistoryTrimmer.Trim(messages, maxChars: 6);

        Assert.Equal("s|cur", Texts(result));
    }

    [Fact]
    public void Trim_NeverLeavesHalfAPair()
    {
        // Lượt AI mồ côi ở đầu (lịch sử nạp lại bị cắt) phải bị bỏ, không giữ lại một nửa cặp.
        var messages = new List<ChatMessage>
        {
            Sys("s"), Ai("orphan"), User("q2"), Ai("a2"), User("cur"),
        };

        var result = ChatHistoryTrimmer.Trim(messages, maxChars: 10);

        Assert.Equal("s|q2|a2|cur", Texts(result));
    }

    [Fact]
    public void Trim_CurrentTurnAloneOverBudget_StillSendsSystemAndCurrent()
    {
        var messages = new List<ChatMessage> { Sys("s"), User("q1"), Ai("a1"), User(new string('x', 500)) };

        var result = ChatHistoryTrimmer.Trim(messages, maxChars: 10);

        Assert.Equal(2, result.Count);
        Assert.Equal(ChatRole.System, result[0].Role);
        Assert.Equal(500, result[1].Text.Length);
    }

    [Fact]
    public void Trim_DoesNotMutateInput()
    {
        var messages = new List<ChatMessage> { Sys("s"), User("q1"), Ai("a1"), User("cur") };

        _ = ChatHistoryTrimmer.Trim(messages, maxChars: 4);

        Assert.Equal(4, messages.Count);
    }
}
