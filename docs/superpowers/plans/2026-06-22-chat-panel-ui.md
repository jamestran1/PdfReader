# Panel chat: ẩn khi ở thư viện + chỉnh kích cỡ — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ẩn panel AI chat khi ở giao diện thư viện, và cho kéo chỉnh bề rộng panel (nhớ trong phiên).

**Architecture:** Bề rộng cột chat đưa vào `MainViewModel` dưới dạng `GridLength` bind hai chiều; `OnShowLibraryChanged` thu cột về 0 (lưu bề rộng cũ) khi vào thư viện và khôi phục khi rời. `MainWindow.xaml` thêm cột thứ 4 cho chat + `GridSplitter`, ẩn Card chat và splitter theo `ShowLibrary` qua `InverseBoolToVisibilityConverter` có sẵn.

**Tech Stack:** WPF, .NET net10.0-windows, CommunityToolkit.Mvvm, MaterialDesignThemes, xUnit.

## Global Constraints

- Comment/chuỗi tiếng Việt GIỮ DẤU (không strip ASCII).
- Không dùng dấu gạch ngang dài (em dash).
- Resize nhớ TRONG PHIÊN (không lưu settings).
- Giới hạn kéo: 280px (min) đến 700px (max). Mặc định 350px.
- Mặc định mở app ở thư viện (`ShowLibrary == true`) nên panel ẩn ngay lúc khởi động.
- Dùng converter có sẵn `InverseBoolToVisibilityConverter` (true → Collapsed). KHÔNG tạo converter mới.

---

### Task 1: Trạng thái bề rộng cột chat trong MainViewModel

**Files:**
- Modify: `src/PdfReaderApp/ViewModels/MainViewModel.cs`
- Test: `tests/PdfReaderApp.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `[ObservableProperty] bool _showLibrary` đã có (mặc định `true`), tạo sẵn partial `OnShowLibraryChanged`.
- Produces: `[ObservableProperty] System.Windows.GridLength ChatColumnWidth` (mặc định `new GridLength(0)`), `[ObservableProperty] double ChatColumnMinWidth` (mặc định `0`). Hai property này sẽ được `MainWindow.xaml` (Task 2) bind tới `ColumnDefinition.Width`/`MinWidth`.

- [ ] **Step 1: Viết test thất bại**

Trong `tests/PdfReaderApp.Tests/MainViewModelTests.cs`: thêm `using System.Windows;` vào đầu file (sau các using hiện có), rồi thêm 3 test vào trong lớp `MainViewModelTests` (trước dấu `}` đóng lớp):

```csharp
    [Fact]
    public void ChatColumn_DefaultsHidden_WhenLibraryShown()
    {
        var vm = new MainViewModel(); // ShowLibrary mặc định true
        Assert.Equal(0, vm.ChatColumnWidth.Value);
        Assert.Equal(0, vm.ChatColumnMinWidth);
    }

    [Fact]
    public void ChatColumn_RestoresDefaultWidth_WhenLeavingLibrary()
    {
        var vm = new MainViewModel();
        vm.ShowLibrary = false;
        Assert.Equal(350, vm.ChatColumnWidth.Value);
        Assert.Equal(280, vm.ChatColumnMinWidth);
    }

    [Fact]
    public void ChatColumn_RemembersResizedWidth_WithinSession()
    {
        var vm = new MainViewModel();
        vm.ShowLibrary = false;                          // hiện panel: 350
        vm.ChatColumnWidth = new GridLength(500);        // mô phỏng kéo GridSplitter
        vm.ShowLibrary = true;                           // vào thư viện: lưu 500, thu về 0
        Assert.Equal(0, vm.ChatColumnWidth.Value);
        vm.ShowLibrary = false;                          // rời thư viện: khôi phục 500
        Assert.Equal(500, vm.ChatColumnWidth.Value);
    }
```

- [ ] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests.ChatColumn"`
Expected: FAIL biên dịch (`ChatColumnWidth`/`ChatColumnMinWidth` chưa tồn tại).

- [ ] **Step 3: Thêm property + hằng số + field**

Trong `src/PdfReaderApp/ViewModels/MainViewModel.cs`, ngay sau khối:

```csharp
    [ObservableProperty]
    private bool _showLibrary = true;
```

thêm:

```csharp
    private const double DefaultChatWidthPx = 350;
    private const double MinChatWidthPx = 280;
    private double _savedChatWidthPx = DefaultChatWidthPx;

    // Bề rộng cột chat (bind hai chiều tới ColumnDefinition.Width trong XAML). Mặc định 0 vì
    // app mở ở thư viện -> panel ẩn. MinWidth cũng động: 0 khi ẩn, 280 khi hiện (nếu để cố định
    // 280 thì không thu cột về 0 được khi ẩn).
    [ObservableProperty]
    private System.Windows.GridLength _chatColumnWidth = new System.Windows.GridLength(0);

    [ObservableProperty]
    private double _chatColumnMinWidth = 0;

    // Vào thư viện: lưu bề rộng đang có rồi thu cột về 0. Rời thư viện: khôi phục bề rộng đã lưu.
    partial void OnShowLibraryChanged(bool value)
    {
        if (value)
        {
            if (ChatColumnWidth.IsAbsolute && ChatColumnWidth.Value > 0)
                _savedChatWidthPx = ChatColumnWidth.Value;
            ChatColumnWidth = new System.Windows.GridLength(0);
            ChatColumnMinWidth = 0;
        }
        else
        {
            ChatColumnMinWidth = MinChatWidthPx;
            ChatColumnWidth = new System.Windows.GridLength(_savedChatWidthPx);
        }
    }
```

