using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace PdfReaderApp;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        // MessageBox.Show("App constructor called!");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // MessageBox.Show("App OnStartup called!");
        base.OnStartup(e);

        ApplyTriThuSurfaceColors();

        this.DispatcherUnhandledException += (s, args) =>
        {
            LogAndShowException(args.Exception, "Dispatcher Unhandled Exception");
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LogAndShowException(ex, "Domain Unhandled Exception");
            }
        };
    }

    // Đẩy màu surface/on-surface của design vào control built-in MDT
    // (CustomColorTheme chỉ seed primary/secondary; surface phải set qua PaletteHelper).
    // Lưu ý MDT 5.1: Theme chỉ expose Background/Foreground/ForegroundLight làm màu toàn cục —
    // KHÔNG có Outline/Divider. Outline/divider sống ở token app-owned TriThu.Brush.Outline(.Variant)
    // trong Themes/TriThuTokens.xaml; các slice reskin #60–#67 bind vào đó.
    private static void ApplyTriThuSurfaceColors()
    {
        var paletteHelper = new PaletteHelper();
        Theme theme = paletteHelper.GetTheme();
        theme.Background = (Color)ColorConverter.ConvertFromString("#F4FBF7")!; // surface
        theme.Foreground = (Color)ColorConverter.ConvertFromString("#161D1B")!; // on-surface
        paletteHelper.SetTheme(theme);
    }

    private static void LogAndShowException(Exception ex, string title)
    {
        string detail = GetExceptionDetails(ex);
        try
        {
            System.IO.File.WriteAllText("pdf_crash_log.txt", detail);
        }
        catch { }

        MessageBox.Show(detail, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string GetExceptionDetails(Exception? ex)
    {
        if (ex == null) return "No exception details available.";

        System.Text.StringBuilder sb = new();
        sb.AppendLine("=== PDF READER GLOBAL CRASH LOG ===");
        sb.AppendLine($"Time: {DateTime.Now}");
        sb.AppendLine();

        int depth = 0;
        Exception? current = ex;
        while (current != null)
        {
            sb.AppendLine($"[Level {depth}] {current.GetType().FullName}");
            sb.AppendLine($"Message: {current.Message}");
            sb.AppendLine("Stack Trace:");
            sb.AppendLine(current.StackTrace);
            sb.AppendLine(new string('-', 40));

            current = current.InnerException;
            depth++;
        }

        return sb.ToString();
    }
}