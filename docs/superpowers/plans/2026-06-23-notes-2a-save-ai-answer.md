# Notes Layer 2a — Lưu câu trả lời AI thành note (one-click) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bấm một icon trên tin nhắn AI là lưu thẳng câu trả lời thành note (không chuyển tab, không composer), báo bằng snackbar.

**Architecture:** Thêm `NotesViewModel.AddNote(content, quote, pageIndex)` tạo note trực tiếp; `MainViewModel.SaveAnswerAsNote` one-click + `SnackbarMessageQueue`; nút icon-only hover-reveal trên bong bóng AI. Dọn luồng composer của bản nháp 2a trước trên cùng nhánh.

**Tech Stack:** WPF .NET 10, CommunityToolkit.Mvvm, MaterialDesignThemes 5.1.0, xUnit.

## Global Constraints

- Comment/chuỗi UI tiếng Việt GIỮ DẤU. Không dùng dấu gạch ngang dài (em dash).
- Câu trả lời AI lưu vào `Content` của note; `Quote` và `PageIndex` = null (note AI không neo trang).
- One-click: KHÔNG chuyển tab, KHÔNG composer. Báo đã lưu bằng MaterialDesign Snackbar.
- Nút chỉ hiện trên tin nhắn `Role == "AI"` VÀ khi rê chuột vào bong bóng (hover).
- 2b (chọn text → composer + trích dẫn neo trang) phải GIỮ NGUYÊN hành vi sau khi dọn.
- Bối cảnh: nhánh `feature/notes-2a-save-ai-answer` đang chứa bản nháp 2a kiểu composer (BeginNoteFromText / _pendingPageIndex int? / SaveAnswerAsNote-qua-pending / nút chữ). Plan này thay nó.

---

### Task 1: NotesViewModel.AddNote + MainViewModel one-click + snackbar (dọn composer)

**Files:**
- Modify: `src/PdfReaderApp/ViewModels/NotesViewModel.cs`
- Modify: `src/PdfReaderApp/ViewModels/MainViewModel.cs`
- Test: `tests/PdfReaderApp.Tests/ViewModels/NotesViewModelTests.cs`, `tests/PdfReaderApp.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `Note` (có Quote), `INoteStore`, `ChatMessage` (Role/Content), `MatchesFilter`/`InsertSorted`/`_all`/`_ownerKey` (sẵn có).
- Produces:
  - `NotesViewModel.AddNote(string content, string? quote, int? pageIndex) : bool` (tạo note trực tiếp, trả true nếu lưu).
  - `MainViewModel.SaveAnswerAsNoteCommand` (RelayCommand `ChatMessage?`) one-click; `MainViewModel.NotesSnackbar` (`MaterialDesignThemes.Wpf.SnackbarMessageQueue`).
  - Dọn: bỏ `BeginNoteFromText`; `BeginNoteFromSelection` về thân inline; `_pendingPageIndex` về `int`.

- [ ] **Step 1: Viết test thất bại + xóa test cũ của bản nháp**

1a. Trong `tests/PdfReaderApp.Tests/ViewModels/NotesViewModelTests.cs`: XÓA 3 test của bản nháp composer: `BeginNoteFromText_NullPage_SetsPendingAndTab`, `Save_FromTextNullPage_CreatesNoteWithNoPageAnchor`, `Save_FromTextNullPage_EmptyDraft_StillCreates`. Thêm test mới cho `AddNote`:

```csharp
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
        var vm = Make(store, page: 1);
        vm.LoadFor(null); // chưa mở sách

        bool ok = vm.AddNote("x", null, null);

        Assert.False(ok);
        Assert.Empty(store.Rows);
    }

    [Fact]
    public void AddNote_EmptyContent_ReturnsFalse()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 1);
        vm.LoadFor("doc1");

        Assert.False(vm.AddNote("   ", null, null));
        Assert.Empty(store.Rows);
    }

    [Fact]
    public void AddNote_RespectsActiveFilter()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 1);
        vm.LoadFor("doc1");
        vm.FilterText = "xyz"; // không khớp note mới

        bool ok = vm.AddNote("nội dung khác", null, null);

        Assert.True(ok);                       // vẫn lưu vào store
        Assert.Empty(vm.Items);                // nhưng bị lọc khỏi danh sách hiển thị
    }
```

1b. Trong `tests/PdfReaderApp.Tests/MainViewModelTests.cs`: XÓA 2 test của bản nháp: `SaveAnswerAsNote_AiMessage_SetsPendingQuoteAndOpensNotesTab` và `SaveAnswerAsNote_NonAiOrEmpty_DoesNothing` (luồng đã đổi sang one-click cần sách thật; glue verify ở GUI). Không thêm test MainViewModel mới.

- [ ] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test --filter "FullyQualifiedName~NotesViewModelTests.AddNote"`
Expected: FAIL biên dịch (`AddNote` chưa tồn tại).

