# Notes Layer 2a — Lưu câu trả lời AI thành note — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mỗi tin nhắn AI có nút "Lưu thành ghi chú": bấm mở tab Notes, câu trả lời AI vào banner trích dẫn (Quote), thêm bình luận rồi lưu; note không neo trang.

**Architecture:** Tổng quát hóa luồng pending-quote của 2b để neo trang có thể null (`BeginNoteFromText(quote, int?)`). Thêm `MainViewModel.SaveAnswerAsNote(ChatMessage)`. Thêm nút trong DataTemplate bong bóng chat, chỉ hiện trên tin nhắn AI.

**Tech Stack:** WPF .NET 10, CommunityToolkit.Mvvm, MaterialDesignThemes 5.1.0, xUnit.

## Global Constraints

- Comment/chuỗi UI tiếng Việt GIỮ DẤU. Không dùng dấu gạch ngang dài (em dash).
- Note từ câu trả lời AI: KHÔNG neo trang (PageIndex null). Câu trả lời AI lưu vào trường `Quote`; bình luận người dùng vào `Content`.
- Tái dùng banner "Trích dẫn" + khối quote thẻ note đã có (2b). KHÔNG làm citations/nguồn (backlog Layer 4).
- Nút chỉ hiện trên tin nhắn `Role == "AI"`. Lệnh chặn msg null / không phải AI / Content rỗng.
- Hành vi 2b (`BeginNoteFromSelection` neo trang) KHÔNG đổi.

---

### Task 1: NotesViewModel nullable-page + MainViewModel.SaveAnswerAsNote

**Files:**
- Modify: `src/PdfReaderApp/ViewModels/NotesViewModel.cs`
- Modify: `src/PdfReaderApp/ViewModels/MainViewModel.cs`
- Test: `tests/PdfReaderApp.Tests/ViewModels/NotesViewModelTests.cs`, `tests/PdfReaderApp.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `Note` (có Quote), `NotesViewModel.Save`/`PendingQuote`/`RightTabIndex` (2b), `ChatMessage` (`Role`, `Content`).
- Produces:
  - `NotesViewModel.BeginNoteFromText(string quote, int? pageIndex)` (set PendingQuote + pending page + RightTabIndex=1); `_pendingPageIndex` đổi sang `int?`; `BeginNoteFromSelection(string, int)` thành wrapper.
  - `MainViewModel.SaveAnswerAsNoteCommand` (RelayCommand nhận `ChatMessage?`).

- [ ] **Step 1: Viết test thất bại (NotesViewModel + MainViewModel)**

Thêm vào `tests/PdfReaderApp.Tests/ViewModels/NotesViewModelTests.cs`:

```csharp
    [Fact]
    public void BeginNoteFromText_NullPage_SetsPendingAndTab()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 9);
        vm.LoadFor("doc1");

        vm.BeginNoteFromText("câu trả lời AI", null);

        Assert.Equal("câu trả lời AI", vm.PendingQuote);
        Assert.Equal(1, vm.RightTabIndex);
    }

    [Fact]
    public void Save_FromTextNullPage_CreatesNoteWithNoPageAnchor()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 9); // trang hiện tại 9 nhưng note AI không neo trang
        vm.LoadFor("doc1");
        vm.BeginNoteFromText("câu trả lời AI", null);
        vm.Draft = "ý của tôi";

        vm.SaveCommand.Execute(null);

        var saved = store.Rows.Single();
        Assert.Equal("câu trả lời AI", saved.Quote);
        Assert.Null(saved.PageIndex);            // không neo trang dù trang hiện tại = 9
        Assert.Equal("ý của tôi", saved.Content);
        Assert.Null(vm.PendingQuote);
    }

    [Fact]
    public void Save_FromTextNullPage_EmptyDraft_StillCreates()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 1);
        vm.LoadFor("doc1");
        vm.BeginNoteFromText("chỉ câu trả lời", null);

        vm.SaveCommand.Execute(null);

        Assert.Single(store.Rows);
        Assert.Equal("chỉ câu trả lời", store.Rows[0].Quote);
        Assert.Null(store.Rows[0].PageIndex);
    }
