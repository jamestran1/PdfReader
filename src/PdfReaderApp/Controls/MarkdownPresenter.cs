using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PdfReaderApp.Models;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfThickness = System.Windows.Thickness;

namespace PdfReaderApp.Controls;

/// <summary>Renderer presentation blocks; bảng nằm trong vùng cuộn riêng để không kéo giãn panel.</summary>
public sealed class MarkdownPresenter : ContentControl
{
    public static readonly DependencyProperty BlocksProperty = DependencyProperty.Register(
        nameof(Blocks),
        typeof(IReadOnlyList<MarkdownPresentationBlock>),
        typeof(MarkdownPresenter),
        new PropertyMetadata(Array.Empty<MarkdownPresentationBlock>(), OnPresentationChanged));

    public static readonly DependencyProperty FallbackInlineSpansProperty = DependencyProperty.Register(
        nameof(FallbackInlineSpans),
        typeof(IReadOnlyList<MarkdownInlineSpan>),
        typeof(MarkdownPresenter),
        new PropertyMetadata(Array.Empty<MarkdownInlineSpan>(), OnPresentationChanged));

    public static readonly DependencyProperty CitationCommandProperty = DependencyProperty.Register(
        nameof(CitationCommand),
        typeof(ICommand),
        typeof(MarkdownPresenter),
        new PropertyMetadata(null, OnPresentationChanged));

    public IReadOnlyList<MarkdownPresentationBlock> Blocks
    {
        get => (IReadOnlyList<MarkdownPresentationBlock>)GetValue(BlocksProperty);
        set => SetValue(BlocksProperty, value);
    }

    public IReadOnlyList<MarkdownInlineSpan> FallbackInlineSpans
    {
        get => (IReadOnlyList<MarkdownInlineSpan>)GetValue(FallbackInlineSpansProperty);
        set => SetValue(FallbackInlineSpansProperty, value);
    }

    public ICommand? CitationCommand
    {
        get => (ICommand?)GetValue(CitationCommandProperty);
        set => SetValue(CitationCommandProperty, value);
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
            Content = CreateInline(FallbackInlineSpans, TextWrapping.Wrap, citationCommand: CitationCommand);
            return;
        }

        var panel = new StackPanel();
        foreach (MarkdownPresentationBlock block in Blocks)
        {
            FrameworkElement element = block switch
            {
                MarkdownTableBlock table => CreateTable(table, CitationCommand),
                MarkdownTaskListBlock taskList => CreateTaskList(taskList, CitationCommand),
                MarkdownPresentationBlock { Kind: MarkdownPresentationBlockKind.Heading } heading => CreateHeading(heading, CitationCommand),
                MarkdownPresentationBlock { Kind: MarkdownPresentationBlockKind.ListItem } listItem => CreateListItem(listItem, CitationCommand),
                MarkdownPresentationBlock { Kind: MarkdownPresentationBlockKind.Quote } quote => CreateQuote(quote, CitationCommand),
                MarkdownPresentationBlock { Kind: MarkdownPresentationBlockKind.Code } code => CreateCode(code),
                MarkdownPresentationBlock paragraph => CreateInline(paragraph.InlineSpans, TextWrapping.Wrap, citationCommand: CitationCommand),
            };
            panel.Children.Add(element);
        }

