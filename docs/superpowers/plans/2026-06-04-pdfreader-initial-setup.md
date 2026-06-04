# Khởi tạo Ứng dụng PDF Reader & Giao diện cơ bản (Initial Setup) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Khởi tạo project WPF C# với kiến trúc MVVM, giao diện cơ bản (PDF Viewer placeholder, Sidebar, Toolbar) và có thể build chạy thành công.
**Architecture:** Sử dụng kiến trúc MVVM, tách biệt UI và logic. Project WPF .NET 8/9.
**Tech Stack:** C#, .NET 8+, WPF, CommunityToolkit.Mvvm, xUnit (cho testing).

---

### Task 1: Khởi tạo Project và Solution

**Files:**
- Create: `PdfReaderApp.sln`
- Create: `src/PdfReaderApp/PdfReaderApp.csproj`
- Create: `tests/PdfReaderApp.Tests/PdfReaderApp.Tests.csproj`

- [ ] **Step 1: Tạo Solution và Projects bằng .NET CLI**

Run: 
```bash
dotnet new sln -n PdfReaderApp
dotnet new wpf -n PdfReaderApp -o src/PdfReaderApp
dotnet new xunit -n PdfReaderApp.Tests -o tests/PdfReaderApp.Tests
dotnet sln add src/PdfReaderApp/PdfReaderApp.csproj
dotnet sln add tests/PdfReaderApp.Tests/PdfReaderApp.Tests.csproj
dotnet add tests/PdfReaderApp.Tests/PdfReaderApp.Tests.csproj reference src/PdfReaderApp/PdfReaderApp.csproj
```
Expected: Solution created successfully.

- [ ] **Step 2: Cài đặt thư viện MVVM Community Toolkit**

Run:
```bash
dotnet add src/PdfReaderApp/PdfReaderApp.csproj package CommunityToolkit.Mvvm
```
Expected: Package installed.

- [ ] **Step 3: Run the baseline build to make sure it compiles**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "chore: initial wpf project setup with MVVM toolkit and xunit"
```

### Task 2: Thiết lập MVVM và MainViewModel

**Files:**
- Create: `src/PdfReaderApp/ViewModels/MainViewModel.cs`
- Modify: `src/PdfReaderApp/MainWindow.xaml`
- Modify: `src/PdfReaderApp/MainWindow.xaml.cs`
- Create: `tests/PdfReaderApp.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Viết test cho MainViewModel**

```csharp
// tests/PdfReaderApp.Tests/MainViewModelTests.cs
using PdfReaderApp.ViewModels;
using Xunit;

namespace PdfReaderApp.Tests;

public class MainViewModelTests
{
    [Fact]
    public void MainViewModel_ShouldInitializeWithDefaultValues()
    {
        var viewModel = new MainViewModel();
        Assert.Equal("PDF Reader & AI", viewModel.WindowTitle);
    }
}
```

- [ ] **Step 2: Chạy test để đảm bảo fail (TDD)**

Run: `dotnet test tests/PdfReaderApp.Tests`
Expected: FAIL (The type or namespace name 'MainViewModel' could not be found)

- [ ] **Step 3: Tạo MainViewModel**

```csharp
// src/PdfReaderApp/ViewModels/MainViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;

namespace PdfReaderApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string windowTitle = "PDF Reader & AI";
}
```

- [ ] **Step 4: Chạy test lại**

Run: `dotnet test tests/PdfReaderApp.Tests`
Expected: PASS

- [ ] **Step 5: Gắn ViewModel vào MainWindow**

```xml
<!-- src/PdfReaderApp/MainWindow.xaml -->
<Window x:Class="PdfReaderApp.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:local="clr-namespace:PdfReaderApp"
        xmlns:viewmodels="clr-namespace:PdfReaderApp.ViewModels"
        mc:Ignorable="d"
        Title="{Binding WindowTitle}" Height="720" Width="1080">
    <Window.DataContext>
        <viewmodels:MainViewModel />
    </Window.DataContext>
    <Grid>
        <TextBlock Text="Welcome to PDF Reader &amp; AI" HorizontalAlignment="Center" VerticalAlignment="Center" FontSize="24"/>
    </Grid>
</Window>
```

- [ ] **Step 6: Commit**

```bash
git add src/PdfReaderApp tests/PdfReaderApp.Tests
git commit -m "feat: setup MainViewModel and bindings"
```

### Task 3: Tạo cấu trúc giao diện chính (Layout)

**Files:**
- Modify: `src/PdfReaderApp/MainWindow.xaml`

- [ ] **Step 1: Cập nhật Grid layout chính trong MainWindow**

Chia giao diện thành 3 phần: Toolbar (Top), PDF Viewer (Middle-Left), AI Sidebar (Middle-Right).

```xml
<!-- Thay thế toàn bộ Grid trong src/PdfReaderApp/MainWindow.xaml -->
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="50"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="300"/>
        </Grid.ColumnDefinitions>

        <!-- Toolbar -->
        <Border Grid.Row="0" Grid.ColumnSpan="2" Background="#EEE" BorderBrush="#CCC" BorderThickness="0,0,0,1">
            <StackPanel Orientation="Horizontal" Margin="10,0">
                <Button Content="Open PDF" Margin="0,0,10,0" Padding="10,5"/>
                <Button Content="Highlight" Margin="0,0,10,0" Padding="10,5"/>
            </StackPanel>
        </Border>

        <!-- PDF Viewer Placeholder -->
        <Border Grid.Row="1" Grid.Column="0" Background="#525659">
            <TextBlock Text="PDF Viewer Component" Foreground="White" HorizontalAlignment="Center" VerticalAlignment="Center"/>
        </Border>

        <!-- AI Sidebar -->
        <Border Grid.Row="1" Grid.Column="1" Background="#F9F9F9" BorderBrush="#CCC" BorderThickness="1,0,0,0">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="*"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>
                <ScrollViewer Grid.Row="0" Margin="10">
                    <StackPanel>
                        <TextBlock Text="AI Chat" FontWeight="Bold" Margin="0,0,0,10"/>
                        <TextBlock Text="Xin chào! Bạn có câu hỏi nào về tài liệu này?" TextWrapping="Wrap"/>
                    </StackPanel>
                </ScrollViewer>
                <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="10">
                    <TextBox Width="200" Margin="0,0,10,0" Padding="5"/>
                    <Button Content="Gửi" Padding="10,5"/>
                </StackPanel>
            </Grid>
        </Border>
    </Grid>
```

- [ ] **Step 2: Build để kiểm tra syntax**

Run: `cd C:/Users/jamet/source/repos/pdfreader && dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/PdfReaderApp/MainWindow.xaml
git commit -m "feat: design main application layout"
```
