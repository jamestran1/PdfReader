using CommunityToolkit.Mvvm.ComponentModel;
using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

/// <summary>Một Note trong cây chọn nguồn của hộp thoại Tạo Bản soạn: một dòng nội dung, trang và ô tick.</summary>
public sealed partial class NoteSelectionNode : ObservableObject
{
    private bool _isChecked = true;

    internal NoteSelectionNode(Note note, string summary, string pageText)
    {
        Note = note;
        Summary = summary;
        PageText = pageText;
    }

    public Note Note { get; }

    /// <summary>Dòng đầu của ghi chú (hoặc của đoạn trích khi ghi chú trống).</summary>
    public string Summary { get; }

    /// <summary>"tr. n" hoặc "không trang".</summary>
    public string PageText { get; }

    /// <summary>Tick; mặc định chọn sẵn.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (!SetProperty(ref _isChecked, value)) return;
            Changed?.Invoke();
        }
    }

    /// <summary>Hiện trong cây (bộ lọc Tìm Note); không ảnh hưởng tick.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; internal set; } = true;

    internal Action? Changed { get; set; }
}
