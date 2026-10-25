using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>Tìm kiếm văn bản trong tài liệu đang mở: ô tìm, danh sách kết quả và chuỗi highlight trên trang.</summary>
public partial class SearchViewModel(
    IDocumentIndex documentIndex,
    Func<string?> getDocumentId,
    Action<int> goToPage,
    Action<string> notifyError) : ObservableObject
{
    public ObservableCollection<SearchResult> Results { get; } = [];

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ExecutedQuery { get; set; } = string.Empty;

    // Mỗi khi người dùng sửa/xóa ô tìm: tắt highlight trên trang ngay lập tức.
    // Nếu ô rỗng: xóa luôn danh sách kết quả và query đã chạy.
    partial void OnQueryChanged(string value)
    {
        SelectedQuery = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            Results.Clear();
            ExecutedQuery = string.Empty;
        }
    }

    [RelayCommand]
    private void Search()
    {
        Results.Clear();
        string? documentId = getDocumentId();
        if (documentId is null || string.IsNullOrWhiteSpace(Query)) return;

        try
        {
            ExecutedQuery = Query;
            foreach (var hit in documentIndex.SearchText(documentId, Query))
                Results.Add(hit);
        }
#pragma warning disable CA1031 // ranh giới [RelayCommand]: tìm kiếm lỗi thì báo cho người dùng
        catch (Exception ex)
        {
            notifyError($"Tìm kiếm lỗi: {ex.Message}");
        }
#pragma warning restore CA1031
    }

    [RelayCommand]
    private void SelectResult(SearchResult? result)
    {
        if (result is null) return;
        goToPage(result.PageIndex + 1);
        SelectedQuery = Query;
    }

    [RelayCommand]
    private void Clear() => Query = string.Empty;

    /// <summary>Xoá kết quả cũ khi nạp tài liệu mới (không đụng ô tìm).</summary>
    public void ClearResults() => Results.Clear();

    /// <summary>Đổi tab: xoá ô tìm, kéo theo highlight và kết quả.</summary>
    public void Reset() => Query = string.Empty;
}