```

Thêm vào `tests/PdfReaderApp.Tests/MainViewModelTests.cs` (thêm `using PdfReaderApp.ViewModels;` nếu thiếu — file đã dùng `MainViewModel` nên thường đã có):

```csharp
    [Fact]
    public void SaveAnswerAsNote_AiMessage_SetsPendingQuoteAndOpensNotesTab()
    {
        var vm = new MainViewModel();
        var msg = new ChatMessage { Role = "AI", Content = "Đây là câu trả lời." };

        vm.SaveAnswerAsNoteCommand.Execute(msg);

        Assert.Equal("Đây là câu trả lời.", vm.Notes.PendingQuote);
        Assert.Equal(1, vm.Notes.RightTabIndex);
    }

    [Fact]
    public void SaveAnswerAsNote_NonAiOrEmpty_DoesNothing()
    {
        var vm = new MainViewModel();

        vm.SaveAnswerAsNoteCommand.Execute(new ChatMessage { Role = "User", Content = "hỏi" });
        vm.SaveAnswerAsNoteCommand.Execute(new ChatMessage { Role = "AI", Content = "   " });
        vm.SaveAnswerAsNoteCommand.Execute(null);

        Assert.Null(vm.Notes.PendingQuote);
    }
```

- [ ] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test --filter "FullyQualifiedName~NotesViewModelTests.BeginNoteFromText|FullyQualifiedName~NotesViewModelTests.Save_FromText|FullyQualifiedName~MainViewModelTests.SaveAnswerAsNote"`
Expected: FAIL biên dịch (`BeginNoteFromText` / `SaveAnswerAsNoteCommand` chưa tồn tại).

- [ ] **Step 3: Sửa NotesViewModel**

Trong `src/PdfReaderApp/ViewModels/NotesViewModel.cs`:

3a. Đổi khai báo trường `_pendingPageIndex` từ:

```csharp
    private int _pendingPageIndex;
```

thành:

```csharp
    private int? _pendingPageIndex;
```

3b. Thay method `BeginNoteFromSelection` hiện tại:

```csharp
    // Bắt đầu tạo note từ vùng chọn: chuyển sang tab Notes, giữ trích dẫn + trang chờ.
    public void BeginNoteFromSelection(string quote, int pageIndex)
    {
        CancelEdit();
        PendingQuote = quote;
        _pendingPageIndex = pageIndex;
        RightTabIndex = 1; // 0=Chat, 1=Notes
    }
```

bằng:

```csharp
    // Bắt đầu tạo note từ một đoạn text bất kỳ (vùng chọn trang hoặc câu trả lời AI).
    // pageIndex = null nghĩa là không neo trang.
    public void BeginNoteFromText(string quote, int? pageIndex)
    {
        CancelEdit();
        PendingQuote = quote;
        _pendingPageIndex = pageIndex;
        RightTabIndex = 1; // 0=Chat, 1=Notes
    }

    // Tạo note từ vùng chọn trang (luôn neo trang chứa đoạn chọn).
    public void BeginNoteFromSelection(string quote, int pageIndex)
        => BeginNoteFromText(quote, pageIndex);
```

(`Save` giữ NGUYÊN: dòng `int? page = hasQuote ? _pendingPageIndex : _currentPageIndex();` vẫn đúng vì `_pendingPageIndex` giờ là `int?`.)

- [ ] **Step 4: Thêm command vào MainViewModel**

Trong `src/PdfReaderApp/ViewModels/MainViewModel.cs`, cạnh `AddNoteFromSelection` (hoặc các RelayCommand khác), thêm:

```csharp
    // Lưu một câu trả lời AI thành note: mở tab Notes, đưa nội dung vào banner trích dẫn (không neo trang).
    [RelayCommand]
    private void SaveAnswerAsNote(ChatMessage? msg)
    {
        if (msg is null || msg.Role != "AI" || string.IsNullOrWhiteSpace(msg.Content)) return;
        Notes.BeginNoteFromText(msg.Content, null);
    }
```

- [ ] **Step 5: Chạy test mới, xác nhận đạt**

Run: `dotnet test --filter "FullyQualifiedName~NotesViewModelTests.BeginNoteFromText|FullyQualifiedName~NotesViewModelTests.Save_FromText|FullyQualifiedName~MainViewModelTests.SaveAnswerAsNote"`
Expected: PASS (5 test mới).

- [ ] **Step 6: Chạy toàn bộ test (không hồi quy 2b)**

Run: `dotnet test`
Expected: PASS toàn bộ (gồm test 2b `BeginNoteFromSelection` vẫn xanh).

- [ ] **Step 7: Commit**

```bash
git add src/PdfReaderApp/ViewModels/NotesViewModel.cs src/PdfReaderApp/ViewModels/MainViewModel.cs tests/PdfReaderApp.Tests/ViewModels/NotesViewModelTests.cs tests/PdfReaderApp.Tests/MainViewModelTests.cs
git commit -m "feat: SaveAnswerAsNote command + NotesViewModel.BeginNoteFromText (nullable page)"
```

---

### Task 2: Nút "Lưu thành ghi chú" trên bong bóng AI (XAML)

**Files:**
- Modify: `src/PdfReaderApp/MainWindow.xaml`

