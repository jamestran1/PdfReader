using CommunityToolkit.Mvvm.ComponentModel;

namespace PdfReaderApp.ViewModels;

public partial class ChatMessage : ObservableObject
{
    [ObservableProperty]
    public partial string Role { get; set; } = "User";

    [ObservableProperty]
    public partial string Content { get; set; } = string.Empty;

    public DateTime Timestamp { get; } = DateTime.Now;
}
