using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace PdfReaderApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        try
        {
            InitializeComponent();
            Closing += (_, _) => (DataContext as ViewModels.MainViewModel)?.SaveOpenSetNow();
        }
#pragma warning disable CA1031 // ranh giới khởi tạo cửa sổ: ghi log và hiện lỗi cho người dùng rồi tắt app; ghi log là best-effort
        catch (Exception ex)
        {
            string detail = GetExceptionDetails(ex);
            try
            {
                System.IO.File.WriteAllText("pdf_crash_log.txt", detail);
            }
            catch (Exception inner) { System.Diagnostics.Trace.TraceWarning($"MainWindow: không ghi được crash log: {inner}"); }

            MessageBox.Show(detail, "Lỗi Khởi tạo Hệ thống", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
#pragma warning restore CA1031
    }

    // Phím tắt không gắn command: Ctrl+F focus ô tìm kiếm, Ctrl+0 fit trang vừa khung.
    private void PdfViewer_LoadFailed(string message)
        => (DataContext as ViewModels.MainViewModel)?.NotifyError(message);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.None;
        if (ctrl && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (ctrl && (e.Key == Key.D0 || e.Key == Key.NumPad0))
        {
            PdfViewer.FitToViewport();
            e.Handled = true;
        }
    }

    private void OpenPromotePopup(object sender, System.Windows.RoutedEventArgs e)
    {
        PromoteNameBox.Clear();
        PromotePopup.IsOpen = true;
        PromoteNameBox.Focus();
    }

    // Đóng popup sau khi bấm "Tạo"; lệnh promote chạy qua Command nên chỉ cần đóng UI ở đây.
    private void ClosePromotePopup(object sender, System.Windows.RoutedEventArgs e)
        => PromotePopup.IsOpen = false;

    private void OnMinimizeWindow(object sender, System.Windows.RoutedEventArgs e)
        => System.Windows.SystemCommands.MinimizeWindow(this);

    private void OnMaximizeRestoreWindow(object sender, System.Windows.RoutedEventArgs e)
    {
        if (WindowState == System.Windows.WindowState.Maximized)
            System.Windows.SystemCommands.RestoreWindow(this);
        else
            System.Windows.SystemCommands.MaximizeWindow(this);
    }

    private void OnCloseWindow(object sender, System.Windows.RoutedEventArgs e)
        => System.Windows.SystemCommands.CloseWindow(this);

    private static string GetExceptionDetails(Exception? ex)
    {
        if (ex == null) return "No exception details available.";

        System.Text.StringBuilder sb = new();
        sb.AppendLine("=== PDF READER CRASH LOG ===");
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
