using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PdfReaderApp.Core;

namespace PdfReaderApp.Services;

/// <summary>
/// Khi có <see cref="SnapshotRequest"/>: chờ cửa sổ chính hiện ra, render nó ra PNG bằng RenderTargetBitmap
/// rồi thoát app. Render từ trong app nên không chụp màn hình, không lộ cửa sổ khác trên desktop.
/// </summary>
internal static class WindowSnapshot
{
    public static void ScheduleIfRequested(Application app, SnapshotRequest? request)
    {
        if (request is null)
        {
            return;
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            if (app.MainWindow is not { IsLoaded: true } window)
            {
                return;
            }

            timer.Stop();
            // Cảnh chụp: mở tài liệu, chờ render, rồi đặt trang / zoom / tab panel, chờ tiếp rồi mới chụp.
            Step(app, () => OpenDocument(window, request), request.Delay,
                () => Step(app, () => ApplyView(window, request), request.Delay,
                    () => Step(app, () => Save(window, request.Path), TimeSpan.Zero, () => app.Shutdown(0))));
        };
        timer.Start();
    }

    // Chạy một bước rồi hẹn bước sau; lỗi ở bước nào cũng thoát với mã 1, không để app đứng ở hộp thoại lỗi.
    private static void Step(Application app, Action action, TimeSpan wait, Action next)
    {
        try
        {
            action();
        }
#pragma warning disable CA1031 // ranh giới công cụ dev: ghi lỗi ra stderr và thoát với mã 1
        catch (Exception ex)
        {
            Console.Error.WriteLine($"TRITHU_SCREENSHOT: lỗi khi dựng cảnh hoặc lưu ảnh: {ex}");
            app.Shutdown(1);
            return;
        }
#pragma warning restore CA1031

        if (wait <= TimeSpan.Zero)
        {
            next();
            return;
        }

        var timer = new DispatcherTimer { Interval = wait };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            next();
        };
        timer.Start();
    }

    private static void OpenDocument(Window window, SnapshotRequest request)
    {
        if (request.OpenPath is not { } path)
        {
            return;
        }

        if (window.DataContext is not ViewModels.MainViewModel vm)
        {
            throw new InvalidOperationException("DataContext của cửa sổ chính không phải MainViewModel.");
        }

        vm.ImportAndOpen(path);
        // Viewer giữ trang đích đặt trước khi nó nạp xong tài liệu (như khi nhảy tới trang của ghi chú);
        // đặt sau khi đã nạp thì đồng bộ cuộn kéo trang về chỗ đang hiển thị.
        if (request.Page is { } page)
        {
            vm.CurrentPage = page;
        }
        // ImportAndOpen báo lỗi bằng notification trên UI; đưa nó ra stderr để script thấy nguyên nhân.
        if (vm.CurrentNotification is { IsError: true } error)
        {
            throw new InvalidOperationException($"Mở {path} lỗi: {error.Message}");
        }
    }

    private static void ApplyView(Window window, SnapshotRequest request)
    {
        if (window.DataContext is not ViewModels.MainViewModel vm)
        {
            return;
        }

        if (request.Zoom is { } zoom)
        {
            vm.ZoomLevel = zoom;
        }

        if (request.RightTabIndex is { } tab)
        {
            vm.Notes.RightTabIndex = tab;
        }
    }

    private static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
