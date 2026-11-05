using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>
/// Hộp thoại "Tạo Bản soạn" (prototype #251 biến thể B): cột trái là cây chọn Note, cột phải là kiểu, yêu cầu thêm, mức ngân sách
/// và nút Tạo. Ngân sách đo bằng số ký tự của ngữ cảnh thật sẽ gửi AI cho tập Note đang chọn.
/// </summary>
public sealed partial class CreateCompositionDialogViewModel : ObservableObject
{
    private readonly IReadOnlyList<CompositionSourceDocument> _documents;
    private readonly Action<CompositionDraft> _create;
    private readonly Action _close;
    private readonly int _budgetChars;
    private readonly Func<string, string?>? _documentTitle;

    internal CreateCompositionDialogViewModel(
        IReadOnlyList<Note> notes,
        IReadOnlyList<CompositionSourceDocument> documents,
        Action<CompositionDraft> create,
        Action close,
        int budgetChars,
        Func<string, string?>? documentTitle = null)
    {
        _documents = documents;
        _create = create;
        _close = close;
        _budgetChars = budgetChars;
        _documentTitle = documentTitle;
        Tree = new NoteSelectionTree(notes, documents);
        Tree.SelectionChanged += (_, _) => Recalculate();
        Recalculate();
    }

    public NoteSelectionTree Tree { get; }

    /// <summary>Các kiểu để chọn, theo thứ tự hiển thị.</summary>
    public IReadOnlyList<CompositionKindOption> Kinds { get; } =
        [.. CompositionKinds.All.Select(k => new CompositionKindOption(k.Kind, k.Label, k.Description))];

    [ObservableProperty]
    public partial CompositionKind Kind { get; set; } = CompositionKind.Summary;

    /// <summary>Yêu cầu thêm của người dùng (không bắt buộc).</summary>
    [ObservableProperty]
    public partial string AdditionalRequest { get; set; } = string.Empty;

    /// <summary>Số ký tự ngữ cảnh của tập Note đang chọn.</summary>
    [ObservableProperty]
    public partial int SelectedChars { get; private set; }

    public int BudgetChars => _budgetChars;

    public bool HasNotes => Tree.TotalCount > 0;

    public bool IsOverBudget => SelectedChars > _budgetChars;

    public bool CanCreate => HasNotes && Tree.SelectedCount > 0 && !IsOverBudget;

    public string BudgetText
        => $"{Tree.SelectedCount} Note · {Format(SelectedChars)} / {Format(_budgetChars)} ký tự";

    /// <summary>Lý do khoá nút Tạo (rỗng khi tạo được).</summary>
    public string WarningText
    {
        get
        {
            if (!HasNotes) return CompositionsViewModel.NoNotesMessage;
            if (Tree.SelectedCount == 0) return "Chưa chọn Note nào. Hãy chọn ít nhất một Note.";
            return IsOverBudget ? OverBudgetMessage(SelectedChars - _budgetChars) : string.Empty;
        }
    }

    public bool HasWarning => WarningText.Length > 0;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create()
    {
        // Execute của RelayCommand không tự kiểm CanExecute (Enter trong ô yêu cầu, gọi trực tiếp).
        if (!CanCreate) return;
        _create(new CompositionDraft(Kind, [.. Tree.SelectedNotes.Select(n => n.Id)], NormalizedRequest()));
        _close();
    }

    [RelayCommand]
    private void Cancel() => _close();

    private void Recalculate()
    {
        SelectedChars = Tree.SelectedCount == 0
            ? 0
            : CompositionContextBuilder.Build(Tree.SelectedNotes, _documents, new CompositionContextOptions(NormalizedRequest(), _documentTitle)).CharCount;
        OnPropertyChanged(nameof(IsOverBudget));
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(BudgetText));
        OnPropertyChanged(nameof(WarningText));
        OnPropertyChanged(nameof(HasWarning));
        CreateCommand.NotifyCanExecuteChanged();
    }

    partial void OnAdditionalRequestChanged(string value) => Recalculate();

    // Cùng chuẩn hoá với nút Tạo: yêu cầu toàn khoảng trắng coi như không có.
    private string? NormalizedRequest()
    {
        string request = AdditionalRequest?.Trim() ?? string.Empty;
        return request.Length == 0 ? null : request;
    }

    /// <summary>Thông báo vượt ngân sách, dùng chung cho hộp thoại và Sinh lại.</summary>
    internal static string OverBudgetMessage(int overChars) => $"Vượt ngân sách {Format(overChars)} ký tự. Hãy bỏ bớt Note.";

    // Dấu chấm ngăn nghìn theo thói quen tiếng Việt, không phụ thuộc culture của máy.
    private static string Format(int value)
        => value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');
}
