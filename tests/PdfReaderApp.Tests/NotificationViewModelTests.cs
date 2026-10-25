using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

public class NotificationViewModelTests
{
    [Fact]
    public void ShowError_SetsCurrentAsError()
    {
        var vm = new NotificationViewModel();

        vm.ShowError("Lỗi");

        Assert.Equal(new Notification("Lỗi", IsError: true), vm.Current);
    }

    [Fact]
    public void ShowSuccess_ReplacesPreviousNotification()
    {
        var vm = new NotificationViewModel();
        vm.ShowError("Lỗi");

        vm.ShowSuccess("Xong");

        Assert.Equal(new Notification("Xong", IsError: false), vm.Current);
    }

    [Fact]
    public void ShowError_RaisesPropertyChangedForCurrent()
    {
        var vm = new NotificationViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ShowError("Lỗi");

        Assert.Contains(nameof(NotificationViewModel.Current), raised, StringComparer.Ordinal);
    }
}
