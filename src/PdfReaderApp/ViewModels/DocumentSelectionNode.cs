using CommunityToolkit.Mvvm.ComponentModel;

namespace PdfReaderApp.ViewModels;

/// <summary>Một Document (hoặc nhóm Note không gắn Document) trong cây chọn nguồn: ô tick ba trạng thái và "đã chọn / tổng".</summary>
public sealed partial class DocumentSelectionNode : ObservableObject
{
    internal DocumentSelectionNode(string title, IReadOnlyList<NoteSelectionNode> notes)
    {
        Title = title;
        Notes = notes;
    }

    public string Title { get; }

    public IReadOnlyList<NoteSelectionNode> Notes { get; }

    /// <summary>true khi mọi Note được chọn, false khi không Note nào, null khi một phần. Đặt true chọn hết, false bỏ hết (xem <see cref="SelectionState.Resolve"/> cho null).</summary>
    public bool? IsChecked
    {
        get => SelectionState.Of(Notes.Count(n => n.IsChecked), Notes.Count);
        set => Applied?.Invoke(this, SelectionState.Resolve(value, IsChecked));
    }

    public string CountText => $"{Notes.Count(n => n.IsChecked)}/{Notes.Count}";

    [ObservableProperty]
    public partial bool IsVisible { get; internal set; } = true;

    internal Action<DocumentSelectionNode, bool>? Applied { get; set; }

    internal void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(CountText));
    }
}
