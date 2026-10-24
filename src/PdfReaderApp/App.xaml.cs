using System.Globalization;
using System.Windows;
using PdfReaderApp.Services;

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

        ApplySavedTheme();
        WindowSnapshot.ScheduleIfRequested(this, Core.SnapshotRequest.FromEnvironment(Environment.GetEnvironmentVariable));

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

    // Áp giao diện đã lưu khi khởi động. CustomColorTheme trong App.xaml chỉ seed primary/secondary +
    // dictionary token sáng; theme thật (sáng/tối) do người dùng chọn được nối dây ở đây qua IThemeService.
    private static void ApplySavedTheme()
    {
        var settingsService = new WindowsSettingsService();
        var themeService = new MaterialDesignThemeService();
        themeService.Apply(settingsService.GetThemePreference());
    }

    private static void LogAndShowException(Exception ex, string title)
    {
        string detail = GetExceptionDetails(ex);
        try
        {
            System.IO.File.WriteAllText("pdf_crash_log.txt", detail);
        }
#pragma warning disable CA1031 // ghi crash log là best-effort: lỗi ghi file không được che hộp thoại báo lỗi gốc
        catch (Exception inner) { System.Diagnostics.Trace.TraceWarning($"App.LogAndShowException: không ghi được crash log: {inner}"); }
#pragma warning restore CA1031

        MessageBox.Show(detail, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string GetExceptionDetails(Exception? ex)
    {
        if (ex == null) return "No exception details available.";

        System.Text.StringBuilder sb = new();
        sb.AppendLine("=== PDF READER GLOBAL CRASH LOG ===");
        sb.AppendLine(CultureInfo.CurrentCulture, $"Time: {DateTime.Now}");
        sb.AppendLine();

        int depth = 0;
        Exception? current = ex;
        while (current != null)
        {
            sb.AppendLine(CultureInfo.CurrentCulture, $"[Level {depth}] {current.GetType().FullName}");
            sb.AppendLine(CultureInfo.CurrentCulture, $"Message: {current.Message}");
            sb.AppendLine("Stack Trace:");
            sb.AppendLine(current.StackTrace);
            sb.AppendLine(new string('-', 40));

            current = current.InnerException;
            depth++;
        }

        return sb.ToString();
    }
}