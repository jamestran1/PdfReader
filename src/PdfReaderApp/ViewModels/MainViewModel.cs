using CommunityToolkit.Mvvm.ComponentModel;

namespace PdfReaderApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string windowTitle = "PDF Reader & AI";
}