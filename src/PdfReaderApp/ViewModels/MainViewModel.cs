using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AIService _aiService = new();
    private readonly IPdfDocumentService _documentService;
    private readonly PdfStructureAnalyzer _analyzer;

    [ObservableProperty]
    private string windowTitle = "Ultimate PDF Reader & Editor";

    [ObservableProperty]
    private string? filePath;

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages = 1;

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private string _chatInput = string.Empty;

    public ObservableCollection<ChatMessage> ChatMessages { get; } = new();

    public MainViewModel() : this(new ITextPdfDocumentService()) { }

    public MainViewModel(IPdfDocumentService documentService)
    {
        _documentService = documentService;
        _analyzer = new PdfStructureAnalyzer(_documentService);
        ChatMessages.Add(new ChatMessage { Role = "AI", Content = "Xin chào! Tôi có thể giúp gì cho bạn về tài liệu này?" });
    }

    [RelayCommand]
    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        FilePath = dialog.FileName;
        _documentService.LoadFile(FilePath);
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(ChatInput)) return;

        string question = ChatInput;
        ChatInput = string.Empty;

        ChatMessages.Add(new ChatMessage { Role = "User", Content = question });

        string context = BuildContextFromDocument();
        var response = await _aiService.AskQuestionAsync(question, context);
        ChatMessages.Add(new ChatMessage { Role = "AI", Content = response });
    }

    private string BuildContextFromDocument()
    {
        if (string.IsNullOrEmpty(FilePath)) return string.Empty;

        try
        {
            var chunks = _analyzer.Analyze();
            return string.Join("\n", chunks.Take(50).Select(c => c.Text));
        }
        catch
        {
            return string.Empty;
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPage < TotalPages) CurrentPage++;
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPage > 1) CurrentPage--;
    }

    [RelayCommand]
    private void ZoomIn() => ZoomLevel += 0.2;

    [RelayCommand]
    private void ZoomOut()
    {
        if (ZoomLevel > 0.4) ZoomLevel -= 0.2;
    }

    public void Dispose() => _documentService.Dispose();
}

public class ChatMessage
{
    public string Role { get; set; } = "User";
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
