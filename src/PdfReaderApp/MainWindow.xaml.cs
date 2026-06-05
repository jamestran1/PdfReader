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
            System.IO.File.WriteAllText("crash_log.txt", $"XAML Load Error: {ex.Message}\n\nInner: {ex.InnerException?.Message}\n\nStack Trace: {ex.StackTrace}");
            Application.Current.Shutdown();
        }
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