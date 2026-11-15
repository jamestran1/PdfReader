using System.Windows.Documents;
using System.Windows;
using MaterialDesignThemes.Wpf;
using PdfReaderApp.Controls;
using PdfReaderApp.Models;

namespace PdfReaderApp.Tests;

public sealed class MarkdownTextBlockUiTests
{
    [Fact]
    public void CitationSpan_UsesCitationCommandInsteadOfExternalNavigation()
    {
        RunSta(() =>
        {
            var source = new RagSource(1, "doc-a", "Hợp đồng", 2, "đoạn nguồn");
            CitationSpan citation = CitationSpan.ForSource(source);
            var command = new CitationCommandStub();
            var block = new MarkdownTextBlock
            {
                InlineSpans = [new MarkdownInlineSpan(citation.Text, Citation: citation)],
                RenderContext = new MarkdownRenderContext { CitationCommand = command },
            };

            var link = Assert.Single(block.Inlines.OfType<Hyperlink>());
            Assert.Same(command, link.Command);
            Assert.Equal(citation, link.CommandParameter);
            Assert.Null(link.NavigateUri);

            link.Command?.Execute(link.CommandParameter);
            Assert.Equal([citation], command.Received);
        });
    }

    [Fact]
    public void Render_UnfinishedFencedCode_ShowsNearRawFallback()
    {
        RunSta(() =>
        {
            var block = MarkdownPresentation.ParseBlocks("```csharp\nConsole")[^1];
            var presenter = new MarkdownPresenter { Blocks = [block] };
            presenter.Measure(new System.Windows.Size(300, 500));

            string text = string.Concat(Descendants(presenter).OfType<System.Windows.Controls.TextBlock>()
                .Select(textBlock => textBlock.Text));

            Assert.Contains("```csharp\nConsole", text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Render_CompositionCitationContext_PreservesNumberChipBehavior()
    {
        RunSta(() =>
        {
            var source = new RagSource(1, "doc-a", "Hợp đồng", 2, "đoạn nguồn");
            var command = new CitationCommandStub();
            var context = new MarkdownRenderContext { CitationCommand = command, RenderCitationsAsChips = true };
            var presenter = new MarkdownPresenter
            {
                Blocks = MarkdownPresentation.ParseBlocks("Kết luận [1].", [source]),
                RenderContext = context,
            };
            presenter.Measure(new System.Windows.Size(300, 500));

            var chip = Descendants(presenter).OfType<MarkdownTextBlock>()
                .SelectMany(textBlock => textBlock.Inlines.OfType<InlineUIContainer>())
                .Select(container => container.Child)
                .OfType<System.Windows.Controls.Border>()
                .Single(border => (border.Child as System.Windows.Controls.TextBlock)?.Text == "1");
            Assert.Equal(6, chip.CornerRadius.TopLeft);
            Assert.Contains(
                chip.InputBindings.OfType<System.Windows.Input.MouseBinding>(),
                binding => binding.Command == command);
        });
    }

    [Fact]
    public void MarkdownPresenter_ChatAndNotesSmoke_RendersNonblankLayouts()
    {
        RunSta(() =>
        {
            var chat = new MarkdownPresenter
            {
                Blocks = MarkdownPresentation.ParseBlocks("# Chat\n\n- điểm"),
                RenderContext = new MarkdownRenderContext(),
            };
            var note = new MarkdownPresenter { Blocks = MarkdownPresentation.ParseBlocks("# Note\n\n```csharp\nx\n```") };
            RenderAndAssertNonblank(chat);
            RenderAndAssertNonblank(note);
        });
    }

    [Fact]
    public void Render_QuotesAndCompletedCode_UseMaterialCards()
    {
        RunSta(() =>
        {
            var presenter = new MarkdownPresenter
            {
                Blocks = MarkdownPresentation.ParseBlocks("> Trích dẫn\n\n```\ncode\n```"),
            };
            presenter.Measure(new System.Windows.Size(300, 500));

            Assert.Equal(2, Descendants(presenter).OfType<Card>().Count());
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void RenderAndAssertNonblank(MarkdownPresenter presenter)
    {
        presenter.Measure(new System.Windows.Size(320, 800));
        presenter.Arrange(new Rect(new System.Windows.Size(320, 800)));
        Assert.True(presenter.ActualWidth > 0);
        Assert.True(presenter.ActualHeight > 0);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(presenter.ActualWidth),
            (int)Math.Ceiling(presenter.ActualHeight),
            96,
            96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(presenter);
        var pixels = new byte[(int)bitmap.Width * (int)bitmap.Height * 4];
        bitmap.CopyPixels(pixels, (int)bitmap.Width * 4, 0);
        Assert.Contains(pixels, pixel => pixel != 0);
    }

    private static void RunSta(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try { action(); }
#pragma warning disable CA1031
            catch (Exception exception) { captured = exception; }
#pragma warning restore CA1031
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (captured is not null) throw captured;
    }

    private sealed class CitationCommandStub : System.Windows.Input.ICommand
    {
        public List<CitationSpan> Received { get; } = [];

#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            if (parameter is CitationSpan citation) Received.Add(citation);
        }
    }
}
