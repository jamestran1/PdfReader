using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PdfReaderApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            string detail = GetExceptionDetails(ex);
            try
            {
                System.IO.File.WriteAllText("pdf_crash_log.txt", detail);
            }
            catch { }
            
            MessageBox.Show(detail, "Lỗi Khởi tạo Hệ thống", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
    }

    private static string GetExceptionDetails(Exception? ex)
    {
        if (ex == null) return "No exception details available.";
        
        System.Text.StringBuilder sb = new();
        sb.AppendLine("=== PDF READER CRASH LOG ===");
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

public class RoleToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string role = value as string ?? "User";
        if (role == "AI")
            return Application.Current.Resources["MaterialDesignSecondaryContainerLow"] ?? Brushes.LightBlue;
        return Application.Current.Resources["MaterialDesignPrimaryContainerLow"] ?? Brushes.LightGray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class RoleToAlignConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string role = value as string ?? "User";
        return role == "AI" ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public sealed class CountToBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is int n && n > 0;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class PageDisplayConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is int n ? n + 1 : value;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}