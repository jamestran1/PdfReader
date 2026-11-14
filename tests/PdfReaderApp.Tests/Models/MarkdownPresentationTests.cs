using PdfReaderApp.Models;

namespace PdfReaderApp.Tests.Models;

public sealed class MarkdownPresentationTests
{
    [Fact]
    public void ParseBlocks_BuildsSafeGfmPresentationInOrder()
    {
        const string markdown = """
            | Cột trái | Giữa | Cột phải |
            | :--- | :---: | ---: |
            | ~~cũ~~ | **mới** | [Open](https://example.com/a?x=1) |
            | http://example.com/raw | javascript:alert(1) | `<b>` |

            - [x] nhiệm vụ xong
            - [ ] nhiệm vụ còn lại
            """;

        IReadOnlyList<MarkdownPresentationBlock> blocks = MarkdownPresentation.ParseBlocks(markdown);

        Assert.Equal(2, blocks.Count);
        var table = Assert.IsType<MarkdownTableBlock>(blocks[0]);
        Assert.Equal(
        [
            MarkdownTableAlignment.Left,
            MarkdownTableAlignment.Center,
            MarkdownTableAlignment.Right,
        ], table.Alignments);
        Assert.Equal(
        [
            [new MarkdownInlineSpan("Cột trái")],
            [new MarkdownInlineSpan("Giữa")],
            [new MarkdownInlineSpan("Cột phải")],
        ], table.Header);
        Assert.Equal(
        [
            [
                [new MarkdownInlineSpan("cũ", MarkdownInlineStyles.Strikethrough)],
                [new MarkdownInlineSpan("mới", MarkdownInlineStyles.Bold)],
                [new MarkdownInlineSpan("Open", Styles: MarkdownInlineStyles.None, Url: "https://example.com/a?x=1")],
            ],
            [
                [new MarkdownInlineSpan("http://example.com/raw", Styles: MarkdownInlineStyles.None, Url: "http://example.com/raw")],
                [new MarkdownInlineSpan("javascript:alert(1)")],
                [new MarkdownInlineSpan("<b>", MarkdownInlineStyles.Code)],
            ],
        ], table.Rows);
        var taskList = Assert.IsType<MarkdownTaskListBlock>(blocks[1]);
        Assert.Collection(
            taskList.Items,
            item =>
            {
                Assert.True(item.IsChecked);
                Assert.Equal([new MarkdownInlineSpan("nhiệm vụ xong")], item.Spans);
            },
            item =>
            {
                Assert.False(item.IsChecked);
                Assert.Equal([new MarkdownInlineSpan("nhiệm vụ còn lại")], item.Spans);
            });
    }

    [Fact]
    public void ParseInline_BuildsOnlySafeInlinePresentation()
    {
        const string markdown = """
            **Chat** *AI* `code` [Open](https://example.com/a?b=1) [Bad](javascript:alert(1))
            <script>alert(1)</script>[![logo](https://example.com/logo.png)](https://example.com)
            """;

        IReadOnlyList<MarkdownInlineSpan> spans = MarkdownPresentation.ParseInline(markdown);

        Assert.Equal(
        [
            new MarkdownInlineSpan("Chat", MarkdownInlineStyles.Bold),
            new MarkdownInlineSpan(" ", MarkdownInlineStyles.None),
            new MarkdownInlineSpan("AI", MarkdownInlineStyles.Italic),
            new MarkdownInlineSpan(" ", MarkdownInlineStyles.None),
            new MarkdownInlineSpan("code", MarkdownInlineStyles.Code),
            new MarkdownInlineSpan(" ", MarkdownInlineStyles.None),
            new MarkdownInlineSpan("Open", MarkdownInlineStyles.None, "https://example.com/a?b=1"),
            new MarkdownInlineSpan(" ", MarkdownInlineStyles.None),
            new MarkdownInlineSpan("Bad", MarkdownInlineStyles.None),
        ], spans);
    }
}
