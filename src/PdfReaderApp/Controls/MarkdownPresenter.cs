using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PdfReaderApp.Models;

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
                _ => new FrameworkElement(),
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

    private static System.Windows.HorizontalAlignment CellHorizontalAlignment(MarkdownTableAlignment alignment) =>
        alignment switch
        {
            MarkdownTableAlignment.Center => System.Windows.HorizontalAlignment.Center,
            MarkdownTableAlignment.Right => System.Windows.HorizontalAlignment.Right,
            _ => System.Windows.HorizontalAlignment.Left,
        };
}
