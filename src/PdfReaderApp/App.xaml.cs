using System.Windows;

namespace PdfReaderApp;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        MessageBox.Show("App constructor called!");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        MessageBox.Show("App OnStartup called!");
        base.OnStartup(e);

        this.DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show($"Unhandled Exception: {args.Exception.Message}\n\nStack Trace: {args.Exception.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = (Exception)args.ExceptionObject;
            MessageBox.Show($"Domain Unhandled Exception: {ex.Message}\n\nStack Trace: {ex.StackTrace}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };
    }
}