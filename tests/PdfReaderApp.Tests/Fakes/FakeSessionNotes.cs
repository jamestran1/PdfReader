using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.Fakes;

internal sealed class FakeSessionNotes(CallLog log) : IReadingSessionNotes
{
    public NotesPanelState PanelState { get; set; } = new(0, null, false);

    /// <summary>Workspace của lần <see cref="LoadFor"/> gần nhất.</summary>
    public string? LoadedFor { get; private set; }

    public void LoadFor(string? workspaceId)
    {
        LoadedFor = workspaceId;
        log.Add($"notes.load:{workspaceId ?? "null"}");
    }

    public void RefreshDocumentContext() => log.Add("notes.context");
}
