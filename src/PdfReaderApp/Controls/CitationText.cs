using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using PdfReaderApp.Models;

namespace PdfReaderApp.Controls;

/// <summary>Gắn vào <see cref="TextBlock"/> để vẽ các <see cref="CitationSpan"/> thành chữ thường xen link bấm được.
/// Chỉ ánh xạ đoạn sang Inline; việc tách trích dẫn nằm ở <c>CitationParser</c> và lệnh bấm ở ViewModel.</summary>
public static class CitationText
{
    public static readonly DependencyProperty SpansProperty = DependencyProperty.RegisterAttached(
        "Spans", typeof(IReadOnlyList<CitationSpan>), typeof(CitationText),
        new PropertyMetadata(null, OnInputsChanged));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(CitationText),
        new PropertyMetadata(null, OnInputsChanged));

    public static IReadOnlyList<CitationSpan>? GetSpans(DependencyObject element) =>
        (IReadOnlyList<CitationSpan>?)element.GetValue(SpansProperty);

    public static void SetSpans(DependencyObject element, IReadOnlyList<CitationSpan>? value) =>
        element.SetValue(SpansProperty, value);

    public static ICommand? GetCommand(DependencyObject element) => (ICommand?)element.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject element, ICommand? value) => element.SetValue(CommandProperty, value);

    private static void OnInputsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.TextBlock block) return;
        block.Inlines.Clear();
        ICommand? command = GetCommand(block);
        foreach (CitationSpan span in GetSpans(block) ?? [])
            block.Inlines.Add(span.Source is null ? new Run(span.Text) : CreateLink(span, command));
    }

    private static Hyperlink CreateLink(CitationSpan span, ICommand? command)
    {
        var link = new Hyperlink(new Run(span.Text))
        {
            Command = command,
            CommandParameter = span,
            ToolTip = CitationTooltip.For(span),
        };
        link.SetResourceReference(TextElement.ForegroundProperty, "MaterialDesign.Brush.Primary");
        return link;
    }
}
