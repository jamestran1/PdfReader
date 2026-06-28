using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

    // Phím tắt không gắn command: Ctrl+F focus ô tìm kiếm, Ctrl+0 fit trang vừa khung.
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
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
        else if (ctrl && e.Key == Key.G)
        {
            PageBox.Focus();
            PageBox.SelectAll();
            e.Handled = true;
        }
    }

    // Enter trong ô số trang: parse + kẹp về [1, TotalPages] rồi nhảy tới trang, sau đó nhả focus.
    private void PageBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (DataContext is ViewModels.MainViewModel vm && int.TryParse(PageBox.Text, out var page))
            vm.CurrentPage = Math.Clamp(page, 1, Math.Max(1, vm.TotalPages));
        PdfViewer.Focus();
        e.Handled = true;
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

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public sealed class CountToInverseVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is int n && n > 0 ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

// Unix seconds -> chuỗi ngày địa phương (cho nhãn "lần mở cuối" trên thẻ thư viện).
public sealed class UnixToDateConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is long secs ? DateTimeOffset.FromUnixTimeSeconds(secs).LocalDateTime.ToString("dd/MM/yyyy") : "";
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

// Đường dẫn file -> BitmapImage nạp với OnLoad + IgnoreImageCache: đọc hết vào RAM nên KHÔNG
// khoá file (xoá/ghi lại được) và KHÔNG dùng ảnh cache cũ theo URI.
public sealed class PathToImageConverter : IValueConverter
{
    public object? Convert(object value, Type t, object p, CultureInfo c)
    {
        if (value is not string path || string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

// PageIndex (0-based, int?) -> chuỗi "Trang N" (N = index + 1). Null -> rỗng.
public sealed class PageBadgeConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is int i ? $"Trang {i + 1}" : string.Empty;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

// Giá trị null -> Collapsed, có giá trị -> Visible (ẩn badge khi note không neo trang).
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

// S3: chuyển DocumentId -> SolidColorBrush cho chip nhãn tài liệu
public sealed class DocumentIdToBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        string hex = PdfReaderApp.ViewModels.DocumentChip.ColorHexFor(value as string);
        if (System.Windows.Media.ColorConverter.ConvertFromString(hex) is System.Windows.Media.Color col)
            return new System.Windows.Media.SolidColorBrush(col);
        return System.Windows.Media.Brushes.Gray;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

// S3: IMultiValueConverter nhận [DocumentId, DocumentTitles] -> nhãn rút gọn cho chip
public sealed class DocumentIdToTitleConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c)
    {
        if (values.Length < 2) return string.Empty;
        string? docId = values[0] as string;
        if (string.IsNullOrEmpty(docId)) return string.Empty;
        if (values[1] is IReadOnlyDictionary<string, string> titles && titles.TryGetValue(docId, out var title))
            return PdfReaderApp.ViewModels.DocumentChip.ShortLabel(title);
        // Không có tiêu đề cho documentId này -> nhãn rỗng (không lộ id nội bộ ra UI)
        return string.Empty;
    }
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c) => Array.Empty<object>();
}

// Checks a PdfViewMode against the mode name passed as ConverterParameter, for radio-style toggles.
public sealed class ViewModeToBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c)
        => value is PdfReaderApp.Core.PdfViewMode m && parameter is string p
           && string.Equals(m.ToString(), p, StringComparison.Ordinal);

    public object? ConvertBack(object value, Type t, object parameter, CultureInfo c)
        => value is true && parameter is string p
           && Enum.TryParse<PdfReaderApp.Core.PdfViewMode>(p, out var m) ? m : Binding.DoNothing;
}

public sealed class NavDestinationToBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c)
        => value is PdfReaderApp.Core.NavDestination d && parameter is string p
           && string.Equals(d.ToString(), p, StringComparison.Ordinal);

    // OneWay: nav rail highlight chỉ đọc đích; Command mới là thứ đổi đích.
    public object ConvertBack(object value, Type t, object parameter, CultureInfo c)
        => Binding.DoNothing;
}

// Tab S1: MultiValueConverter nhận [thisTab, ActiveTab] -> Visible nếu bằng nhau (ReferenceEqual), Collapsed nếu không.
// Dùng trong ItemTemplate viewer-per-tab để chỉ tab active render, còn lại Collapsed.
public sealed class IsActiveTabConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c)
    {
        if (values.Length < 2) return Visibility.Collapsed;
        return ReferenceEquals(values[0], values[1]) ? Visibility.Visible : Visibility.Collapsed;
    }
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c) => Array.Empty<object>();
}
