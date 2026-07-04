using System;
using System.IO;
using Xunit;

namespace PdfReaderApp.Tests;

public class WorkspaceDocumentsViewStyleTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PdfReaderApp.slnx")))
            directory = directory.Parent;
        if (directory == null)
            throw new InvalidOperationException("Không tìm thấy gốc repo (PdfReaderApp.slnx).");
        return directory.FullName;
    }

    private static string ViewXaml()
        => File.ReadAllText(Path.Combine(RepoRoot(), "src", "PdfReaderApp", "Controls", "WorkspaceDocumentsView.xaml"));

    private static string MainWindowXaml()
        => File.ReadAllText(Path.Combine(RepoRoot(), "src", "PdfReaderApp", "MainWindow.xaml"));

    [Fact]
    public void View_HasTwoZones_AndSurfaceCommands()
    {
        var xaml = ViewXaml();
        Assert.Contains("Trong Workspace", xaml);
        Assert.Contains("Thêm từ thư viện", xaml);
        Assert.Contains("OpenMemberCommand", xaml);
        Assert.Contains("AddFromLibraryCommand", xaml);
        Assert.Contains("RemoveMemberCommand", xaml);
        Assert.Contains("BeginRenameCommand", xaml);
    }

    [Fact]
    public void MainWindow_HostsSurface_InDialogHost_AndInline()
    {
        var xaml = MainWindowXaml();
        Assert.Contains("materialDesign:DialogHost", xaml);
        Assert.Contains("{Binding DocumentsSurface.IsModalOpen}", xaml);
        Assert.Contains("controls:WorkspaceDocumentsView", xaml);
    }

    [Fact]
    public void MainWindow_OldDetailScreen_IsGone()
    {
        var xaml = MainWindowXaml();
        Assert.DoesNotContain("RenameWorkspaceBox", xaml);
        Assert.DoesNotContain("LibraryPickerBox", xaml);
        Assert.DoesNotContain("ShowWorkspaceDetail", xaml);
        Assert.DoesNotContain("ShowWorkspacesGrid", xaml);
    }
}