**Interfaces:**
- Consumes: `MainViewModel.SaveAnswerAsNoteCommand` (Task 1); `ChatMessage.Role`/`Content`.

**Bối cảnh hiện tại (đọc trước khi sửa):** Trong tab Chat, `ItemsControl ItemsSource="{Binding ChatMessages}"` (khoảng dòng 322). DataTemplate là `materialDesign:Card` chứa `StackPanel` với `TextBlock Text="{Binding Role}"` và `TextBlock Text="{Binding Content}"` (khoảng dòng 329-331).

- [ ] **Step 1: Thêm nút vào DataTemplate bong bóng chat**

Trong `StackPanel` của DataTemplate (sau `TextBlock Text="{Binding Content}" .../>`), thêm nút icon chỉ hiện trên tin nhắn AI:

```xml
                                                <Button HorizontalAlignment="Right" Margin="0,4,0,0"
                                                        Style="{StaticResource MaterialDesignFlatButton}"
                                                        Padding="6,2" FontSize="11"
                                                        ToolTip="Lưu thành ghi chú"
                                                        Command="{Binding DataContext.SaveAnswerAsNoteCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                                        CommandParameter="{Binding}">
                                                    <Button.Style>
                                                        <Style TargetType="Button" BasedOn="{StaticResource MaterialDesignFlatButton}">
                                                            <Setter Property="Visibility" Value="Collapsed"/>
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding Role}" Value="AI">
                                                                    <Setter Property="Visibility" Value="Visible"/>
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </Button.Style>
                                                    <StackPanel Orientation="Horizontal">
                                                        <materialDesign:PackIcon Kind="NotePlusOutline" Width="14" Height="14" VerticalAlignment="Center"/>
                                                        <TextBlock Text="Lưu thành ghi chú" Margin="4,0,0,0" VerticalAlignment="Center"/>
                                                    </StackPanel>
                                                </Button>
```

(Ghi chú: đặt cả `Style` lồng `BasedOn` MaterialDesignFlatButton để vừa giữ style phẳng vừa thêm DataTrigger Visibility; nếu `Style` lồng gây trùng với thuộc tính `Style=` ở trên thì BỎ thuộc tính `Style="{StaticResource MaterialDesignFlatButton}"` ngoài, chỉ giữ `Button.Style`.)

- [ ] **Step 2: Build**

Run: `dotnet build PdfReaderApp.slnx`
Expected: build thành công (0 Errors).

- [ ] **Step 3: Commit**

```bash
git add src/PdfReaderApp/MainWindow.xaml
git commit -m "feat: 'save answer as note' button on AI chat bubbles"
```

- [ ] **Step 4: Manual GUI verify (người dùng chạy app)**

1. Mở sách, chat một câu → tin nhắn AI có nút "Lưu thành ghi chú"; tin nhắn User KHÔNG có nút.
2. Bấm nút → chuyển sang tab Notes, banner "Trích dẫn" hiện đúng câu trả lời AI.
3. Gõ bình luận, Ctrl+Enter → thẻ note có khối trích dẫn (câu trả lời AI, cắt ~3 dòng) + bình luận, KHÔNG có badge "Trang N".
4. Lưu khi để trống bình luận (chỉ trích dẫn) → vẫn tạo note.
5. Không hỏng: chọn text trên trang → note trích dẫn (2b) vẫn neo trang đúng.

---

## Self-Review

**1. Spec coverage:**
- Lưu câu trả lời AI qua composer, AI text làm Quote → Task 1 (BeginNoteFromText) + Task 2 (nút). ✅
- Không neo trang (PageIndex null) → Task 1 (`_pendingPageIndex` int? = null) + test Save_FromTextNullPage. ✅
- Nút chỉ trên bong bóng AI → Task 2 DataTrigger Role=="AI". ✅
- Lệnh chặn non-AI/rỗng/null → Task 1 SaveAnswerAsNote + test. ✅
- 2b không đổi (BeginNoteFromSelection neo trang) → Task 1 wrapper + chạy full suite. ✅
- Tái dùng banner/khối quote → Task 2 (không thêm UI quote mới). ✅

**2. Placeholder scan:** Không có TBD/TODO; mọi step có code/lệnh. Ghi chú ở Task 2 Step 1 về `Style` lồng là hướng dẫn xử lý xung đột XAML cụ thể, không phải nội dung thiếu.

**3. Type consistency:** `BeginNoteFromText(string, int?)`, `BeginNoteFromSelection(string, int)` wrapper, `_pendingPageIndex` int?, `SaveAnswerAsNoteCommand` (RelayCommand `ChatMessage?`), `Notes.PendingQuote`/`RightTabIndex` — nhất quán giữa Task 1, Task 2 và test.
