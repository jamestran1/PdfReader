namespace TriThu.Maui;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell())
        {
            Title = "Tri Thu",
            Width = 1200,
            Height = 800,
        };
        return window;
    }
}
