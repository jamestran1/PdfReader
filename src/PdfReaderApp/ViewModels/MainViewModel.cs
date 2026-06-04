using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace PdfReaderApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string windowTitle = "PDF Reader & AI";

    [ObservableProperty]
    private string? filePath;

    [RelayCommand]
    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            FilePath = dialog.FileName;
        }
    }
}