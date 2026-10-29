using System.IO;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.Tests.Fakes;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

/// <summary>
/// Đường nạp Document của MainViewModel đi qua port <see cref="IDocumentLoader"/>: test truyền bộ nạp giả
/// qua constructor nên đường nạp thật (dọn Đoạn nguồn, nạp Notes/Chat, dọn kết quả tìm) chạy mà không cần PDF.
/// </summary>
public class MainViewModelDocumentLoaderTests
{
    private static (MainViewModel vm, FakeDocumentLoader loader, FakeWorkspaceStore wsStore) MakeVm()
    {
        var loader = new FakeDocumentLoader();
        var wsStore = new FakeWorkspaceStore();
        var vm = new MainViewModel(
            new ITextPdfDocumentService(),
            new WindowsSettingsService(),
            new AiChatClientFactory(),
            new SqliteDocumentIndex(
                Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".db"),
                Path.Combine(AppContext.BaseDirectory, "vec0.dll")),
            new AiEmbeddingGeneratorFactory(),
            workspaceStore: wsStore,
            documentLoader: loader);
        return (vm, loader, wsStore);
    }

    private static Workspace OpenWorkspaceWith(MainViewModel vm, FakeWorkspaceStore wsStore, params LibraryItem[] items)
    {
        var workspace = new Workspace("ws-1", "Dự án", false, null, 1, 1);
        wsStore.Upsert(workspace);
        foreach (var item in items)
        {
            wsStore.AddDocument(workspace.Id, item.DocumentId);
            vm.Library.Add(item);
        }
        vm.OpenWorkspaceCommand.Execute(workspace);
        return workspace;
    }

    private static LibraryItem Item(string docId, string path) => new(docId, "Tài liệu " + docId, path, null, 0, 1, 1);

    [Fact]
    public void ActivatingTab_UsesDocumentReturnedByLoader_WithoutOpeningAnyPdf()
    {
        var (vm, loader, wsStore) = MakeVm();
        var block = new TextBlock("Xin chào", 0, 0, 1, 1, 12, 0, "P");
        loader.Returns("/a.pdf", new LoadedDocument("doc-from-loader", [block], [new PageText(0, "Xin chào")]));
        var a = Item("docA", "/a.pdf");
        OpenWorkspaceWith(vm, wsStore, a);

        vm.OpenWorkspaceDocumentCommand.Execute(a);

        Assert.Equal("doc-from-loader", vm.Session.DocumentId);
        Assert.Equal([block], vm.Session.DocumentBlocks);
        Assert.Equal(["/a.pdf"], loader.LoadedPaths);
    }

    [Fact]
    public void LoaderFailure_NotifiesUserAndLeavesNoDocumentOpen()
    {
        var (vm, loader, wsStore) = MakeVm();
        loader.ThrowOnLoad = new InvalidOperationException("hỏng");
        var a = Item("docA", "/a.pdf");
        OpenWorkspaceWith(vm, wsStore, a);

        vm.OpenWorkspaceDocumentCommand.Execute(a);

        Assert.False(vm.Session.HasDocument);
        Assert.Empty(vm.Session.DocumentBlocks);
        Assert.True(vm.Notifications.Current?.IsError);
        Assert.Contains("hỏng", vm.Notifications.Current!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_ExposesMatchSourceOfLoader()
    {
        var (vm, loader, _) = MakeVm();

        Assert.Same(loader.MatchSource, vm.Session.MatchSource);
    }
}