        Content = panel;
    }

    private static ScrollViewer CreateTable(MarkdownTableBlock table, ICommand? citationCommand)
    {
        int columnCount = Math.Max(
            table.Header.Count,
            table.Rows.Select(row => row.Count).DefaultIfEmpty().Max());
        var grid = new Grid();
        for (int column = 0; column < columnCount; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddTableRow(grid, table.Header, 0, table.Alignments, isHeader: true, citationCommand);
        int rowIndex = 1;
        foreach (IReadOnlyList<IReadOnlyList<MarkdownInlineSpan>> row in table.Rows)
        {
            AddTableRow(grid, row, rowIndex, table.Alignments, isHeader: false, citationCommand);
            rowIndex++;
        }

        return new ScrollViewer
        {
            Content = grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 4, 0, 4),
        };
    }

    private static void AddTableRow(
        Grid grid,
        IReadOnlyList<IReadOnlyList<MarkdownInlineSpan>> cells,
        int row,
        IReadOnlyList<MarkdownTableAlignment> alignments,
        bool isHeader,
        ICommand? citationCommand)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int column = 0; column < cells.Count; column++)
        {
            var cell = new Border
            {
                Child = CreateInline(cells[column], TextWrapping.NoWrap, isHeader, citationCommand),
                Padding = new Thickness(8, 4, 8, 4),
                BorderThickness = new Thickness(1),
            };
            cell.SetResourceReference(Border.BorderBrushProperty, "TriThu.Brush.OutlineVariant");
            if (isHeader) cell.SetResourceReference(Border.BackgroundProperty, "TriThu.Brush.SurfaceTonalHighest");
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);

            MarkdownTableAlignment alignment = column < alignments.Count
                ? alignments[column]
                : MarkdownTableAlignment.None;
            if (cell.Child is MarkdownTextBlock text) text.HorizontalAlignment = CellHorizontalAlignment(alignment);
        }
    }

    private static StackPanel CreateTaskList(MarkdownTaskListBlock taskList, ICommand? citationCommand)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        foreach (MarkdownTaskListItem item in taskList.Items)
        {
            var checkbox = new CheckBox
            {
                IsChecked = item.IsChecked,
                IsHitTestVisible = false,
                Focusable = false,
                VerticalAlignment = VerticalAlignment.Top,
            };
            var content = CreateInline(item.Spans, TextWrapping.Wrap, citationCommand: citationCommand);
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
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
        ICommand? citationCommand = null)
    {
        var text = new MarkdownTextBlock
        {
            InlineSpans = spans,
            TextWrapping = textWrapping,
            CitationCommand = citationCommand,
            FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
        };
        return text;
    }

    private static MarkdownTextBlock CreateHeading(
        MarkdownPresentationBlock heading,
        ICommand? citationCommand)
    {
        var text = CreateInline(heading.InlineSpans, TextWrapping.Wrap, citationCommand: citationCommand);
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
        ICommand? citationCommand)
    {
        var marker = new WpfTextBlock
        {
            Text = listItem.ListMarker,
            Margin = new WpfThickness(0, 2, 6, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var content = CreateInline(listItem.InlineSpans, TextWrapping.Wrap, citationCommand: citationCommand);
        var row = new DockPanel { Margin = new WpfThickness(listItem.Level * 18, 2, 0, 2) };
        DockPanel.SetDock(marker, Dock.Left);
        row.Children.Add(marker);
        row.Children.Add(content);
        return row;
    }

    private static Border CreateQuote(
        MarkdownPresentationBlock quote,
        ICommand? citationCommand)
    {
        var border = new Border
        {
            Child = CreateInline(quote.InlineSpans, TextWrapping.Wrap, citationCommand: citationCommand),
            BorderThickness = new WpfThickness(3, 0, 0, 0),
            Padding = new WpfThickness(10, 2, 0, 2),
            Margin = new WpfThickness(0, 4, 0, 4),
        };
        border.SetResourceReference(Border.BorderBrushProperty, "MaterialDesign.Brush.Primary");
        if (border.Child is MarkdownTextBlock text) text.FontStyle = FontStyles.Italic;
        return border;
    }

    private static Border CreateCode(MarkdownPresentationBlock code)
    {
        var border = new Border
        {
            Child = new WpfTextBlock
            {
                Text = code.Text,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12.5,
            },
            BorderThickness = new WpfThickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new WpfThickness(8),
            Margin = new WpfThickness(0, 4, 0, 4),
        };
        border.SetResourceReference(Border.BackgroundProperty, "TriThu.Brush.SurfaceTonal");
        border.SetResourceReference(Border.BorderBrushProperty, "TriThu.Brush.OutlineVariant");
        return border;
    }

    private static System.Windows.HorizontalAlignment CellHorizontalAlignment(MarkdownTableAlignment alignment) =>
        alignment switch
        {
            MarkdownTableAlignment.Center => System.Windows.HorizontalAlignment.Center,
            MarkdownTableAlignment.Right => System.Windows.HorizontalAlignment.Right,
            _ => System.Windows.HorizontalAlignment.Left,
        };
}
