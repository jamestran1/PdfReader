using System.Collections.Generic;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lưu Workspace + membership (workspace_document) trong workspaces.db (ADR 0002).</summary>
public interface IWorkspaceStore
{
    void EnsureSchema();
    void Upsert(Workspace workspace);
    Workspace? Get(string id);
    IReadOnlyList<Workspace> GetAll(bool includeDefault);
    void AddDocument(string workspaceId, string documentId);
    void RemoveDocument(string workspaceId, string documentId);
    IReadOnlyList<string> GetDocumentIds(string workspaceId);
    IReadOnlyList<string> GetWorkspaceIdsForDocument(string documentId);
    Workspace GetOrCreateDefaultForDocument(string documentId, string name, long nowUnixMs);
    void Rename(string id, string name, long nowUnixMs);
    void Delete(string id);
}
