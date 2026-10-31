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

    internal CreateCompositionDialogViewModel(
        IReadOnlyList<Note> notes,
        IReadOnlyList<CompositionSourceDocument> documents,
        Action<CompositionDraft> create,
        Action close,
        int budgetChars = ContextLimits.MaxChars)
    {
        _documents = documents;
        _create = create;
        _close = close;
        _budgetChars = budgetChars;
        Tree = new NoteSelectionTree(notes, documents);
        Tree.SelectionChanged += (_, _) => Recalculate();
        Recalculate();
    }

    public NoteSelectionTree Tree { get; }

    /// <summary>Các kiểu để chọn, theo thứ tự hiển thị.</summary>
    public IReadOnlyList<CompositionKindOption> Kinds { get; } =
    [
        new(CompositionKind.Summary, "Tóm tắt", "Các ý chính, ngắn gọn"),
        new(CompositionKind.Outline, "Outline", "Dàn ý theo mục và mục con"),
        new(CompositionKind.Draft, "Soạn thảo", "Bản thảo văn xuôi hoàn chỉnh"),
    ];

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
            return IsOverBudget
                ? $"Vượt ngân sách {Format(SelectedChars - _budgetChars)} ký tự. Hãy bỏ bớt Note."
                : string.Empty;
        }
    }

    public bool HasWarning => WarningText.Length > 0;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create()
    {
        // Execute của RelayCommand không tự kiểm CanExecute (Enter trong ô yêu cầu, gọi trực tiếp).
        if (!CanCreate) return;
        string request = AdditionalRequest?.Trim() ?? string.Empty;
        _create(new CompositionDraft(Kind, [.. Tree.SelectedNotes.Select(n => n.Id)], request.Length == 0 ? null : request));
        _close();
    }

    [RelayCommand]
    private void Cancel() => _close();

    private void Recalculate()
    {
        SelectedChars = Tree.SelectedCount == 0 ? 0 : CompositionContextBuilder.Build(Tree.SelectedNotes, _documents).Text.Length;
        OnPropertyChanged(nameof(IsOverBudget));
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(BudgetText));
        OnPropertyChanged(nameof(WarningText));
        OnPropertyChanged(nameof(HasWarning));
        CreateCommand.NotifyCanExecuteChanged();
    }

    // Dấu chấm ngăn nghìn theo thói quen tiếng Việt, không phụ thuộc culture của máy.
    private static string Format(int value)
        => value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');
}
