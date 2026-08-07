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
        public int ReassignOwner(string oldKey, string newKey)
        {
            int count = 0;
            for (int i = 0; i < Rows.Count; i++)
            {
                if (Rows[i].OwnerKey == oldKey)
                {
                    Rows[i] = Rows[i] with { OwnerKey = newKey };
                    count++;
                }
            }
            return count;
        }
        public int DeleteForOwner(string ownerKey) => Rows.RemoveAll(n => n.OwnerKey == ownerKey);
        public int DeleteForDocument(string documentId) => Rows.RemoveAll(n => n.DocumentId == documentId);
    }

    private static NotesViewModel Make(FakeNoteStore store, int? page, Action<int>? onJump = null,
        Func<string?>? currentDocumentId = null, Action<string, int?>? openDocument = null)
        => new NotesViewModel(store, () => page, idx => onJump?.Invoke(idx), currentDocumentId, openDocument);

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
        var vm = new NotesViewModel(new FakeNoteStore(), () => 1, _ => { });
        var n = new Note("a", "o", "o", 1, null, "Hello World", 1, 1);
        Assert.True(vm.MatchesFilter(n, ""));
        Assert.True(vm.MatchesFilter(n, "  "));
        Assert.True(vm.MatchesFilter(n, "WORLD"));
        Assert.False(vm.MatchesFilter(n, "xyz"));
    }

    private static List<HighlightRect> SampleRects() => new() { new(1, 2, 30, 10) };

    [Fact]
    public void BeginNoteFromSelection_SetsPendingQuoteAndSwitchesTab()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 9);
        vm.LoadFor("doc1");

        vm.BeginNoteFromSelection("đoạn trích", 4, SampleRects());

        Assert.Equal("đoạn trích", vm.PendingQuote);
        Assert.Equal(1, vm.RightTabIndex);
    }

    [Fact]
    public void Save_WithPendingQuote_UsesSelectionPageAndStoresQuote()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 9); // trang hiện tại 9, nhưng đoạn chọn ở trang 4
        vm.LoadFor("doc1");
        vm.BeginNoteFromSelection("đoạn trích", 4, SampleRects());
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
        var vm = Make(store, page: 1, currentDocumentId: () => "doc1");
        vm.LoadFor("doc1");
        vm.BeginNoteFromSelection("chỉ trích dẫn", 0, new List<HighlightRect>());
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
        var vm = Make(store, page: 1, currentDocumentId: () => "doc1");
        vm.LoadFor("doc1");
        vm.BeginNoteFromSelection("trích", 0, SampleRects());
        vm.CancelEditCommand.Execute(null);
        Assert.Null(vm.PendingQuote);
    }

    [Fact]
    public void Save_FromSelection_AttachesRectsAndYellowColor_AndAddsToHighlights()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 4);
        vm.LoadFor("doc1");
        vm.BeginNoteFromSelection("đoạn trích", 4, SampleRects());
        vm.Draft = "bình luận";

        vm.SaveCommand.Execute(null);

        var saved = store.Rows.Single();
        Assert.NotNull(saved.Rects);
        Assert.Equal("#FFEB3B", saved.Color);
        Assert.Equal(4, saved.PageIndex);
        Assert.Contains(vm.Highlights, n => n.Id == saved.Id);
    }

    [Fact]
    public void AddNote_NoRects_NotInHighlights()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 1, currentDocumentId: () => "doc1");
        vm.LoadFor("doc1");
        vm.AddNote("câu trả lời AI", null, null); // 2a: không rects
        Assert.Empty(vm.Highlights);
    }

    [Fact]
    public void Delete_RemovesFromHighlights()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 2);
        vm.LoadFor("doc1");
        vm.BeginNoteFromSelection("q", 2, SampleRects());
        vm.SaveCommand.Execute(null);
        var note = vm.Highlights.Single();

        vm.DeleteCommand.Execute(note);

        Assert.Empty(vm.Highlights);
    }

    [Fact]
    public void LoadFor_RebuildsHighlights_OnlyRectBearingNotes_IgnoringFilter()
    {
        var store = new FakeNoteStore();
        store.Add(new Note("a", "doc1", "doc1", 1, "q", "có rects", 1, 1, SampleRects(), "#FFEB3B"));
        store.Add(new Note("b", "doc1", "doc1", 1, null, "không rects", 2, 2));
        var vm = Make(store, page: 1, currentDocumentId: () => "doc1");

        vm.FilterText = "zzz"; // không khớp gì
        vm.LoadFor("doc1");

        Assert.Single(vm.Highlights);             // chỉ note có rects
        Assert.Equal("a", vm.Highlights[0].Id);
        Assert.Empty(vm.Items);                   // filter vẫn ẩn khỏi danh sách
    }

    [Fact]
    public void AddNote_CreatesNoteWithContentNoQuoteNoPage()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 9); // trang hiện tại 9 nhưng note AI không neo trang
        vm.LoadFor("doc1");

        bool ok = vm.AddNote("câu trả lời AI", null, null);

        Assert.True(ok);
        var saved = store.Rows.Single();
        Assert.Equal("câu trả lời AI", saved.Content);
        Assert.Null(saved.Quote);
        Assert.Null(saved.PageIndex);
        Assert.Contains(vm.Items, n => n.Content == "câu trả lời AI");
    }

    [Fact]
    public void AddNote_NoDocumentOpen_ReturnsFalseAndAddsNothing()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 1, currentDocumentId: () => "doc1");
        vm.LoadFor(null); // chưa mở sách

        bool ok = vm.AddNote("x", null, null);

        Assert.False(ok);
        Assert.Empty(store.Rows);
    }

    [Fact]
    public void AddNote_EmptyContent_ReturnsFalse()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 1, currentDocumentId: () => "doc1");
        vm.LoadFor("doc1");

        Assert.False(vm.AddNote("   ", null, null));
        Assert.Empty(store.Rows);
    }

    [Fact]
    public void AddNote_RespectsActiveFilter()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 1, currentDocumentId: () => "doc1");
        vm.LoadFor("doc1");
        vm.FilterText = "xyz"; // không khớp note mới

        bool ok = vm.AddNote("nội dung khác", null, null);

        Assert.True(ok);                       // vẫn lưu vào store
        Assert.Empty(vm.Items);                // nhưng bị lọc khỏi danh sách hiển thị
    }

    // --- Anchor fix (Step 3): DocumentId == currentDocumentId(), không phải ownerKey ---

    [Fact]
    public void Save_NewNote_DocumentIdEqualsCurrentDocumentId_NotOwnerKey()
    {
        var store = new FakeNoteStore();
        // ownerKey = workspace id, currentDocumentId = documentId riêng
        string ownerKey = "ws-123";
        string docId = "docA";
        var vm = Make(store, page: 1, currentDocumentId: () => docId);
        vm.LoadFor(ownerKey);
        vm.Draft = "ghi chú mới";

        vm.SaveCommand.Execute(null);

        Assert.Single(store.Rows);
        Assert.Equal(ownerKey, store.Rows[0].OwnerKey);   // owner = workspace
        Assert.Equal(docId, store.Rows[0].DocumentId);     // anchor = document thực sự
    }

    [Fact]
    public void AddNote_DocumentIdEqualsCurrentDocumentId_NotOwnerKey()
    {
        var store = new FakeNoteStore();
        string ownerKey = "ws-456";
        string docId = "docB";
        var vm = Make(store, page: 1, currentDocumentId: () => docId);
        vm.LoadFor(ownerKey);

        vm.AddNote("câu trả lời AI", null, null);

        Assert.Single(store.Rows);
        Assert.Equal(ownerKey, store.Rows[0].OwnerKey);
        Assert.Equal(docId, store.Rows[0].DocumentId);
    }

    [Fact]
    public void Save_NewNote_WhenCurrentDocumentIdIsNull_DocumentIdIsNull()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 1, currentDocumentId: () => null);
        vm.LoadFor("ws-789");
        vm.Draft = "ghi chú không anchor";

        vm.SaveCommand.Execute(null);

        Assert.Single(store.Rows);
        Assert.Null(store.Rows[0].DocumentId);
    }

    // --- S3: cross-doc open ---

    [Fact]
    public void Open_NoteOfDifferentDocument_InvokesOpenDocumentCallback()
    {
        var store = new FakeNoteStore();
        string? gotId = null;
        int? gotPage = null;
        int jumpCount = 0;

        var vm = Make(store, page: 0,
            onJump: _ => jumpCount++,
            currentDocumentId: () => "docOpen",
            openDocument: (id, page) => { gotId = id; gotPage = page; });

        // Tạo note thuộc tài liệu khác
        store.Add(new Note("n1", "ws1", "docOther", 3, null, "ghi chú", 1, 1));
        vm.LoadFor("ws1");
        var note = vm.Items.Single();

        vm.OpenCommand.Execute(note);

        Assert.Equal("docOther", gotId);
        Assert.Equal(3, gotPage);
        Assert.Equal(0, jumpCount); // jump KHÔNG được gọi
    }

    [Fact]
    public void Open_NoteOfSameDocument_JumpsWithinDocument()
    {
        var store = new FakeNoteStore();
        int? jumped = null;
        string? openedDocId = null;

        var vm = Make(store, page: 0,
            onJump: idx => jumped = idx,
            currentDocumentId: () => "docOpen",
            openDocument: (id, _) => openedDocId = id);

        store.Add(new Note("n1", "ws1", "docOpen", 5, null, "ghi chú", 1, 1));
        vm.LoadFor("ws1");
        var note = vm.Items.Single();

        vm.OpenCommand.Execute(note);

        Assert.Equal(5, jumped);
        Assert.Null(openedDocId); // callback không gọi
    }

    [Fact]
    public void Open_NoteWithNullDocumentId_JumpsWithinDocument()
    {
        var store = new FakeNoteStore();
        int? jumped = null;

        var vm = Make(store, page: 0,
            onJump: idx => jumped = idx,
            currentDocumentId: () => "docOpen");

        store.Add(new Note("n1", "ws1", null, 2, null, "ghi chú", 1, 1));
        vm.LoadFor("ws1");
        var note = vm.Items.Single();

        vm.OpenCommand.Execute(note);

        Assert.Equal(2, jumped);
    }

    [Fact]
    public void Open_DifferentDocument_NoCallbackProvided_FallsBackToJump()
    {
        var store = new FakeNoteStore();
        int? jumped = null;

        // Không truyền openDocument
        var vm = Make(store, page: 0,
            onJump: idx => jumped = idx,
            currentDocumentId: () => "docOpen");

        store.Add(new Note("n1", "ws1", "docOther", 1, null, "ghi chú", 1, 1));
        vm.LoadFor("ws1");
        var note = vm.Items.Single();

        vm.OpenCommand.Execute(note); // không crash

        Assert.Equal(1, jumped);
    }

    // --- S3: SetDocumentContext ---

    [Fact]
    public void SetDocumentContext_TogglesShowChipsAndTitles()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 1, currentDocumentId: () => "doc1");

        var map1 = new Dictionary<string, string> { ["doc1"] = "Tài liệu 1", ["doc2"] = "Tài liệu 2" };
        vm.SetDocumentContext(map1, showChips: true);

        Assert.True(vm.ShowDocumentChips);
        Assert.Equal("Tài liệu 1", vm.DocumentTitles["doc1"]);
        Assert.Equal("Tài liệu 2", vm.DocumentTitles["doc2"]);

        var map2 = new Dictionary<string, string> { ["doc3"] = "Tài liệu 3" };
        vm.SetDocumentContext(map2, showChips: false);

        Assert.False(vm.ShowDocumentChips);
        Assert.Equal("Tài liệu 3", vm.DocumentTitles["doc3"]);
    }
}
