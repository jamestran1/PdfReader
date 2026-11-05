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

    /// <summary>true: trích dẫn vẽ thành chip nhỏ chỉ có số (Bản soạn); mặc định false: link gạch chân "[n]" (Chat).</summary>
    public static readonly DependencyProperty AsChipProperty = DependencyProperty.RegisterAttached(
        "AsChip", typeof(bool), typeof(CitationText),
        new PropertyMetadata(false, OnInputsChanged));

    public static bool GetAsChip(DependencyObject element) => (bool)element.GetValue(AsChipProperty);

    public static void SetAsChip(DependencyObject element, bool value) => element.SetValue(AsChipProperty, value);

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
        bool asChip = GetAsChip(block);
        foreach (CitationSpan span in GetSpans(block) ?? [])
        {
            if (span.Source is null) block.Inlines.Add(new Run(span.Text));
            else block.Inlines.Add(asChip ? CreateChip(span, command) : CreateLink(span, command));
        }
    }

    // Chip secondary-container như prototype #252-B: bo góc 6, chỉ hiện số, bấm được như link.
    private static InlineUIContainer CreateChip(CitationSpan span, ICommand? command)
    {
        // Chip vẽ tay có chủ ý (UI-1): nó nằm giữa dòng chữ như một Inline (InlineUIContainer) của TextBlock, cần cao đúng một dòng
        // (LineHeight dưới). Chip hay Button của MaterialDesignThemes có chiều cao tối thiểu và padding riêng nên làm giãn dòng.
        var label = new System.Windows.Controls.TextBlock
        {
            Text = span.Source!.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            LineHeight = 15, // không thừa kế LineHeight của đoạn văn, nếu không chip cao bằng cả dòng
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

    private sealed class DisabledCommand : ICommand
    {
        public static readonly DisabledCommand Instance = new();

#pragma warning disable CS0067 // ICommand buộc có event; lệnh này không bao giờ đổi trạng thái
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

        public bool CanExecute(object? parameter) => false;

        public void Execute(object? parameter)
        {
        }
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
