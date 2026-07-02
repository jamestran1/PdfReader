namespace TriThu.Maui.Pages;

public partial class SettingsPage : ContentPage
{
    public string? ApiKey { get; private set; }

    public SettingsPage()
    {
        InitializeComponent();
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        ApiKey = ApiKeyEntry.Text;
        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        ApiKey = null;
        await Navigation.PopModalAsync();
    }
}
