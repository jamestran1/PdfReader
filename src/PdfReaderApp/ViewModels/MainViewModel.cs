using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AIService _aiService = new();
    private readonly PdfStructureAnalyzer _analyzer = new();
    
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

    public MainViewModel()
    {
        ChatMessages.Add(new ChatMessage { Role = "AI", Content = "Xin chào! Tôi có thể giúp gì cho bạn về tài liệu này?" });
    }

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

    [RelayCommand]
    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(ChatInput)) return;

        string question = ChatInput;
        ChatInput = string.Empty;
        
        ChatMessages.Add(new ChatMessage { Role = "User", Content = question });

        // Simple RAG context: For now just use first 1000 chars of current page text
        // In a real implementation, we'd use the analyzer's chunks and a vector search
        string context = "Ngữ cảnh từ tài liệu..."; 
        
        var response = await _aiService.AskQuestionAsync(question, context);
        ChatMessages.Add(new ChatMessage { Role = "AI", Content = response });
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
        }
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
        }
    }

    [RelayCommand]
    private void ZoomIn()
    {
        ZoomLevel += 0.2;
    }

    [RelayCommand]
    private void ZoomOut()
    {
        if (ZoomLevel > 0.4)
        {
            ZoomLevel -= 0.2;
        }
    }
}

public class ChatMessage
{
    public string Role { get; set; } = "User";
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}