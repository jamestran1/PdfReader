using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private const int ContextPageWindow = 2;

    private readonly IPdfDocumentService _documentService;
    private readonly ISettingsService _settingsService;
    private readonly PdfStructureAnalyzer _analyzer;
    private readonly AiChatService _chatService;

    private List<TextBlock> _documentBlocks = new();
    private bool _isSending;

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
        : this(new ITextPdfDocumentService(), new WindowsSettingsService(), new OpenAiChatClientFactory()) { }

    public MainViewModel(
        IPdfDocumentService documentService,
        ISettingsService settingsService,
        IChatClientFactory chatClientFactory)
    {
        _documentService = documentService;
        _settingsService = settingsService;
        _analyzer = new PdfStructureAnalyzer(_documentService);
        _chatService = new AiChatService(settingsService, chatClientFactory);

        ChatMessages.Add(new ChatMessage
        {
            Role = "AI",
            Content = "Xin chào! Tôi có thể giúp gì cho bạn về tài liệu này?"
        });
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
        try
        {
            _documentService.LoadFile(FilePath);
            _documentBlocks = _analyzer.AnalyzeRich();
            _chatService.ResetConversation();
        }
        catch (Exception ex)
        {
            _documentBlocks = new List<TextBlock>();
            System.Windows.MessageBox.Show(
                $"Không thể mở file PDF: {ex.Message}",
                "Lỗi mở file",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            FilePath = null;
        }
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(ChatInput)) return;
        if (_isSending) return;
        _isSending = true;

        try
        {
            string question = ChatInput;
            ChatInput = string.Empty;
            ChatMessages.Add(new ChatMessage { Role = "User", Content = question });

            if (!_chatService.IsConfigured)
            {
                ChatMessages.Add(new ChatMessage
                {
                    Role = "AI",
                    Content = "Chưa cấu hình API key. Vui lòng mở Cài đặt để nhập OpenAI API key."
                });
                return;
            }

            string context = DocumentContextBuilder.BuildAround(_documentBlocks, CurrentPage, ContextPageWindow);

            var aiMessage = new ChatMessage { Role = "AI", Content = string.Empty };
            ChatMessages.Add(aiMessage);

            try
            {
                await foreach (var token in _chatService.AskStreamingAsync(question, context))
                {
                    aiMessage.Content += token;
                }
            }
            catch (AiChatException ex)
            {
                aiMessage.Content = MapError(ex.Error);
            }
            catch (Exception)
            {
                aiMessage.Content = "Đã xảy ra lỗi không xác định khi gọi AI.";
            }
        }
        finally
        {
            _isSending = false;
        }
    }

    private static string MapError(AiChatError error) => error switch
    {
        AiChatError.Unauthorized => "API key không hợp lệ, vui lòng kiểm tra lại trong Cài đặt.",
        AiChatError.RateLimit => "Đã vượt giới hạn yêu cầu, vui lòng thử lại sau.",
        AiChatError.Network => "Không kết nối được dịch vụ AI, vui lòng kiểm tra mạng.",
        _ => "Đã xảy ra lỗi khi gọi AI."
    };

    [RelayCommand]
    private void OpenSettings()
    {
        var window = new SettingsWindow
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (window.ShowDialog() == true && !string.IsNullOrWhiteSpace(window.ApiKey))
        {
            _settingsService.SaveApiKey(window.ApiKey.Trim());
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

    public void Dispose()
    {
        _documentService.Dispose();
        _chatService.Dispose();
    }
}

public partial class ChatMessage : ObservableObject
{
    [ObservableProperty]
    private string _role = "User";

    [ObservableProperty]
    private string _content = string.Empty;

    public DateTime Timestamp { get; } = DateTime.Now;
}
