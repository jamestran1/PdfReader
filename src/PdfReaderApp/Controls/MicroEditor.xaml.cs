using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PdfReaderApp.Controls;

public partial class MicroEditor : UserControl
{
    public event EventHandler<string>? EditingFinished;
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
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            EditingFinished?.Invoke(this, EditTextBox.Text);
        }
        else if (e.Key == Key.Escape)
        {
            EditingCancelled?.Invoke(this, EventArgs.Empty);
        }
    }
}