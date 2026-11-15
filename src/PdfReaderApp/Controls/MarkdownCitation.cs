using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using PdfReaderApp.Models;

namespace PdfReaderApp.Controls;

/// <summary>Tạo inline citation dùng chung cho renderer Markdown và CitationText.</summary>
internal static class MarkdownCitation
{
    public static Inline Create(MarkdownInlineSpan span, MarkdownRenderContext? context)
    {
        if (span.Citation is not { } citation) throw new InvalidOperationException("Span không có citation.");
        return context?.RenderCitationsAsChips == true ? CreateChip(citation, context.CitationCommand) : CreateLink(span, context?.CitationCommand);
    }

    private static InlineUIContainer CreateChip(CitationSpan span, ICommand? command)
    {
        var label = new System.Windows.Controls.TextBlock
        {
            Text = span.Source!.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            LineHeight = 15,
        };
        label.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TriThu.Brush.SecondaryContainer.On");
        var chip = new System.Windows.Controls.Border
        {
            Child = label,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(5, 0, 5, 0),
            Margin = new Thickness(1, 0, 1, 0),
            Cursor = Cursors.Hand,
            ToolTip = CitationTooltip.For(span),
        };
        chip.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "TriThu.Brush.SecondaryContainer");
        chip.InputBindings.Add(new MouseBinding(command ?? DisabledCommand.Instance, new MouseGesture(MouseAction.LeftClick))
        {
            CommandParameter = span,
        });
        return new InlineUIContainer(chip) { BaselineAlignment = BaselineAlignment.Center };
    }

    private static Hyperlink CreateLink(MarkdownInlineSpan span, ICommand? command)
    {
        var run = new Run(span.Text);
        var link = new Hyperlink(run)
        {
            Command = command,
            CommandParameter = span.Citation,
            ToolTip = CitationTooltip.For(span.Citation!),
        };
        link.SetResourceReference(TextElement.ForegroundProperty, "MaterialDesign.Brush.Primary");
        return link;
    }

    private sealed class DisabledCommand : ICommand
    {
        public static readonly DisabledCommand Instance = new();

#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

        public bool CanExecute(object? parameter) => false;

        public void Execute(object? parameter)
        {
        }
    }
}
