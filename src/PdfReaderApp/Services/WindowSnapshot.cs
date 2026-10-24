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
                Save(window, request.Path);
                app.Shutdown();
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
