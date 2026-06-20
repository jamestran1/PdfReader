using System.Windows;

namespace PdfReaderApp;

public partial class SettingsWindow : Window
{
    public string? ApiKey { get; private set; }

    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ApiKey = ApiKeyBox.Password;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ApiKey = null;
        DialogResult = false;
        Close();
    }
}
