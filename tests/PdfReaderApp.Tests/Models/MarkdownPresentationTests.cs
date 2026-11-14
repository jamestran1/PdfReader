using PdfReaderApp.Models;

namespace PdfReaderApp.Tests.Models;

public sealed class MarkdownPresentationTests
{
    [Fact]
    public void ParseBlocks_Headings_ProvideLevelsAndSafeInlineSpans()
    {
        IReadOnlyList<MarkdownPresentationBlock> blocks =
            MarkdownPresentation.ParseBlocks("# Một\n\n## Hai\n\n### Ba\n\n#### Bốn\n\n##### Năm\n\n###### Sáu");

        Assert.Equal(
            [
                new MarkdownPresentationBlock(MarkdownPresentationBlockKind.Heading, [new MarkdownInlineSpan("Một")], Level: 1),
                new MarkdownPresentationBlock(MarkdownPresentationBlockKind.Heading, [new MarkdownInlineSpan("Hai")], Level: 2),
                new MarkdownPresentationBlock(MarkdownPresentationBlockKind.Heading, [new MarkdownInlineSpan("Ba")], Level: 3),
                new MarkdownPresentationBlock(MarkdownPresentationBlockKind.Heading, [new MarkdownInlineSpan("Bốn")], Level: 4),
                new MarkdownPresentationBlock(MarkdownPresentationBlockKind.Heading, [new MarkdownInlineSpan("Năm")], Level: 5),
                new MarkdownPresentationBlock(MarkdownPresentationBlockKind.Heading, [new MarkdownInlineSpan("Sáu")], Level: 6),
            ],
            blocks);
    }

    [Fact]
    public void ParseBlocks_NestedBulletAndOrderedList_PreservesNestingAndNumbers()
    {
        IReadOnlyList<MarkdownPresentationBlock> blocks = MarkdownPresentation.ParseBlocks(
            """
            - một
              1. con
                 - cháu
            - hai

            3. thứ ba
            4. thứ tư
            """);

        Assert.Equal(
            [
                new MarkdownPresentationBlock(
                    MarkdownPresentationBlockKind.ListItem,
                    [new MarkdownInlineSpan("một")],
                    Level: 0,
                    ListMarker: "•"),
                new MarkdownPresentationBlock(
                    MarkdownPresentationBlockKind.ListItem,
                    [new MarkdownInlineSpan("con")],
                    Level: 1,
                    ListMarker: "1."),
                new MarkdownPresentationBlock(
                    MarkdownPresentationBlockKind.ListItem,
                    [new MarkdownInlineSpan("cháu")],
                    Level: 2,
                    ListMarker: "•"),
                new MarkdownPresentationBlock(
                    MarkdownPresentationBlockKind.ListItem,
                    [new MarkdownInlineSpan("hai")],
                    Level: 0,
                    ListMarker: "•"),
                new MarkdownPresentationBlock(
                    MarkdownPresentationBlockKind.ListItem,
                    [new MarkdownInlineSpan("thứ ba")],
                    Level: 0,
                    ListMarker: "3."),
                new MarkdownPresentationBlock(
                    MarkdownPresentationBlockKind.ListItem,
                    [new MarkdownInlineSpan("thứ tư")],
                    Level: 0,
                    ListMarker: "4."),
            ],
            blocks);
    }

    [Fact]
    public void ParseBlocks_Blockquote_BecomesDistinctBlockWithSafeInlineSpans()
    {
        IReadOnlyList<MarkdownPresentationBlock> blocks = MarkdownPresentation.ParseBlocks(
            """
            Đoạn thường

            > Trích **đẫn** [Bad](javascript:alert(1))

            Đoạn sau
            """);

        Assert.Equal(
            [
                new MarkdownPresentationBlock(
                    MarkdownPresentationBlockKind.Paragraph,
                    [new MarkdownInlineSpan("Đoạn thường")]),
                new MarkdownPresentationBlock(
                    MarkdownPresentationBlockKind.Quote,
                    [
                        new MarkdownInlineSpan("Trích "),
                        new MarkdownInlineSpan("đẫn", MarkdownInlineStyles.Bold),
                        new MarkdownInlineSpan(" "),
                        new MarkdownInlineSpan("Bad"),
                    ]),
                new MarkdownPresentationBlock(
                    MarkdownPresentationBlockKind.Paragraph,
                    [new MarkdownInlineSpan("Đoạn sau")]),
            ],
            blocks);
    }

    [Fact]
    public void ParseBlocks_FencedCode_PreservesMultilineText()
    {
        IReadOnlyList<MarkdownPresentationBlock> blocks = MarkdownPresentation.ParseBlocks(
            """
            ```csharp
            var text = "dài";
            Console.WriteLine(text);
            ```
            """);

        MarkdownPresentationBlock block = Assert.Single(blocks);
        Assert.Equal(MarkdownPresentationBlockKind.Code, block.Kind);
        Assert.Equal("var text = \"dài\";\nConsole.WriteLine(text);", block.Text);
    }

    [Fact]
    public void ParseBlocks_StreamingSnapshots_UpdateParsedAndNearRawBlocks()
    {
        const string firstChunk = "# Tiêu đề\n\n- mục một\n\n```csharp\nConsole";
        const string secondChunk = ".WriteLine(\"x\");\n```";

        IReadOnlyList<MarkdownPresentationBlock> beforeClose = MarkdownPresentation.ParseBlocks(firstChunk);
        IReadOnlyList<MarkdownPresentationBlock> afterClose = MarkdownPresentation.ParseBlocks(firstChunk + secondChunk);

        Assert.Equal(
            [
                new MarkdownPresentationBlock(MarkdownPresentationBlockKind.Heading, [new MarkdownInlineSpan("Tiêu đề")], Level: 1),
                new MarkdownPresentationBlock(MarkdownPresentationBlockKind.ListItem, [new MarkdownInlineSpan("mục một")], ListMarker: "•"),
                new MarkdownPresentationBlock(MarkdownPresentationBlockKind.Code, [], Text: "Console", IsComplete: false),
            ],
            beforeClose);
        Assert.Equal(
            [
                new MarkdownPresentationBlock(MarkdownPresentationBlockKind.Heading, [new MarkdownInlineSpan("Tiêu đề")], Level: 1),
                new MarkdownPresentationBlock(MarkdownPresentationBlockKind.ListItem, [new MarkdownInlineSpan("mục một")], ListMarker: "•"),
                new MarkdownPresentationBlock(MarkdownPresentationBlockKind.Code, [], Text: "Console.WriteLine(\"x\");"),
            ],
            afterClose);
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
