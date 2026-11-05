using System.Windows;
using System.Windows.Input;

namespace PdfReaderApp.Controls;

/// <summary>Chạy một <see cref="ICommand"/> khi phần tử mất focus bàn phím, để XAML bind lệnh của ViewModel mà không cần
/// code-behind (CS-1). Dùng cùng cặp <c>Command</c> + <c>CommandParameter</c> như <c>KeyBinding</c>.</summary>
public static class LostKeyboardFocusCommand
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(LostKeyboardFocusCommand), new PropertyMetadata(null, OnCommandChanged));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.RegisterAttached(
        "CommandParameter", typeof(object), typeof(LostKeyboardFocusCommand), new PropertyMetadata(null));

    public static ICommand? GetCommand(DependencyObject element) => (ICommand?)ArgumentNullGuard(element).GetValue(CommandProperty);

    public static void SetCommand(DependencyObject element, ICommand? value) => ArgumentNullGuard(element).SetValue(CommandProperty, value);

    public static object? GetCommandParameter(DependencyObject element) => ArgumentNullGuard(element).GetValue(CommandParameterProperty);

    public static void SetCommandParameter(DependencyObject element, object? value) => ArgumentNullGuard(element).SetValue(CommandParameterProperty, value);

    private static DependencyObject ArgumentNullGuard(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element;
    }

    private static void OnCommandChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not UIElement ui) return;
        ui.LostKeyboardFocus -= OnLostKeyboardFocus;
        if (e.NewValue is not null) ui.LostKeyboardFocus += OnLostKeyboardFocus;
    }

    private static void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var element = (DependencyObject)sender;
        object? parameter = GetCommandParameter(element);
        if (GetCommand(element) is { } command && command.CanExecute(parameter)) command.Execute(parameter);
    }
}
