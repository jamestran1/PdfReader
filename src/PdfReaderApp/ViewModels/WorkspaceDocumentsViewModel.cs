using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>
/// Workspace Documents surface (#47, ADR 0004): MỘT component hai vùng — thành viên + thêm-từ-thư-viện —
/// render ở 2 host (modal DialogHost và inline khi Open Set rỗng), cùng bind một instance này.
/// Tách khỏi MainViewModel (pattern NotesViewModel) để test trọn vẹn bằng delegate.
/// </summary>
public sealed partial class WorkspaceDocumentsViewModel : ObservableObject
{
    private readonly IWorkspaceStore _workspaceStore;
    private readonly Func<Workspace?> _activeWorkspace;
    private readonly Func<IEnumerable<LibraryItem>> _libraryItems;
    private readonly Action<LibraryItem> _openTab;
    private readonly Action<string> _closeTabForDocument;
    private readonly Action<string> _notify;
    private readonly Action _stateChanged;

    public ObservableCollection<LibraryItem> Members { get; } = new();
    public ObservableCollection<LibraryItem> LibraryAdditions { get; } = new();

    [ObservableProperty] private bool _isModalOpen;
    [ObservableProperty] private string _workspaceName = string.Empty;

    public WorkspaceDocumentsViewModel(
        IWorkspaceStore workspaceStore,
        Func<Workspace?> activeWorkspace,
        Func<IEnumerable<LibraryItem>> libraryItems,
        Action<LibraryItem> openTab,
        Action<string> closeTabForDocument,
        Action<string> notify,
        Action stateChanged)
    {
        _workspaceStore = workspaceStore;
        _activeWorkspace = activeWorkspace;
        _libraryItems = libraryItems;
        _openTab = openTab;
        _closeTabForDocument = closeTabForDocument;
        _notify = notify;
        _stateChanged = stateChanged;
    }

    /// <summary>Tính lại hai vùng từ membership + Library. Gọi khi mở surface hoặc dữ liệu nền đổi.</summary>
    public void Refresh()
    {
        Members.Clear();
        LibraryAdditions.Clear();
        var workspace = _activeWorkspace();
        WorkspaceName = workspace?.Name ?? string.Empty;
        if (workspace is null) return;

        var memberDocumentIds = new HashSet<string>(
            _workspaceStore.GetDocumentIds(workspace.Id), StringComparer.Ordinal);
        foreach (var item in _libraryItems())
        {
            if (memberDocumentIds.Contains(item.DocumentId)) Members.Add(item);
            else LibraryAdditions.Add(item);
        }
    }

    [RelayCommand]
    private void OpenMember(LibraryItem? item)
    {
        if (item is null) return;
        _openTab(item);
        IsModalOpen = false;
    }

    [RelayCommand]
    private void AddFromLibrary(LibraryItem? item)
    {
        var workspace = _activeWorkspace();
        if (item is null || workspace is null) return;
        _workspaceStore.AddDocument(workspace.Id, item.DocumentId);
        // Add = thêm membership VÀ mở Tab active (ADR 0004).
        _openTab(item);
        IsModalOpen = false;
        Refresh();
        _stateChanged();
        _notify($"Đã thêm “{item.Title}” vào Workspace");
    }

    [RelayCommand]
    private void RemoveMember(LibraryItem? item)
    {
        var workspace = _activeWorkspace();
        if (item is null || workspace is null) return;
        _workspaceStore.RemoveDocument(workspace.Id, item.DocumentId);
        // Bất biến Open Set ⊆ membership: gỡ membership thì đóng luôn tab đang mở (grill #47).
        _closeTabForDocument(item.DocumentId);
        Refresh();
        _stateChanged();
        _notify($"Đã gỡ “{item.Title}” khỏi Workspace");
    }

    [RelayCommand]
    private void CloseModal() => IsModalOpen = false;
}
