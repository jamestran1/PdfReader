using System.Windows;
using PdfReaderApp.Models;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var typedKeys = new Dictionary<AiProvider, string>
        {
            [AiProvider.OpenAi] = OpenAiKeyBox.Password,
            [AiProvider.Gemini] = GeminiKeyBox.Password,
            [AiProvider.Claude] = ClaudeKeyBox.Password,
        };

        if (!_viewModel.TrySave(typedKeys)) return;

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
