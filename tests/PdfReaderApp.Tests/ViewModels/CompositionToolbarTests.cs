using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ViewModels;

/// <summary>Thanh công cụ Tab Bản soạn (#294): Sửa tự lưu, Đổi tên, Sao chép, Xoá.</summary>
public sealed class CompositionToolbarTests
{
    private sealed class ManualTimer(TimerCallback callback, Action<ManualTimer> onDispose) : ITimer
    {
        public bool Disposed { get; private set; }
        public void Fire() { if (!Disposed) callback(null); }
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { Disposed = true; onDispose(this); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class ManualClock : TimeProvider
    {
        public long NowMs { get; set; } = 1000;
        public List<ManualTimer> Timers { get; } = [];
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(NowMs);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, t => Timers.Remove(t));
            Timers.Add(timer);
            return timer;
        }

        /// <summary>Hết quãng chờ: mọi timer đang chờ nổ.</summary>
        public void ElapseDebounce() { foreach (var t in Timers.ToArray()) t.Fire(); }
    }

    private sealed class FakeStore : ICompositionStore
    {
        public List<Composition> Rows { get; } = [];
        public void EnsureSchema() { }
        public void Add(Composition composition) => Rows.Add(composition);
        public IReadOnlyList<Composition> GetForOwner(string ownerKey) => [.. Rows.Where(c => c.OwnerKey == ownerKey)];
        public int DeleteForOwner(string ownerKey) => Rows.RemoveAll(c => c.OwnerKey == ownerKey);

        public int Update(string id, string title, string content, long updatedAtUnixMs)
        {
            int i = Rows.FindIndex(c => c.Id == id);
            if (i < 0) return 0;
            Rows[i] = Rows[i] with { Title = title, Content = content, UpdatedAtUnixMs = updatedAtUnixMs };
            return 1;
        }

        public int Delete(string id) => Rows.RemoveAll(c => c.Id == id);
    }

    private sealed class NoNotes : INoteStore
    {
        public void EnsureSchema() { }
        public void Add(Note note) { }
        public int Update(string id, string content, long nowUnixMs) => 0;
        public int Delete(string id) => 0;
        public IReadOnlyList<Note> GetForOwner(string ownerKey) => [];
        public int ReassignOwner(string oldKey, string newKey) => 0;
        public int DeleteForOwner(string ownerKey) => 0;
        public int DeleteForDocument(string documentId) => 0;
    }

    private sealed class NoGenerator : ICompositionGenerator
    {
        public Task<GeneratedComposition> GenerateAsync(CompositionKind kind, CompositionContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException();
    }

    private sealed class Fixture
    {
        public ManualClock Clock { get; } = new();
        public FakeStore Store { get; } = new();
        public List<CompositionTab> OpenTabs { get; } = [];
        public List<string> Copied { get; } = [];
        public List<string> Asked { get; } = [];
        public bool Answer { get; set; } = true;
        public List<string> Errors { get; } = [];
        public CompositionsViewModel Vm { get; }

        public Fixture()
        {
            Vm = new CompositionsViewModel(Store, new NoNotes(), new NoGenerator(), () => [], _ => { }, Errors.Add, Clock)
            {
                RunOnUi = action => action(),
                OpenTabFor = id => OpenTabs.FirstOrDefault(t => t.CompositionId == id),
                CloseTab = tab => OpenTabs.Remove(tab),
                CopyToClipboard = Copied.Add,
                ConfirmDelete = title => { Asked.Add(title); return Answer; },
            };
            Vm.LoadFor("ws");
        }

        public CompositionTab Add(string id, string content = "Nội dung [1] và [2]", string title = "Do AI đặt", bool open = true)
        {
            var composition = new Composition(id, "ws", title, CompositionKind.Summary, content, "m", ["n1", "n2"], 1);
            Store.Add(composition);
            Vm.LoadFor("ws");
            var tab = new CompositionTab(Vm.Items.Single(c => c.Id == id));
            if (open) OpenTabs.Add(tab);
            return tab;
        }
    }