- [ ] **Step 3: Thêm AddNote + dọn composer trong NotesViewModel**

Trong `src/PdfReaderApp/ViewModels/NotesViewModel.cs`:

3a. Đổi `private int? _pendingPageIndex;` (dòng ~35) về:

```csharp
    private int _pendingPageIndex;
```

3b. Thay khối `BeginNoteFromText` + wrapper `BeginNoteFromSelection` (dòng ~88-100) bằng `BeginNoteFromSelection` inline (như 2b gốc) + `AddNote`:

```csharp
    // Bắt đầu tạo note từ vùng chọn trang: chuyển sang tab Notes, giữ trích dẫn + trang chờ.
    public void BeginNoteFromSelection(string quote, int pageIndex)
    {
        CancelEdit();
        PendingQuote = quote;
        _pendingPageIndex = pageIndex;
        RightTabIndex = 1; // 0=Chat, 1=Notes
    }

    // Tạo note trực tiếp (không qua Draft/composer). Dùng cho one-click lưu câu trả lời AI.
    // Trả true nếu đã lưu (đang mở sách + content không rỗng).
    public bool AddNote(string content, string? quote, int? pageIndex)
    {
        if (_ownerKey == null) return false;
        string text = (content ?? string.Empty).Trim();
        if (text.Length == 0) return false;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var note = new Note(Guid.NewGuid().ToString("N"), _ownerKey, _ownerKey, pageIndex, quote, text, now, now);
        try { _store.Add(note); }
        catch { return false; }
        _all.Add(note);
        if (MatchesFilter(note, FilterText)) InsertSorted(note);
        return true;
    }
```

(Lưu ý: `Save` vẫn dùng `int? page = hasQuote ? _pendingPageIndex : _currentPageIndex();` — `_pendingPageIndex` giờ là `int` vẫn hợp lệ vì kết quả ternary là `int?`.)

- [ ] **Step 4: Sửa SaveAnswerAsNote + thêm NotesSnackbar trong MainViewModel**

Trong `src/PdfReaderApp/ViewModels/MainViewModel.cs`:

4a. Thêm property snackbar (cạnh các property khác, ví dụ gần `public NotesViewModel Notes { get; }`):

```csharp
    public MaterialDesignThemes.Wpf.SnackbarMessageQueue NotesSnackbar { get; } = new();
```

4b. Thay method `SaveAnswerAsNote` hiện có (đang gọi `Notes.BeginNoteFromText(...)`) bằng:

```csharp
    // One-click lưu câu trả lời AI thành note (không chuyển tab); báo bằng snackbar.
    [RelayCommand]
    private void SaveAnswerAsNote(ChatMessage? msg)
    {
        if (msg is null || msg.Role != "AI" || string.IsNullOrWhiteSpace(msg.Content)) return;
        bool saved = Notes.AddNote(msg.Content, null, null);
        NotesSnackbar.Enqueue(saved ? "Đã lưu vào ghi chú" : "Hãy mở một tài liệu để lưu ghi chú");
    }
```

- [ ] **Step 5: Chạy test mới + toàn bộ**

Run: `dotnet test --filter "FullyQualifiedName~NotesViewModelTests.AddNote"`
Expected: PASS 4/4.

Run: `dotnet test`
Expected: PASS toàn bộ (test 2b BeginNoteFromSelection vẫn xanh; không còn test composer cũ).

- [ ] **Step 6: Commit**

```bash
git add src/PdfReaderApp/ViewModels/NotesViewModel.cs src/PdfReaderApp/ViewModels/MainViewModel.cs tests/PdfReaderApp.Tests/ViewModels/NotesViewModelTests.cs tests/PdfReaderApp.Tests/MainViewModelTests.cs
git commit -m "feat: one-click AddNote + SaveAnswerAsNote + snackbar (drop composer path)"
```

---

### Task 2: Nút icon-only hover + Snackbar (XAML)

**Files:**
- Modify: `src/PdfReaderApp/MainWindow.xaml`

**Interfaces:**
- Consumes: `SaveAnswerAsNoteCommand`, `NotesSnackbar` (Task 1); `ChatMessage.Role`.

**Bối cảnh:** Trong tab Chat, DataTemplate bong bóng là `materialDesign:Card` chứa `StackPanel` (TextBlock Role + TextBlock Content). Bản nháp 2a trước đã thêm một nút CHỮ "Lưu thành ghi chú" với `Button.Style` + DataTrigger đơn `Role == "AI"` — cần THAY bằng nút icon-only hover-reveal. Vùng chat nằm trong TabItem "Chat" của TabControl sidebar phải.

