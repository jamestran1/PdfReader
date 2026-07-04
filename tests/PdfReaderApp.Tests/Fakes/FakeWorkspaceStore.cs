using System.Collections.Generic;
using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Fakes;

public sealed class FakeWorkspaceStore : IWorkspaceStore
{
    public readonly List<Workspace> All = new();
    public readonly Dictionary<string, HashSet<string>> Membership = new();
    public int RemoveDocumentCallCount { get; private set; }

    public void EnsureSchema() { }
    public void Upsert(Workspace w) { All.RemoveAll(x => x.Id == w.Id); All.Add(w); }
    public Workspace? Get(string id) => All.FirstOrDefault(w => w.Id == id);
    public IReadOnlyList<Workspace> GetAll(bool includeDefault)
        => All.Where(w => includeDefault || !w.IsDefault).ToList();
    public void AddDocument(string workspaceId, string documentId)
    {
        if (!Membership.TryGetValue(workspaceId, out var s)) { s = new(); Membership[workspaceId] = s; }
        s.Add(documentId);
    }
    public void RemoveDocument(string workspaceId, string documentId)
    {
        RemoveDocumentCallCount++;
        if (Membership.TryGetValue(workspaceId, out var s)) s.Remove(documentId);
    }
    public IReadOnlyList<string> GetDocumentIds(string workspaceId)
        => Membership.TryGetValue(workspaceId, out var s) ? s.ToList() : new List<string>();
    public IReadOnlyList<string> GetWorkspaceIdsForDocument(string documentId)
    {
        var result = new List<string>();
        foreach (var kv in Membership)
            if (kv.Value.Contains(documentId)) result.Add(kv.Key);
        return result;
    }
    public Workspace GetOrCreateDefaultForDocument(string documentId, string name, long nowUnixMs)
    {
        var existing = All.FirstOrDefault(w => w.IsDefault && w.DefaultDocumentId == documentId);
        if (existing != null) return existing;
        var ws = new Workspace(System.Guid.NewGuid().ToString("N"), name, true, documentId, nowUnixMs, nowUnixMs);
        All.Add(ws);
        AddDocument(ws.Id, documentId);
        return ws;
    }
    public void Rename(string id, string name, long nowUnixMs)
    {
        int i = All.FindIndex(w => w.Id == id);
        if (i >= 0) All[i] = All[i] with { Name = name, UpdatedAtUnixMs = nowUnixMs };
    }
    public void Delete(string id) { All.RemoveAll(w => w.Id == id); Membership.Remove(id); }
    public readonly Dictionary<string, List<OpenTabState>> OpenSets = new();

    public void SaveOpenTabs(string workspaceId, IReadOnlyList<OpenTabState> tabs)
        => OpenSets[workspaceId] = tabs.OrderBy(t => t.TabOrder).ToList();

    public IReadOnlyList<OpenTabState> GetOpenTabs(string workspaceId)
        => OpenSets.TryGetValue(workspaceId, out var s) ? s.ToList() : new List<OpenTabState>();
}
