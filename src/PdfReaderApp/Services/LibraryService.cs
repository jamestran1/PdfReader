using System;
using System.Collections.Generic;
using System.IO;
using PdfiumViewer.Core;
using PdfReaderApp.Core;
using PdfReaderApp.Models;
using SkiaSharp;

namespace PdfReaderApp.Services;

/// <summary>
/// Quan ly thu vien: import (copy file vao thu muc app + render thumbnail bia + ghi store),
/// liet ke, xoa, cap nhat lan mo cuoi. Dedup theo DocumentId (hash noi dung).
/// </summary>
public sealed class LibraryService
{
    private const float ThumbTargetWidthPx = 220f;

    private readonly ILibraryStore _store;
    private readonly string _libraryDir;
    private readonly string _thumbDir;
    private readonly RenderEngine _renderEngine;

    public LibraryService(ILibraryStore store, string libraryDir, string thumbDir, RenderEngine renderEngine)
    {
        _store = store;
        _libraryDir = libraryDir;
        _thumbDir = thumbDir;
        _renderEngine = renderEngine;
    }

    public IReadOnlyList<LibraryItem> GetAll() => _store.GetAll();

    public void MarkOpened(string documentId, long nowUnix) => _store.TouchLastOpened(documentId, nowUnix);

    public LibraryItem Import(string sourcePath, long nowUnix)
    {
        string id = DocumentId.FromFile(sourcePath);

        var existing = _store.Get(id);
        if (existing != null)
        {
            _store.TouchLastOpened(id, nowUnix);
            return existing with { LastOpenedAtUnix = nowUnix };
        }

        Directory.CreateDirectory(_libraryDir);
        Directory.CreateDirectory(_thumbDir);

        string storedPath = Path.Combine(_libraryDir, id + ".pdf");
        if (!File.Exists(storedPath))
            File.Copy(sourcePath, storedPath, overwrite: true);

        int pageCount;
        string? thumbPath = Path.Combine(_thumbDir, id + ".png");
        using (var ms = new MemoryStream(File.ReadAllBytes(storedPath)))
        using (var doc = PdfDocument.Load(ms))
        {
            pageCount = doc.PageCount;
            try { RenderThumbnail(doc.Pages[0], thumbPath); }
            catch { thumbPath = null; } // thumbnail la phu; thieu van import duoc
        }

        var item = new LibraryItem(id, Path.GetFileName(sourcePath), storedPath, thumbPath,
            pageCount, nowUnix, nowUnix);
        _store.Upsert(item);
        return item;
    }

    public void Remove(LibraryItem item)
    {
        _store.Remove(item.DocumentId);
        TryDelete(item.StoredPath);
        if (item.ThumbPath != null) TryDelete(item.ThumbPath);
    }

    private void RenderThumbnail(PdfPage page, string thumbPath)
    {
        float scale = ThumbTargetWidthPx / (float)page.Width;
        using var bmp = _renderEngine.RenderPage(page, scale);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 85);
        using var fs = File.Create(thumbPath);
        data.SaveTo(fs);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