    [Fact]
    public void Edit_TypingThenPausing_SavesContentAndEditTime()
    {
        var f = new Fixture();
        var tab = f.Add("c1");
        f.Vm.ToggleEditCommand.Execute(tab);
        f.Clock.NowMs = 5000;

        tab.EditText = "Nội dung đã sửa [1]";
        Assert.Equal("Nội dung [1] và [2]", f.Store.Rows.Single().Content);   // chưa hết quãng chờ
        f.Clock.ElapseDebounce();

        var saved = f.Store.Rows.Single();
        Assert.Equal(("Nội dung đã sửa [1]", (long?)5000), (saved.Content, saved.UpdatedAtUnixMs));
        Assert.Equal("Nội dung đã sửa [1]", tab.Composition.Content);
        Assert.Equal("Nội dung đã sửa [1]", f.Vm.Items.Single().Content);
        Assert.Contains(tab.Blocks, b => b.Text.Contains("đã sửa"));
    }

    [Fact]
    public void Edit_KeepTyping_RestartsTheQuietPeriodInsteadOfSavingEveryKey()
    {
        var f = new Fixture();
        var tab = f.Add("c1");
        f.Vm.ToggleEditCommand.Execute(tab);

        tab.EditText = "a";
        tab.EditText = "ab";

        Assert.Single(f.Clock.Timers);
    }

    [Fact]
    public void Edit_FinishingEditMode_SavesAtOnceWithoutWaiting()
    {
        var f = new Fixture();
        var tab = f.Add("c1");
        f.Vm.ToggleEditCommand.Execute(tab);
        tab.EditText = "Gõ vội";

        f.Vm.ToggleEditCommand.Execute(tab);

        Assert.False(tab.IsEditing);
        Assert.Equal("Gõ vội", f.Store.Rows.Single().Content);
        Assert.Empty(f.Clock.Timers);
    }

    [Fact]
    public void FlushPendingEdits_BeforeQuietPeriod_SavesWhatWasTyped()
    {
        var f = new Fixture();
        var tab = f.Add("c1");
        f.Vm.ToggleEditCommand.Execute(tab);
        tab.EditText = "Chuyển tab ngay";

        f.Vm.FlushPendingEdits();

        Assert.Equal("Chuyển tab ngay", f.Store.Rows.Single().Content);
        Assert.Empty(f.Clock.Timers);
        Assert.True(tab.IsEditing);
    }

    [Fact]
    public void FlushPendingEdits_WithNothingTyped_WritesNothing()
    {
        var f = new Fixture();
        var tab = f.Add("c1");
        f.Vm.ToggleEditCommand.Execute(tab);

        f.Vm.FlushPendingEdits();

        Assert.Null(f.Store.Rows.Single().UpdatedAtUnixMs);
    }

    [Fact]
    public void Rename_ShowsNewTitleOnTabAndRail_AndPersists()
    {
        var f = new Fixture();
        var tab = f.Add("c1");
        f.Clock.NowMs = 7000;

        f.Vm.StartRenameCommand.Execute(tab);
        Assert.True(tab.IsRenaming);
        Assert.Equal("Do AI đặt", tab.RenameText);
        tab.RenameText = "  Ôn tập chương 3 ";
        f.Vm.CommitRenameCommand.Execute(tab);

        Assert.False(tab.IsRenaming);
        Assert.Equal("Ôn tập chương 3", tab.Title);
        Assert.Equal("Ôn tập chương 3", f.Vm.Items.Single().Title);
        Assert.Equal(("Ôn tập chương 3", (long?)7000), (f.Store.Rows.Single().Title, f.Store.Rows.Single().UpdatedAtUnixMs));
    }

    [Fact]
    public void Rename_ToBlank_KeepsOldTitle()
    {
        var f = new Fixture();
        var tab = f.Add("c1");

        f.Vm.StartRenameCommand.Execute(tab);
        tab.RenameText = "   ";
        f.Vm.CommitRenameCommand.Execute(tab);

        Assert.Equal("Do AI đặt", tab.Title);
        Assert.Equal("Do AI đặt", f.Store.Rows.Single().Title);
        Assert.False(tab.IsRenaming);
    }

