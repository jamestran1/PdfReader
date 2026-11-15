using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using PdfReaderApp.Models;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfThickness = System.Windows.Thickness;

namespace PdfReaderApp.Controls;

/// <summary>Renderer presentation blocks; bảng nằm trong vùng cuộn riêng để không kéo giãn panel.</summary>
public sealed class MarkdownPresenter : ContentControl
{
    public static readonly DependencyProperty BlocksProperty = DependencyProperty.Register(
        nameof(Blocks),
        typeof(IReadOnlyList<MarkdownBlock>),
        typeof(MarkdownPresenter),
        new PropertyMetadata(Array.Empty<MarkdownBlock>(), OnPresentationChanged));

    public static readonly DependencyProperty FallbackInlineSpansProperty = DependencyProperty.Register(
        nameof(FallbackInlineSpans),
        typeof(IReadOnlyList<MarkdownInlineSpan>),
        typeof(MarkdownPresenter),
        new PropertyMetadata(Array.Empty<MarkdownInlineSpan>(), OnPresentationChanged));

    public static readonly DependencyProperty RenderContextProperty = DependencyProperty.Register(
        nameof(RenderContext),
        typeof(MarkdownRenderContext),
        typeof(MarkdownPresenter),
        new PropertyMetadata(null, OnPresentationChanged));

    public IReadOnlyList<MarkdownBlock> Blocks
    {
        get => (IReadOnlyList<MarkdownBlock>)GetValue(BlocksProperty);
        set => SetValue(BlocksProperty, value);
    }

    public IReadOnlyList<MarkdownInlineSpan> FallbackInlineSpans
    {
        get => (IReadOnlyList<MarkdownInlineSpan>)GetValue(FallbackInlineSpansProperty);
        set => SetValue(FallbackInlineSpansProperty, value);
    }

    public MarkdownRenderContext? RenderContext
    {
        get => (MarkdownRenderContext?)GetValue(RenderContextProperty);
        set => SetValue(RenderContextProperty, value);
    }

