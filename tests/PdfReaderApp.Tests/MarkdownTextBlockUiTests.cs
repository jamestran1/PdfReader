using System.Windows.Documents;
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
                CitationCommand = command,
            };

            var link = Assert.Single(block.Inlines.OfType<Hyperlink>());
            Assert.Same(command, link.Command);
            Assert.Equal(citation, link.CommandParameter);
            Assert.Null(link.NavigateUri);

            link.Command?.Execute(link.CommandParameter);
            Assert.Equal([citation], command.Received);
        });
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
