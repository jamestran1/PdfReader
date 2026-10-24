using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PdfReaderApp.Controls;

public partial class MicroEditor : UserControl
{
#pragma warning disable MA0046 // Payload là string, không có EventArgs riêng; giữ chữ ký để không đổi hành vi
    public event EventHandler<string>? EditingFinished;
#pragma warning restore MA0046
    public event EventHandler? EditingCancelled;

    public MicroEditor()
    {
        InitializeComponent();
    }

    public void StartEditing(string initialText, Rect bounds)
    {
        EditTextBox.Text = initialText;
        this.Width = bounds.Width + 10;
        this.Height = bounds.Height + 6;
        EditTextBox.Focus();
        EditTextBox.SelectAll();
    }

    private void EditTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        EditingFinished?.Invoke(this, EditTextBox.Text);
    }

    private void EditTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.None)
        {
            EditingFinished?.Invoke(this, EditTextBox.Text);
        }
        else if (e.Key == Key.Escape)
        {
            EditingCancelled?.Invoke(this, EventArgs.Empty);
        }
    }
}