- [ ] **Step 1: Thay nút chữ bằng nút icon-only hover-reveal**

Trong DataTemplate bong bóng chat, XÓA nút "Lưu thành ghi chú" kiểu chữ của bản nháp trước, thay bằng nút icon-only ẩn mặc định, chỉ hiện khi (Role == "AI") VÀ (đang hover vào Card bong bóng):

```xml
                                                <Button HorizontalAlignment="Right" Margin="0,2,0,0"
                                                        Width="28" Height="28" Padding="0"
                                                        ToolTip="Lưu thành ghi chú"
                                                        Command="{Binding DataContext.SaveAnswerAsNoteCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                                        CommandParameter="{Binding}">
                                                    <Button.Style>
                                                        <Style TargetType="Button" BasedOn="{StaticResource MaterialDesignIconButton}">
                                                            <Setter Property="Visibility" Value="Collapsed"/>
                                                            <Style.Triggers>
                                                                <MultiDataTrigger>
                                                                    <MultiDataTrigger.Conditions>
                                                                        <Condition Binding="{Binding Role}" Value="AI"/>
                                                                        <Condition Binding="{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=materialDesign:Card}}" Value="True"/>
                                                                    </MultiDataTrigger.Conditions>
                                                                    <Setter Property="Visibility" Value="Visible"/>
                                                                </MultiDataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </Button.Style>
                                                    <materialDesign:PackIcon Kind="NotePlusOutline" Width="16" Height="16"/>
                                                </Button>
```

- [ ] **Step 2: Thêm Snackbar overlay cho vùng chat**

Đặt `materialDesign:Snackbar` chồng đáy vùng chat. Trong TabItem "Chat", nếu nội dung là một `Grid`, bọc/đặt Snackbar ở cuối Grid đó (cùng cell, dưới cùng) để nó nổi lên trên:

```xml
<materialDesign:Snackbar MessageQueue="{Binding NotesSnackbar}"
                         VerticalAlignment="Bottom" HorizontalAlignment="Stretch"/>
```

(Đặt như phần tử cuối trong Grid của TabItem Chat để vẽ trên cùng. Nếu TabItem Chat hiện không phải Grid bao ngoài, bọc nội dung chat trong một `<Grid>...</Grid>` rồi thêm Snackbar làm con cuối.)

- [ ] **Step 3: Build**

Run: `dotnet build PdfReaderApp.slnx`
Expected: build thành công (0 Errors).

- [ ] **Step 4: Commit**

```bash
git add src/PdfReaderApp/MainWindow.xaml
git commit -m "feat: hover icon button on AI bubbles + snackbar for one-click save"
```

- [ ] **Step 5: Manual GUI verify (người dùng chạy app)**

1. Rê chuột vào bong bóng AI → hiện icon nhỏ (góc phải); rời chuột → ẩn; bong bóng User không bao giờ có.
2. Bấm icon → KHÔNG chuyển tab; snackbar "Đã lưu vào ghi chú" hiện ở đáy vùng chat rồi tự ẩn.
3. Mở tab Notes → thấy note mới = câu trả lời AI, KHÔNG có badge "Trang N".
4. Không hỏng: 2b chọn text → note trích dẫn vẫn neo trang; lọc/sửa/xóa note vẫn chạy; chuyển tab Chat/Notes không reset cuộn chat.

---

## Self-Review

**1. Spec coverage:**
- One-click tạo note (Content = câu trả lời AI, không quote/trang) → Task 1 `AddNote` + `SaveAnswerAsNote`. ✅
- Không chuyển tab → SaveAnswerAsNote không đụng RightTabIndex. ✅
- Snackbar xác nhận → Task 1 NotesSnackbar + Task 2 Snackbar control. ✅
- Nút icon-only hover chỉ trên AI → Task 2 MultiDataTrigger (Role==AI + Card.IsMouseOver). ✅
- Dọn composer (BeginNoteFromText/pending), 2b giữ nguyên → Task 1 Step 3 + chạy full suite. ✅
- Note làm nguồn cho AI / citations → KHÔNG làm (backlog Layer 4). ✅

**2. Placeholder scan:** Không TBD/TODO; code/lệnh cụ thể. Ghi chú Task 2 Step 2 về bọc Grid là hướng dẫn đặt Snackbar đúng chỗ (đọc file để áp), không phải nội dung thiếu.

**3. Type consistency:** `AddNote(string, string?, int?) : bool`; `SaveAnswerAsNoteCommand` (ChatMessage?); `NotesSnackbar` (SnackbarMessageQueue); `BeginNoteFromSelection(string,int)` inline; `_pendingPageIndex` int — nhất quán Task 1/2 và test.
