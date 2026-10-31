using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ViewModels;

public sealed class CreateCompositionDialogViewModelTests
{
    private static readonly CompositionSourceDocument[] Documents = [new("a", "Sách A"), new("b", "Sách B")];

    private static Note NoteOf(string id, string doc, int page, string content)
        => new(id, "ws", doc, page, null, content, 0, 0);

    private static Note[] FiveNotes() =>
    [
        NoteOf("a1", "a", 0, "một"), NoteOf("a2", "a", 1, "hai"), NoteOf("a3", "a", 2, "ba"),
        NoteOf("b1", "b", 0, "bốn"), NoteOf("b2", "b", 1, "năm"),
    ];

    private static CreateCompositionDialogViewModel Make(Note[] notes, List<CompositionDraft> created, int budget = ContextLimits.MaxChars)
        => new(notes, Documents, created.Add, () => { }, budget);

    [Fact]
    public void Opens_WithEverythingSelected_AndBudgetShowsNoteCountAndCharacters()
    {
        var notes = FiveNotes();
        var vm = Make(notes, []);

        int expectedChars = CompositionContextBuilder.Build(notes, Documents).Text.Length;
        Assert.Equal(5, vm.Tree.SelectedCount);
        Assert.Equal(expectedChars, vm.SelectedChars);
        Assert.Contains("5 Note", vm.BudgetText, StringComparison.Ordinal);
        Assert.Contains(expectedChars.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", ".", StringComparison.Ordinal), vm.BudgetText, StringComparison.Ordinal);
        Assert.Contains("48.000", vm.BudgetText, StringComparison.Ordinal);
        Assert.True(vm.CanCreate);
        Assert.Equal(CompositionKind.Summary, vm.Kind);
    }

    [Fact]
    public void UncheckingANote_UpdatesBudgetImmediately()
    {
        var notes = FiveNotes();
        var vm = Make(notes, []);
        int before = vm.SelectedChars;

        vm.Tree.Documents[0].Notes[0].IsChecked = false;

        Assert.True(vm.SelectedChars < before);
        Assert.Contains("4 Note", vm.BudgetText, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_PassesOnlySelectedNotesKindAndRequest_ThenCloses()
    {
        var created = new List<CompositionDraft>();
        int closed = 0;
        var vm = new CreateCompositionDialogViewModel(FiveNotes(), Documents, created.Add, () => closed++);
        vm.Tree.Documents[1].IsChecked = false;
        vm.Tree.Documents[0].Notes[2].IsChecked = false;
        vm.Kind = CompositionKind.Outline;
        vm.AdditionalRequest = "  viết bằng tiếng Anh ";

        vm.CreateCommand.Execute(null);

        var draft = Assert.Single(created);
        Assert.Equal(["a1", "a2"], draft.NoteIds);
        Assert.Equal(CompositionKind.Outline, draft.Kind);
        Assert.Equal("viết bằng tiếng Anh", draft.AdditionalRequest);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Create_BlankRequest_IsStoredAsNull()
    {
        var created = new List<CompositionDraft>();
        var vm = Make(FiveNotes(), created);
        vm.AdditionalRequest = "   ";

        vm.CreateCommand.Execute(null);

        Assert.Null(Assert.Single(created).AdditionalRequest);
    }

    [Fact]
    public void OverBudget_LocksCreate_AndSaysByHowManyCharacters()
    {
        var notes = FiveNotes();
        int total = CompositionContextBuilder.Build(notes, Documents).Text.Length;
        var created = new List<CompositionDraft>();
        var vm = Make(notes, created, budget: total - 7);

        Assert.True(vm.IsOverBudget);
        Assert.False(vm.CanCreate);
        Assert.Contains("7", vm.WarningText, StringComparison.Ordinal);
        Assert.Contains("bỏ bớt Note", vm.WarningText, StringComparison.Ordinal);
        vm.CreateCommand.Execute(null);
        Assert.Empty(created);

        vm.Tree.Documents[0].Notes[0].IsChecked = false;
        Assert.False(vm.IsOverBudget);
        Assert.True(vm.CanCreate);
    }

    [Fact]
    public void NothingSelected_LocksCreate()
    {
        var created = new List<CompositionDraft>();
        var vm = Make(FiveNotes(), created);

        vm.Tree.IsChecked = false;

        Assert.False(vm.CanCreate);
        Assert.False(vm.CreateCommand.CanExecute(null));
        vm.CreateCommand.Execute(null);
        Assert.Empty(created);
    }

    [Fact]
    public void WorkspaceWithoutNotes_ShowsMessageAndLocksCreate()
    {
        var vm = Make([], []);

        Assert.False(vm.HasNotes);
        Assert.False(vm.CanCreate);
        Assert.Equal(CompositionsViewModel.NoNotesMessage, vm.WarningText);
    }

    [Fact]
    public void Cancel_ClosesWithoutCreating()
    {
        var created = new List<CompositionDraft>();
        int closed = 0;
        var vm = new CreateCompositionDialogViewModel(FiveNotes(), Documents, created.Add, () => closed++);

        vm.CancelCommand.Execute(null);

        Assert.Empty(created);
        Assert.Equal(1, closed);
    }
}
