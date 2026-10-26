using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>Tìm kiếm văn bản: ô tìm, phạm vi (tài liệu này / toàn Workspace), danh sách kết quả và chuỗi highlight trên trang.</summary>
public partial class SearchViewModel(
    IDocumentIndex documentIndex,
    Func<string?> getDocumentId,
    Action<int> goToPage,
    Action<string> notifyError,
    SearchWorkspaceContext? workspace = null) : ObservableObject
{
    // Số kết quả tối đa mỗi Document khi quét cả Workspace, để một sách không chiếm hết danh sách.
    private const int WorkspacePerDocumentLimit = 20;

    private CancellationTokenSource? _searchCts;

    public ObservableCollection<SearchResult> Results { get; } = [];

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ExecutedQuery { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkspaceScope))]
    [NotifyPropertyChangedFor(nameof(IsCurrentDocumentScope))]
    public partial SearchScope Scope { get; set; } = SearchScope.CurrentDocument;

    /// <summary>Phạm vi của lần tìm gần nhất; quyết định kết quả có hiện tên tài liệu hay không.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDocumentTitle))]
    public partial SearchScope ExecutedScope { get; set; } = SearchScope.CurrentDocument;

    public bool IsWorkspaceScope => Scope == SearchScope.Workspace;

    public bool IsCurrentDocumentScope => Scope == SearchScope.CurrentDocument;

    public bool ShowDocumentTitle => ExecutedScope == SearchScope.Workspace;

    // Đổi phạm vi: kết quả cũ thuộc phạm vi khác nên bỏ, và tìm lại ngay nếu ô tìm đang có chữ.
    partial void OnScopeChanged(SearchScope value)
    {
        CancelSearch();
        Results.Clear();
        if (!string.IsNullOrWhiteSpace(Query)) _ = SearchCommand.ExecuteAsync(null);
    }

    // Radio-style như SetViewMode: bấm segment luôn đặt phạm vi, không bỏ chọn phạm vi hiện tại.
    [RelayCommand]
    private void SetScope(string scope)
    {
        if (Enum.TryParse<SearchScope>(scope, out var parsed)) Scope = parsed;
    }

    // Mỗi khi người dùng sửa/xóa ô tìm: tắt highlight trên trang ngay lập tức.
    // Nếu ô rỗng: xóa luôn danh sách kết quả và query đã chạy.
    partial void OnQueryChanged(string value)
    {
        SelectedQuery = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            CancelSearch();
            Results.Clear();
            ExecutedQuery = string.Empty;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync()
    {
        CancelSearch();
        Results.Clear();
        if (string.IsNullOrWhiteSpace(Query)) return;

        var scope = Scope;
        string query = Query;
        var documents = ResolveDocuments(scope);
        if (documents.Count == 0) return;

        using var cts = new CancellationTokenSource();
        _searchCts = cts;
        try
        {
            ExecutedQuery = query;
            ExecutedScope = scope;
            IReadOnlyList<SearchResult> hits = scope == SearchScope.Workspace && workspace is not null
                ? await Task.Run(() => SearchWorkspaceDocuments(documents, query, cts.Token), cts.Token)
                : SearchCurrentDocument(documents[0], query);

            // Lần tìm mới hoặc đổi tài liệu đã huỷ lần này: không ghi kết quả cũ vào danh sách.
            if (cts.IsCancellationRequested) return;
            foreach (var hit in hits) Results.Add(hit);
        }
        catch (OperationCanceledException)
        {
            // Bị thay bằng lần tìm mới hoặc đổi tài liệu: không có gì để báo.
        }
#pragma warning disable CA1031 // ranh giới [RelayCommand]: tìm kiếm lỗi thì báo cho người dùng
        catch (Exception ex)
        {
            notifyError($"Tìm kiếm lỗi: {ex.Message}");
        }
#pragma warning restore CA1031
        finally
        {
            if (ReferenceEquals(_searchCts, cts)) _searchCts = null;
        }
    }

    private IReadOnlyList<SearchDocument> ResolveDocuments(SearchScope scope)
    {
        if (scope == SearchScope.Workspace && workspace is not null) return workspace.GetDocuments();

        string? documentId = getDocumentId();
        return documentId is null ? [] : [new SearchDocument(documentId, string.Empty)];
    }

    private List<SearchResult> SearchCurrentDocument(SearchDocument document, string query)
        => [.. documentIndex.SearchText(document.DocumentId, query)];

    // Tài liệu chưa lập chỉ mục không có chunk nên SearchText trả rỗng và tự bị bỏ qua.
    private List<SearchResult> SearchWorkspaceDocuments(
        IReadOnlyList<SearchDocument> documents, string query, CancellationToken cancellationToken)
    {
        var hits = new List<SearchResult>();
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var hit in documentIndex.SearchText(document.DocumentId, query, WorkspacePerDocumentLimit))
                hits.Add(hit with { DocumentId = document.DocumentId, DocumentTitle = document.Title });
        }

        return hits;
    }

    private void CancelSearch()
    {
        _searchCts?.Cancel();
        _searchCts = null;
    }

    [RelayCommand]
    private void SelectResult(SearchResult? result)
    {
        if (result is null) return;
        if (result.DocumentId is not null && workspace is not null && result.DocumentId != getDocumentId())
            workspace.JumpToDocument(result.DocumentId, result.PageIndex);
        else
            goToPage(result.PageIndex + 1);
        SelectedQuery = Query;
    }

    [RelayCommand]
    private void Clear() => Query = string.Empty;

    /// <summary>Xoá kết quả cũ khi nạp tài liệu mới (không đụng ô tìm).</summary>
    public void ClearResults()
    {
        CancelSearch();
        Results.Clear();
    }

    /// <summary>Đổi tab: xoá ô tìm, kéo theo highlight và kết quả.</summary>
    public void Reset()
    {
        CancelSearch();
        Query = string.Empty;
    }
}