- [ ] **Step 4: Chạy test, xác nhận đạt**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests.ChatColumn"`
Expected: PASS 3/3.

- [ ] **Step 5: Chạy toàn bộ test (không hồi quy)**

Run: `dotnet test`
Expected: PASS toàn bộ (số cũ + 3 test mới).

- [ ] **Step 6: Commit**

```bash
git add src/PdfReaderApp/ViewModels/MainViewModel.cs tests/PdfReaderApp.Tests/MainViewModelTests.cs
git commit -m "feat: VM state for collapsible/resizable chat column width"
```

---

### Task 2: Nối XAML — cột chat 4, GridSplitter, ẩn theo thư viện

**Files:**
- Modify: `src/PdfReaderApp/MainWindow.xaml`

**Interfaces:**
- Consumes: `ChatColumnWidth` (`GridLength`), `ChatColumnMinWidth` (`double`), `ShowLibrary` (`bool`) từ `MainViewModel` (Task 1); converter `InverseBoolToVisibilityConverter` đã đăng ký trong resources (đang dùng ở dòng ~246).
- Produces: layout 4 cột với panel chat ẩn khi `ShowLibrary` và kéo chỉnh được bằng `GridSplitter`.

**Bối cảnh hiện tại (đã đọc):**
- Root `Grid` (dòng 44) có 3 `ColumnDefinition`: `Auto`, `*`, `Auto` (dòng 45-49).
- Card chat ở dòng 298: `<materialDesign:Card Grid.Column="2" Width="350" Margin="4" materialDesign:ElevationAssist.Elevation="Dp1" UniformCornerRadius="12">`.

- [ ] **Step 1: Thêm cột thứ 4 cho chat, cột 2 thành splitter**

Thay khối (dòng 45-49):

```xml
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>
```

bằng:

```xml
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="{Binding ChatColumnWidth, Mode=TwoWay}"
                              MinWidth="{Binding ChatColumnMinWidth}" MaxWidth="700"/>
        </Grid.ColumnDefinitions>
```

- [ ] **Step 2: Đổi Card chat sang cột 3, bỏ Width cố định, ẩn theo thư viện**

Thay dòng 298:

```xml
        <materialDesign:Card Grid.Column="2" Width="350" Margin="4" materialDesign:ElevationAssist.Elevation="Dp1" UniformCornerRadius="12">
```

bằng:

```xml
        <materialDesign:Card Grid.Column="3" Margin="4" materialDesign:ElevationAssist.Elevation="Dp1" UniformCornerRadius="12"
                             Visibility="{Binding ShowLibrary, Converter={StaticResource InverseBoolToVisibilityConverter}}">
```

- [ ] **Step 3: Thêm GridSplitter (cột 2), ẩn theo thư viện**

Ngay TRƯỚC dòng `<!-- Right Sidebar (AI) -->` (dòng 297), thêm:

```xml
        <!-- Thanh kéo chỉnh bề rộng panel chat (ẩn cùng panel khi ở thư viện) -->
        <GridSplitter Grid.Column="2" Width="6" HorizontalAlignment="Stretch" VerticalAlignment="Stretch"
                      ResizeDirection="Columns" ResizeBehavior="PreviousAndNext"
                      Background="{DynamicResource MaterialDesignDivider}"
                      Visibility="{Binding ShowLibrary, Converter={StaticResource InverseBoolToVisibilityConverter}}"/>
```

- [ ] **Step 4: Build**

Run: `dotnet build PdfReaderApp.slnx`
Expected: build thành công (0 Errors).

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/MainWindow.xaml
git commit -m "feat: hide chat panel in library view, make it resizable via GridSplitter"
```

- [ ] **Step 6: Manual GUI verify (người dùng chạy app)**

1. Mở app (vào thư viện): KHÔNG thấy panel chat lẫn thanh splitter bên phải.
2. Mở một quyển sách: panel chat hiện lại; rê chuột vào thanh splitter (mép trái panel) kéo được, bề rộng đổi trong khoảng 280–700px.
3. Bấm nút Read về thư viện rồi mở sách lại: panel giữ đúng bề rộng vừa kéo (trong phiên).

---

## Self-Review

**1. Spec coverage:**
- Ẩn panel khi ở thư viện → Task 2 Step 2 + 3 (Visibility Card + GridSplitter theo ShowLibrary). ✅
- Resize bằng GridSplitter → Task 2 Step 1 + 3 (cột bind GridLength + GridSplitter). ✅
- Bề rộng đưa vào VM, logic ẩn/khôi phục ở VM → Task 1 (ChatColumnWidth/MinWidth + OnShowLibraryChanged). ✅
- Nhớ trong phiên → Task 1 (_savedChatWidthPx) + test ChatColumn_RemembersResizedWidth_WithinSession. ✅
- Giới hạn 280–700, mặc định 350 → Task 1 (consts) + Task 2 (MaxWidth=700). ✅
- Mặc định mở app panel ẩn → Task 1 (khởi tạo width 0/minWidth 0 khớp ShowLibrary=true). ✅
- Không tạo converter mới → Task 2 dùng InverseBoolToVisibilityConverter có sẵn. ✅

**2. Placeholder scan:** Không có TBD/TODO; mọi step có code/lệnh cụ thể.

**3. Type consistency:** `ChatColumnWidth` (`System.Windows.GridLength`), `ChatColumnMinWidth` (`double`), `OnShowLibraryChanged(bool)`, hằng `DefaultChatWidthPx=350`/`MinChatWidthPx=280` dùng nhất quán giữa Task 1, Task 2 và test. Binding TwoWay khớp việc GridSplitter ghi ngược ColumnDefinition.Width.
