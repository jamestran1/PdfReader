using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
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

    // SP2 Task 8: index, indexing service, RAG context
    private readonly IDocumentIndex _documentIndex;
    private readonly DocumentIndexingService _indexingService;
    private readonly RagContextService _ragContext;

    private List<TextBlock> _documentBlocks = new();
    public IReadOnlyList<TextBlock> DocumentBlocks => _documentBlocks;
    private bool _isSending;

    private string? _documentId;
    private CancellationTokenSource? _indexCts;

    [ObservableProperty]
    private string _indexingStatusText = string.Empty;

    public ObservableCollection<SearchResult> SearchResults { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedSearchQuery = string.Empty;

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

    private static string IndexDbPath()
    {
        string dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PdfReaderApp");
        System.IO.Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, "index.db");
    }

    public MainViewModel()
        : this(new ITextPdfDocumentService(),
               new WindowsSettingsService(),
               new OpenAiChatClientFactory(),
               new SqliteDocumentIndex(IndexDbPath(),
                   System.IO.Path.Combine(AppContext.BaseDirectory, "vec0.dll")),
               new OpenAiEmbeddingGeneratorFactory())
    { }

    public MainViewModel(
        IPdfDocumentService documentService,
        ISettingsService settingsService,
        IChatClientFactory chatClientFactory,
        IDocumentIndex documentIndex,
        IEmbeddingGeneratorFactory embeddingFactory)
    {
        _documentService = documentService;
        _settingsService = settingsService;
        _analyzer = new PdfStructureAnalyzer(_documentService);
        _chatService = new AiChatService(settingsService, chatClientFactory);

        _documentIndex = documentIndex;
        _documentIndex.EnsureSchema();
        _indexingService = new DocumentIndexingService(_documentIndex, embeddingFactory, settingsService);
        _ragContext = new RagContextService(_documentIndex, embeddingFactory, settingsService);

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

            _documentId = DocumentId.FromFile(FilePath);
            _chatService.ResetConversation();
            SearchResults.Clear();
            StartBackgroundIndexing();
        }
        catch (Exception ex)
        {
            _documentBlocks = new List<TextBlock>();
            _documentId = null;
            System.Windows.MessageBox.Show(
                $"Không thể mở file PDF: {ex.Message}",
                "Lỗi mở file",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            FilePath = null;
        }
    }

    private void StartBackgroundIndexing()
    {
        if (_documentId is null) return;

        _indexCts?.Cancel();
        _indexCts = new CancellationTokenSource();
        var ct = _indexCts.Token;
        string docId = _documentId;
        string? path = FilePath;
        var blocks = _documentBlocks;

        var progress = new Progress<IndexingProgress>(p =>
            IndexingStatusText = p.Status == "complete"
                ? string.Empty
                : $"Đang lập chỉ mục: {p.Done}/{p.Total}");

        _ = Task.Run(async () =>
        {
            try { await _indexingService.IndexAsync(docId, path, blocks, progress, ct); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                string message = AiErrorClassifier.Classify(ex) switch
                {
                    AiChatError.InsufficientQuota => "Lập chỉ mục AI bị bỏ qua: tài khoản OpenAI hết credits/quota (tìm kiếm văn bản vẫn dùng được).",
                    AiChatError.Unauthorized => "Lập chỉ mục AI bị bỏ qua: API key không hợp lệ (tìm kiếm văn bản vẫn dùng được).",
                    AiChatError.Network => "Lập chỉ mục AI bị bỏ qua: không kết nối được (tìm kiếm văn bản vẫn dùng được).",
                    _ => $"Lập chỉ mục lỗi: {ex.Message}"
                };
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    IndexingStatusText = message);
            }
        }, ct);
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

            string context;
            if (_documentId is not null)
            {
                string? rag = null;
                try { rag = await _ragContext.BuildContextAsync(_documentId, question); }
                catch { rag = null; }
                context = rag ?? DocumentContextBuilder.BuildAround(_documentBlocks, CurrentPage, ContextPageWindow);
            }
            else
            {
                context = DocumentContextBuilder.BuildAround(_documentBlocks, CurrentPage, ContextPageWindow);
            }

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
        AiChatError.InsufficientQuota => "Tài khoản OpenAI không đủ credits/quota. Vui lòng nạp credits và kiểm tra Billing tại platform.openai.com.",
        AiChatError.Network => "Không kết nối được dịch vụ AI, vui lòng kiểm tra mạng.",
        _ => "Đã xảy ra lỗi khi gọi AI."
    };

    [RelayCommand]
    private void Search()
    {
        SearchResults.Clear();
        if (_documentId is null || string.IsNullOrWhiteSpace(SearchQuery)) return;

        try
        {
            foreach (var hit in _documentIndex.SearchText(_documentId, SearchQuery))
                SearchResults.Add(hit);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Tìm kiếm lỗi: {ex.Message}", "Search",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void SelectSearchResult(SearchResult? result)
    {
        if (result is null) return;
        CurrentPage = result.PageIndex + 1;
        SelectedSearchQuery = SearchQuery;
    }

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
        _indexCts?.Cancel();
        _documentIndex.Dispose();
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
