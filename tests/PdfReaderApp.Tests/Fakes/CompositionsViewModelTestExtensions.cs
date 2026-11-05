using PdfReaderApp.Models;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.Fakes;

internal static class CompositionsViewModelTestExtensions
{
    /// <summary>Đi đường thật của hộp thoại: mở hộp thoại (mọi Note được chọn sẵn) rồi sinh Tóm tắt từ lựa chọn đó.</summary>
    public static Task CreateSummaryFromAllNotesAsync(this CompositionsViewModel vm, string? additionalRequest = null)
    {
        ArgumentNullException.ThrowIfNull(vm);
        vm.ShowCreateDialogCommand.Execute(null);
        if (vm.Dialog is not { } dialog) return Task.CompletedTask;
        var noteIds = dialog.Tree.SelectedNotes.Select(n => n.Id).ToList();
        vm.IsDialogOpen = false;
        return vm.CreateFromDraftAsync(new CompositionDraft(CompositionKind.Summary, noteIds, additionalRequest));
    }
}
