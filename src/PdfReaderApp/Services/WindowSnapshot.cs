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
            var capture = new DispatcherTimer { Interval = request.Delay };
            capture.Tick += (_, _) =>
            {
                capture.Stop();
                var exitCode = 1;
                try
                {
                    Save(window, request.Path);
                    exitCode = 0;
                }
                // Công cụ dev: lỗi gì cũng phải thoát kèm mã lỗi, không để app đứng ở hộp thoại lỗi.
#pragma warning disable CA1031 // ranh giới công cụ dev: ghi lỗi ra stderr và thoát với mã 1
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"TRITHU_SCREENSHOT: không lưu được ảnh: {ex}");
                }
#pragma warning restore CA1031
                finally
                {
                    app.Shutdown(exitCode);
                }
            };
            capture.Start();
        };
        timer.Start();
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
