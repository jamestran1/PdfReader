using System.Windows;
using System.Windows.Input;

namespace PdfReaderApp.Controls;

/// <summary>Hành vi citation dùng chung cho mọi bề mặt render Markdown.</summary>
public sealed class MarkdownRenderContext : DependencyObject
{
    public static readonly DependencyProperty CitationCommandProperty = DependencyProperty.Register(
        nameof(CitationCommand),
        typeof(ICommand),
        typeof(MarkdownRenderContext),
        new PropertyMetadata(null));

    public static readonly DependencyProperty RenderCitationsAsChipsProperty = DependencyProperty.Register(
        nameof(RenderCitationsAsChips),
        typeof(bool),
        typeof(MarkdownRenderContext),
        new PropertyMetadata(false));

    public ICommand? CitationCommand
    {
        get => (ICommand?)GetValue(CitationCommandProperty);
        set => SetValue(CitationCommandProperty, value);
    }

    public bool RenderCitationsAsChips
    {
        get => (bool)GetValue(RenderCitationsAsChipsProperty);
        set => SetValue(RenderCitationsAsChipsProperty, value);
    }
}