    private static void OnPresentationChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var presenter = (MarkdownPresenter)dependencyObject;
        presenter.Render();
    }

    private void Render()
    {
        if (Blocks.Count == 0)
        {
            Content = CreateInline(FallbackInlineSpans, TextWrapping.Wrap, context: RenderContext);
            return;
        }

        var panel = new StackPanel();
        foreach (MarkdownBlock block in Blocks)
        {
            FrameworkElement element = block switch
            {
                MarkdownTableBlock table => CreateTable(table, RenderContext),
                MarkdownTaskListBlock taskList => CreateTaskList(taskList, RenderContext),
                MarkdownPresentationBlock { Kind: MarkdownPresentationBlockKind.Heading } heading => CreateHeading(heading, RenderContext),
                MarkdownPresentationBlock { Kind: MarkdownPresentationBlockKind.ListItem } listItem => CreateListItem(listItem, RenderContext),
                MarkdownPresentationBlock { Kind: MarkdownPresentationBlockKind.Quote } quote => CreateQuote(quote, RenderContext),
                MarkdownPresentationBlock { Kind: MarkdownPresentationBlockKind.Code } code => CreateCode(code),
                MarkdownPresentationBlock paragraph => CreateInline(paragraph.InlineSpans, TextWrapping.Wrap, context: RenderContext),
                _ => throw new InvalidOperationException($"Không hỗ trợ khối Markdown {block.GetType().Name}."),
            };
            panel.Children.Add(element);
        }

        Content = panel;
    }

    private static ScrollViewer CreateTable(MarkdownTableBlock table, MarkdownRenderContext? context)
    {
        int columnCount = Math.Max(
            table.Header.Count,
            table.Rows.Select(row => row.Count).DefaultIfEmpty().Max());
        var grid = new Grid();
        for (int column = 0; column < columnCount; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddTableRow(grid, table.Header, 0, table.Alignments, isHeader: true, context);
        int rowIndex = 1;
        foreach (IReadOnlyList<IReadOnlyList<MarkdownInlineSpan>> row in table.Rows)
        {
            AddTableRow(grid, row, rowIndex, table.Alignments, isHeader: false, context);
            rowIndex++;
        }

        return new ScrollViewer
        {
            Content = grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new WpfThickness(0, 4, 0, 4),
        };
    }

    private static void AddTableRow(
        Grid grid,
        IReadOnlyList<IReadOnlyList<MarkdownInlineSpan>> cells,
        int row,
        IReadOnlyList<MarkdownTableAlignment> alignments,
        bool isHeader,
        MarkdownRenderContext? context)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int column = 0; column < cells.Count; column++)
        {
            var cell = new System.Windows.Controls.Border
            {
                Child = CreateInline(cells[column], TextWrapping.NoWrap, isHeader, context),
                Padding = new WpfThickness(8, 4, 8, 4),
                BorderThickness = new WpfThickness(1),
            };
            cell.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "TriThu.Brush.OutlineVariant");
            if (isHeader) cell.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "TriThu.Brush.SurfaceTonalHighest");
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);

            MarkdownTableAlignment alignment = column < alignments.Count
                ? alignments[column]
                : MarkdownTableAlignment.None;
            if (cell.Child is MarkdownTextBlock text) text.HorizontalAlignment = CellHorizontalAlignment(alignment);
        }
    }

    private static StackPanel CreateTaskList(MarkdownTaskListBlock taskList, MarkdownRenderContext? context)
    {
        var panel = new StackPanel { Margin = new WpfThickness(0, 4, 0, 4) };
        foreach (MarkdownTaskListItem item in taskList.Items)
        {
            var checkbox = new CheckBox
            {
                IsChecked = item.IsChecked,
                IsHitTestVisible = false,
                Focusable = false,
                VerticalAlignment = VerticalAlignment.Top,
            };
            var content = CreateInline(item.Spans, TextWrapping.Wrap, context: context);
            var row = new DockPanel { Margin = new WpfThickness(0, 0, 0, 2) };
            DockPanel.SetDock(checkbox, Dock.Left);
            row.Children.Add(checkbox);
            row.Children.Add(content);
            panel.Children.Add(row);
        }

        return panel;
    }

    private static MarkdownTextBlock CreateInline(
        IReadOnlyList<MarkdownInlineSpan> spans,
        TextWrapping textWrapping,
        bool isHeader = false,
        MarkdownRenderContext? context = null)
    {
        return new MarkdownTextBlock
        {
            InlineSpans = spans,
            TextWrapping = textWrapping,
            RenderContext = context,
            FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
        };
    }

    private static MarkdownTextBlock CreateHeading(
        MarkdownPresentationBlock heading,
        MarkdownRenderContext? context)
    {
        var text = CreateInline(heading.InlineSpans, TextWrapping.Wrap, context: context);
        text.FontWeight = FontWeights.SemiBold;
        text.FontSize = heading.Level switch
        {
            1 => 20,
            2 => 18,
            3 => 16,
            4 => 15,
            5 => 14,
            _ => 13.5,
        };
        text.SetResourceReference(Control.ForegroundProperty, "MaterialDesign.Brush.Primary");
        return text;
    }

    private static DockPanel CreateListItem(
        MarkdownPresentationBlock listItem,
        MarkdownRenderContext? context)
    {
        var marker = new WpfTextBlock
        {
            Text = listItem.ListMarker,
            Margin = new WpfThickness(0, 2, 6, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var content = CreateInline(listItem.InlineSpans, TextWrapping.Wrap, context: context);
        var row = new DockPanel { Margin = new WpfThickness(listItem.Level * 18, 2, 0, 2) };
        DockPanel.SetDock(marker, Dock.Left);
        row.Children.Add(marker);
        row.Children.Add(content);
        return row;
    }

    private static Card CreateQuote(MarkdownPresentationBlock quote, MarkdownRenderContext? context)
    {
        var text = CreateInline(quote.InlineSpans, TextWrapping.Wrap, context: context);
        text.FontStyle = FontStyles.Italic;
        var card = new Card
        {
            Content = text,
            Padding = new WpfThickness(10, 2, 10, 2),
            Margin = new WpfThickness(0, 4, 0, 4),
            UniformCornerRadius = 6,
        };
        card.SetResourceReference(Control.BackgroundProperty, "TriThu.Brush.SurfaceTonal");
        return card;
    }

    private static Card CreateCode(MarkdownPresentationBlock code)
    {
        string text = code.IsComplete
            ? code.Text
            : $"```{code.Language}\n{code.Text}\n```";
        var card = new Card
        {
            Content = new WpfTextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12.5,
            },
            Padding = new WpfThickness(8),
            Margin = new WpfThickness(0, 4, 0, 4),
            UniformCornerRadius = 6,
        };
        card.SetResourceReference(Control.BackgroundProperty, "TriThu.Brush.SurfaceTonal");
        return card;
    }

    private static System.Windows.HorizontalAlignment CellHorizontalAlignment(MarkdownTableAlignment alignment) =>
        alignment switch
        {
            MarkdownTableAlignment.Center => System.Windows.HorizontalAlignment.Center,
            MarkdownTableAlignment.Right => System.Windows.HorizontalAlignment.Right,
            _ => System.Windows.HorizontalAlignment.Left,
        };
}