    [Fact]
    public void Rename_WhileEditPending_DoesNotLoseTypedContent()
    {
        var f = new Fixture();
        var tab = f.Add("c1");
        f.Vm.ToggleEditCommand.Execute(tab);
        tab.EditText = "Đang gõ dở";

        f.Vm.StartRenameCommand.Execute(tab);
        tab.RenameText = "Tên mới";
        f.Vm.CommitRenameCommand.Execute(tab);

        var saved = f.Store.Rows.Single();
        Assert.Equal(("Tên mới", "Đang gõ dở"), (saved.Title, saved.Content));
    }

    [Fact]
    public void Copy_PutsMarkdownWithCitationsOnClipboard()
    {
        var f = new Fixture();
        var tab = f.Add("c1", content: "## Ý chính\n- Một [1]\n- Hai [2]");

        f.Vm.CopyCommand.Execute(tab);

        Assert.Equal("## Ý chính\n- Một [1]\n- Hai [2]", Assert.Single(f.Copied));
    }

    [Fact]
    public void Copy_InEditMode_CopiesWhatIsOnScreen()
    {
        var f = new Fixture();
        var tab = f.Add("c1");
        f.Vm.ToggleEditCommand.Execute(tab);
        tab.EditText = "Bản đang sửa [1]";

        f.Vm.CopyCommand.Execute(tab);

        Assert.Equal("Bản đang sửa [1]", Assert.Single(f.Copied));
    }

    [Fact]
    public void Delete_Confirmed_RemovesFromStoreRailAndClosesItsTab()
    {
        var f = new Fixture();
        f.Add("keep", open: false);
        var tab = f.Add("c1", title: "Sẽ xoá");

        f.Vm.DeleteCommand.Execute(tab.Composition);

        Assert.Equal(["Sẽ xoá"], f.Asked);
        Assert.DoesNotContain(f.Store.Rows, c => c.Id == "c1");
        Assert.DoesNotContain(f.Vm.Items, c => c.Id == "c1");
        Assert.Empty(f.OpenTabs);
        Assert.Contains(f.Vm.Items, c => c.Id == "keep");
    }

    [Fact]
    public void Delete_Declined_ChangesNothing()
    {
        var f = new Fixture();
        var tab = f.Add("c1");
        f.Answer = false;

        f.Vm.DeleteCommand.Execute(tab.Composition);

        Assert.Single(f.Store.Rows);
        Assert.Single(f.Vm.Items);
        Assert.Same(tab, Assert.Single(f.OpenTabs));
    }

    [Fact]
    public void Delete_FromRailWhileNotOpen_LeavesOpenTabsAlone()
    {
        var f = new Fixture();
        var open = f.Add("open");
        var closed = f.Add("closed", open: false);

        f.Vm.DeleteCommand.Execute(closed.Composition);

        Assert.Equal("open", Assert.Single(f.Vm.Items).Id);
        Assert.Same(open, Assert.Single(f.OpenTabs));
    }

    [Fact]
    public void Delete_WithPendingEdit_DoesNotResurrectItOnLaterFlush()
    {
        var f = new Fixture();
        var tab = f.Add("c1");
        f.Vm.ToggleEditCommand.Execute(tab);
        tab.EditText = "Sắp bị xoá";

        f.Vm.DeleteCommand.Execute(tab.Composition);
        f.Vm.FlushPendingEdits();
        f.Clock.ElapseDebounce();

        Assert.Empty(f.Store.Rows);
        Assert.Empty(f.Vm.Items);
    }

    [Fact]
    public void DeleteForOwner_RemovesEveryCompositionOfTheWorkspace()
    {
        var f = new Fixture();
        f.Add("a");
        f.Add("b");

        f.Vm.DeleteForOwner("ws");

        Assert.Empty(f.Store.Rows);
        Assert.Empty(f.Vm.Items);
    }
}
