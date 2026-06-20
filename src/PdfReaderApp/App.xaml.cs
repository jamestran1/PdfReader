using System.Windows;

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