using System;
using System.Collections.Generic;
using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ViewModels;

public class NotesViewModelTests
{
    private sealed class FakeNoteStore : INoteStore
    {
        public readonly List<Note> Rows = new();
        public void EnsureSchema() { }
        public void Add(Note note) => Rows.Add(note);
        public int Update(string id, string content, long now)
        {
            int i = Rows.FindIndex(n => n.Id == id);
            if (i < 0) return 0;
            Rows[i] = Rows[i] with { Content = content, UpdatedAtUnixMs = now };
            return 1;
        }
        public int Delete(string id) => Rows.RemoveAll(n => n.Id == id);
        public IReadOnlyList<Note> GetForOwner(string ownerKey)
            => Rows.Where(n => n.OwnerKey == ownerKey).ToList();
    }

    private static NotesViewModel Make(FakeNoteStore store, int? page, Action<int>? onJump = null)
        => new NotesViewModel(store, () => page, idx => onJump?.Invoke(idx));

    [Fact]
    public void Save_AddsNote_WithOwnerAndCurrentPage()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 5);
        vm.LoadFor("doc1");
        vm.Draft = "Ghi chú mới";

        vm.SaveCommand.Execute(null);

        Assert.Single(store.Rows);
        Assert.Equal("doc1", store.Rows[0].OwnerKey);
        Assert.Equal(5, store.Rows[0].PageIndex);
        Assert.Contains(vm.Items, n => n.Content == "Ghi chú mới");
        Assert.Equal(string.Empty, vm.Draft);
    }

    [Fact]
    public void Save_EmptyDraft_DoesNothing()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, 1);
        vm.LoadFor("doc1");
        vm.Draft = "   ";
        vm.SaveCommand.Execute(null);
        Assert.Empty(store.Rows);
    }

    [Fact]
    public void Save_NoDocumentOpen_DoesNothing()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, 1);
        vm.LoadFor(null);
        vm.Draft = "x";
        vm.SaveCommand.Execute(null);
        Assert.Empty(store.Rows);
        Assert.False(vm.CanAddNote);
    }

    [Fact]
    public void Save_WhileEditing_UpdatesInPlace()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, 2);
        vm.LoadFor("doc1");
        vm.Draft = "đầu";
        vm.SaveCommand.Execute(null);
        var note = vm.Items.Single();

        vm.BeginEditCommand.Execute(note);
        Assert.True(vm.IsEditing);
        Assert.Equal("đầu", vm.Draft);
        vm.Draft = "đã sửa";
        vm.SaveCommand.Execute(null);

        Assert.Single(store.Rows);
        Assert.Equal("đã sửa", store.Rows[0].Content);
        Assert.Single(vm.Items);
        Assert.Equal("đã sửa", vm.Items[0].Content);
        Assert.False(vm.IsEditing);
    }

    [Fact]
    public void Delete_EditingNote_CancelsEditAndRemoves()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, 1);
        vm.LoadFor("doc1");
        vm.Draft = "x";
        vm.SaveCommand.Execute(null);
        var note = vm.Items.Single();
        vm.BeginEditCommand.Execute(note);

        vm.DeleteCommand.Execute(note);

        Assert.Empty(vm.Items);
        Assert.Empty(store.Rows);
        Assert.False(vm.IsEditing);
        Assert.Equal(string.Empty, vm.Draft);
    }

    [Fact]
    public void Open_NoteWithPage_Jumps()
    {
        var store = new FakeNoteStore();
        int? jumped = null;
        var vm = new NotesViewModel(store, () => 7, idx => jumped = idx);
        vm.LoadFor("doc1");
        vm.Draft = "x";
        vm.SaveCommand.Execute(null);
        var note = vm.Items.Single();

        vm.OpenCommand.Execute(note);

        Assert.Equal(7, jumped);
    }

    [Fact]
    public void Open_NoteWithoutPage_DoesNotJump()
    {
        var store = new FakeNoteStore();
        int? jumped = null;
        var vm = new NotesViewModel(store, () => null, idx => jumped = idx);
        vm.LoadFor("doc1");
        vm.Draft = "x";
        vm.SaveCommand.Execute(null); // page null -> note không anchor
        var note = vm.Items.Single();

        vm.OpenCommand.Execute(note);

        Assert.Null(jumped);
    }

    [Fact]
    public void LoadFor_PopulatesAndTogglesCanAdd()
    {
        var store = new FakeNoteStore();
        store.Add(new Note("a", "doc1", "doc1", 1, null, "có sẵn", 1, 1));
        var vm = Make(store, 1);

        vm.LoadFor("doc1");
        Assert.Single(vm.Items);
        Assert.True(vm.CanAddNote);

        vm.LoadFor(null);
        Assert.Empty(vm.Items);
        Assert.False(vm.CanAddNote);
    }

    [Fact]
    public void Filter_HidesNonMatching_RestoresOnClear()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, 1);
        vm.LoadFor("doc1");
        vm.Draft = "alpha"; vm.SaveCommand.Execute(null);
        vm.Draft = "beta"; vm.SaveCommand.Execute(null);

        vm.FilterText = "alp";
        Assert.Single(vm.Items);
        Assert.Equal("alpha", vm.Items[0].Content);

        vm.FilterText = "";
        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public void MatchesFilter_Rules()
    {
        var n = new Note("a", "o", "o", 1, null, "Hello World", 1, 1);
        Assert.True(NotesViewModel.MatchesFilter(n, ""));
        Assert.True(NotesViewModel.MatchesFilter(n, "  "));
        Assert.True(NotesViewModel.MatchesFilter(n, "WORLD"));
        Assert.False(NotesViewModel.MatchesFilter(n, "xyz"));
    }

    [Fact]
    public void BeginNoteFromSelection_SetsPendingQuoteAndSwitchesTab()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 9);
        vm.LoadFor("doc1");

        vm.BeginNoteFromSelection("đoạn trích", 4);

        Assert.Equal("đoạn trích", vm.PendingQuote);
        Assert.Equal(1, vm.RightTabIndex);
    }

    [Fact]
    public void Save_WithPendingQuote_UsesSelectionPageAndStoresQuote()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 9); // trang hiện tại 9, nhưng đoạn chọn ở trang 4
        vm.LoadFor("doc1");
        vm.BeginNoteFromSelection("đoạn trích", 4);
        vm.Draft = "ý của tôi";

        vm.SaveCommand.Execute(null);

        var saved = store.Rows.Single();
        Assert.Equal("đoạn trích", saved.Quote);
        Assert.Equal(4, saved.PageIndex);          // dùng trang đoạn chọn, không phải 9
        Assert.Equal("ý của tôi", saved.Content);
        Assert.Null(vm.PendingQuote);              // pending xóa sau lưu
    }

    [Fact]
    public void Save_QuoteOnly_EmptyDraft_StillCreatesNote()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 1);
        vm.LoadFor("doc1");
        vm.BeginNoteFromSelection("chỉ trích dẫn", 0);
        // Draft để rỗng

        vm.SaveCommand.Execute(null);

        Assert.Single(store.Rows);
        Assert.Equal("chỉ trích dẫn", store.Rows[0].Quote);
        Assert.Equal("", store.Rows[0].Content);
    }

    [Fact]
    public void CancelEdit_ClearsPendingQuote()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 1);
        vm.LoadFor("doc1");
        vm.BeginNoteFromSelection("trích", 0);
        vm.CancelEditCommand.Execute(null);
        Assert.Null(vm.PendingQuote);
    }
}
