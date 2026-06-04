# Tích hợp PDF Viewer thực tế (PDF Integration) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Thay thế placeholder bằng control hiển thị PDF thực tế, cho phép chọn file và xem nội dung trong ứng dụng.
**Architecture:** Tạo một WPF UserControl (MVVM Wrapper) bao bọc thư viện PdfiumViewer.
**Tech Stack:** C#, WPF, PdfiumViewer.Net.WPF.

---

### Task 1: Cài đặt thư viện PDFium

**Files:**
- Modify: `src/PdfReaderApp/PdfReaderApp.csproj`

- [x] **Step 1: Cài đặt các gói NuGet cần thiết**

Run:
```bash
dotnet add src/PdfReaderApp/PdfReaderApp.csproj package PdfiumViewer.Net.WPF
```
*Lưu ý: Bản Net.WPF tích hợp sẵn native DLLs và tối ưu cho .NET 5+.*

- [x] **Step 2: Build để kiểm tra nạp thư viện native**

Run: `dotnet build`
Expected: Build succeeded.

- [x] **Step 3: Commit**

```bash
git add src/PdfReaderApp/PdfReaderApp.csproj
git commit -m "chore: add PdfiumViewer dependencies"
```

### Task 2: Tạo PdfViewerControl (MVVM Wrapper)

**Files:**
- Create: `src/PdfReaderApp/Controls/PdfViewerControl.xaml`
- Create: `src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs`

- [x] **Step 1: Tạo thư mục Controls**

Run: `mkdir src/PdfReaderApp/Controls`

- [x] **Step 2: Tạo giao diện XAML cho Control**

```xml
<!-- src/PdfReaderApp/Controls/PdfViewerControl.xaml -->
<UserControl x:Class="PdfReaderApp.Controls.PdfViewerControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" 
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008" 
             mc:Ignorable="d" d:DesignHeight="450" d:DesignWidth="800">
    <Grid Background="#525659">
        <ScrollViewer x:Name="PagesScrollViewer" HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Auto">
            <ItemsControl x:Name="PagesContainer">
                <!-- Nội dung trang PDF sẽ được render động vào đây -->
            </ItemsControl>
        </ScrollViewer>
    </Grid>
</UserControl>
```

- [x] **Step 3: Triển khai logic nạp PDF trong Code-behind (Dependency Property)**

```csharp
// src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs
using System.Windows;
using System.Windows.Controls;
using PdfiumViewer;

namespace PdfReaderApp.Controls;

public partial class PdfViewerControl : UserControl
{
    public static readonly DependencyProperty DocumentSourceProperty =
        DependencyProperty.Register("DocumentSource", typeof(string), typeof(PdfViewerControl), 
            new PropertyMetadata(null, OnDocumentSourceChanged));

    public string DocumentSource
    {
        get => (string)GetValue(DocumentSourceProperty);
        set => SetValue(DocumentSourceProperty, value);
    }

    public PdfViewerControl()
    {
        InitializeComponent();
    }

    private static void OnDocumentSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfViewerControl control && e.NewValue is string path && !string.IsNullOrEmpty(path))
        {
            control.LoadDocument(path);
        }
    }

    private void LoadDocument(string path)
    {
        // Logic render đơn giản: Hiển thị tên file để verify binding trước
        // (Sẽ triển khai render hình ảnh thực tế ở task sau)
        System.Diagnostics.Debug.WriteLine($"Loading PDF: {path}");
    }
}
```

- [x] **Step 4: Commit**

```bash
git add src/PdfReaderApp/Controls
git commit -m "feat: create basic PdfViewerControl structure"
```

### Task 3: Cập nhật ViewModel và Mở file PDF

**Files:**
- Modify: `src/PdfReaderApp/ViewModels/MainViewModel.cs`
- Modify: `src/PdfReaderApp/MainWindow.xaml`

- [x] **Step 1: Cập nhật MainViewModel với OpenCommand**

```csharp
// src/PdfReaderApp/ViewModels/MainViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace PdfReaderApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string windowTitle = "PDF Reader & AI";

    [ObservableProperty]
    private string? filePath;

    [RelayCommand]
    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            FilePath = dialog.FileName;
        }
    }
}
```

- [x] **Step 2: Kết nối Binding trong MainWindow**

```xml
<!-- src/PdfReaderApp/MainWindow.xaml -->
<!-- 1. Thêm namespace Controls -->
xmlns:controls="clr-namespace:PdfReaderApp.Controls"

<!-- 2. Gán Command cho nút Open -->
<Button Content="Open PDF" Command="{Binding OpenFileCommand}" .../>

<!-- 3. Thay thế placeholder bằng PdfViewerControl -->
<controls:PdfViewerControl Grid.Row="1" Grid.Column="0" DocumentSource="{Binding FilePath}"/>
```

- [x] **Step 3: Chạy ứng dụng và verify nhấn nút Open chọn được file**

Run: `dotnet run --project src/PdfReaderApp`
Expected: Nhấn nút "Open PDF" mở được dialog chọn file, Console output hiện "Loading PDF: ...".

- [x] **Step 4: Commit**

```bash
git add src/PdfReaderApp
git commit -m "feat: implement open file logic with binding to PdfViewerControl"
```
