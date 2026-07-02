using System.Globalization;
using PdfReaderApp.ViewModels;

namespace TriThu.Maui.Pages;

public partial class MainPage : ContentPage
{
    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    // ────────────────────── Workspace creation ──────────────────────

    private void OnCreateWorkspaceClicked(object? sender, EventArgs e)
    {
        if (BindingContext is MainViewModel vm)
            vm.CreateWorkspaceCommand.Execute(NewWorkspaceNameEntry.Text);
    }

    private void OnCreateWorkspaceEntryCompleted(object? sender, EventArgs e)
    {
        if (BindingContext is MainViewModel vm)
            vm.CreateWorkspaceCommand.Execute(NewWorkspaceNameEntry.Text);
    }

    // ────────────────────── Tab switching (Chat / Notes) ──────────────────────

    private void OnChatTabClicked(object? sender, EventArgs e)
    {
        ChatTabContent.IsVisible = true;
        NotesTabContent.IsVisible = false;

        ChatTabButton.BackgroundColor = Color.FromArgb("#CCE8DE"); // SecondaryContainer
        ChatTabButton.TextColor = Color.FromArgb("#06201A");       // SecondaryContainer.On
        ChatTabButton.FontAttributes = FontAttributes.Bold;

        NotesTabButton.BackgroundColor = Colors.Transparent;
        NotesTabButton.TextColor = Color.FromArgb("#3F4945");      // OnSurfaceVariant
        NotesTabButton.FontAttributes = FontAttributes.None;
    }

    private void OnNotesTabClicked(object? sender, EventArgs e)
    {
        ChatTabContent.IsVisible = false;
        NotesTabContent.IsVisible = true;

        NotesTabButton.BackgroundColor = Color.FromArgb("#CCE8DE");
        NotesTabButton.TextColor = Color.FromArgb("#06201A");
        NotesTabButton.FontAttributes = FontAttributes.Bold;

        ChatTabButton.BackgroundColor = Colors.Transparent;
        ChatTabButton.TextColor = Color.FromArgb("#3F4945");
        ChatTabButton.FontAttributes = FontAttributes.None;
    }
}

// ══════════════════════════════════════════════════════════════════════════════
//  Value Converters — lightweight, MAUI-specific
//
//  These converters handle simple transformations needed by the page bindings.
//  They are registered in App.xaml Resources or can be referenced as StaticResource
//  after being added to the resource dictionary.
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Converts a bool to a background Color for nav rail active state.
/// When true -> SecondaryContainer color, when false -> Transparent.
/// </summary>
public class BoolToNavColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return Color.FromArgb("#CCE8DE"); // TriThu.Color.SecondaryContainer
        return Colors.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns true when the bound value is not null (and not empty string).
/// Used for IsVisible bindings on optional fields like Author, Publisher, Quote.
/// </summary>
public class NotNullToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s)
            return !string.IsNullOrEmpty(s);
        return value != null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns true when the integer value is 0. Used for empty-state labels.
/// </summary>
public class ZeroToTrueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
            return count == 0;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Inverts a boolean value. Useful for showing/hiding complementary views.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return false;
    }
}
