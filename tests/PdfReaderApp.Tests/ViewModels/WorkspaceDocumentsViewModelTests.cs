using System.Collections.Generic;
using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.Tests.Fakes;
using PdfReaderApp.ViewModels;
using Xunit;

namespace PdfReaderApp.Tests.ViewModels;

public class WorkspaceDocumentsViewModelTests
{
    private readonly FakeWorkspaceStore _store = new();
    private readonly List<LibraryItem> _library = new();
    private readonly List<LibraryItem> _openedTabs = new();
    private readonly List<string> _closedTabDocumentIds = new();
    private readonly List<string> _notifications = new();
    private int _stateChangedCount;
    private Workspace _workspace;

    public WorkspaceDocumentsViewModelTests()
    {
        _workspace = new Workspace("ws1", "Nghiên cứu", false, null, 1, 1);
        _store.Upsert(_workspace);
    }

    private WorkspaceDocumentsViewModel BuildSurface()
        => new(_store,
               activeWorkspace: () => _store.Get(_workspace.Id),
               libraryItems: () => _library,
               openTab: _openedTabs.Add,
               closeTabForDocument: _closedTabDocumentIds.Add,
               notify: _notifications.Add,
               stateChanged: () => _stateChangedCount++);

    private LibraryItem AddLibraryItem(string documentId, string title)
    {
        var item = new LibraryItem(documentId, title, $"/lib/{documentId}.pdf", null, 10, 1, 1);
        _library.Add(item);
        return item;
    }

    [Fact]
    public void Refresh_PartitionsLibraryIntoMembersAndAdditions()
    {
        var member = AddLibraryItem("docA", "Tài liệu A");
        var addition = AddLibraryItem("docB", "Tài liệu B");
        _store.AddDocument("ws1", "docA");
        var surface = BuildSurface();

        surface.Refresh();

        Assert.Equal(new[] { member }, surface.Members);
        Assert.Equal(new[] { addition }, surface.LibraryAdditions);
        Assert.Equal("Nghiên cứu", surface.WorkspaceName);
    }

    [Fact]
    public void AddFromLibrary_AddsMembership_OpensTab_ClosesModal_Notifies()
    {
        var item = AddLibraryItem("docB", "Tài liệu B");
        var surface = BuildSurface();
        surface.Refresh();
        surface.IsModalOpen = true;

        surface.AddFromLibraryCommand.Execute(item);

        Assert.Contains("docB", _store.GetDocumentIds("ws1"));
        Assert.Equal(new[] { item }, _openedTabs);
        Assert.False(surface.IsModalOpen);
        Assert.Contains(surface.Members, m => m.DocumentId == "docB");
        Assert.Empty(surface.LibraryAdditions);
        Assert.Equal(1, _stateChangedCount);
        Assert.Contains(_notifications, n => n.Contains("Tài liệu B"));
    }

    [Fact]
    public void RemoveMember_RemovesMembership_AndClosesItsTab()
    {
        var item = AddLibraryItem("docA", "Tài liệu A");
        _store.AddDocument("ws1", "docA");
        var surface = BuildSurface();
        surface.Refresh();

        surface.RemoveMemberCommand.Execute(item);

        Assert.DoesNotContain("docA", _store.GetDocumentIds("ws1"));
        // Bất biến Open Set ⊆ membership: gỡ membership thì tab (nếu mở) phải đóng theo.
        Assert.Equal(new[] { "docA" }, _closedTabDocumentIds);
        Assert.Empty(surface.Members);
        Assert.Contains(surface.LibraryAdditions, l => l.DocumentId == "docA");
        Assert.Contains(_notifications, n => n.Contains("Đã gỡ"));
    }

    [Fact]
    public void OpenMember_OpensTab_AndClosesModal()
    {
        var item = AddLibraryItem("docA", "Tài liệu A");
        _store.AddDocument("ws1", "docA");
        var surface = BuildSurface();
        surface.Refresh();
        surface.IsModalOpen = true;

        surface.OpenMemberCommand.Execute(item);

        Assert.Equal(new[] { item }, _openedTabs);
        Assert.False(surface.IsModalOpen);
    }

    [Fact]
    public void Commands_WithNullItem_DoNothing()
    {
        var surface = BuildSurface();
        surface.Refresh();

        surface.AddFromLibraryCommand.Execute(null);
        surface.RemoveMemberCommand.Execute(null);
        surface.OpenMemberCommand.Execute(null);

        Assert.Empty(_openedTabs);
        Assert.Empty(_closedTabDocumentIds);
        Assert.Equal(0, _stateChangedCount);
    }
}
