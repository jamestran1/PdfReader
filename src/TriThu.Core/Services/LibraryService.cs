using System;
using System.Collections.Generic;
using System.IO;
using PdfReaderApp.Models;
using SkiaSharp;

namespace PdfReaderApp.Services;

public sealed class LibraryService
{
    private const float ThumbTargetWidthPx = 220f;

    private readonly ILibraryStore _store;
    private readonly string _libraryDir;
    private readonly string _thumbDir;
    private readonly IPdfRenderService _pdfRender;

    public LibraryService(ILibraryStore store, string libraryDir, string thumbDir, IPdfRenderService pdfRender)
    {
        _store = store;
        _libraryDir = libraryDir;
        _thumbDir = thumbDir;
        _pdfRender = pdfRender;
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

        byte[] pdfBytes = File.ReadAllBytes(storedPath);
        using var tempRender = new DocnetPdfRenderService();
        tempRender.LoadDocument(pdfBytes);
        pageCount = tempRender.PageCount;

        try
        {
            var (pageW, pageH) = tempRender.GetPageSize(0);
            float scale = ThumbTargetWidthPx / (float)pageW;
            using var bmp = tempRender.RenderPage(0, scale);
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 85);
            using var fs = File.Create(thumbPath);
            data.SaveTo(fs);
        }
        catch { thumbPath = null; }

        var metadata = PdfMetadataReader.Read(storedPath);
        string title = metadata.Title ?? Path.GetFileName(sourcePath);

        var item = new LibraryItem(id, title, storedPath, thumbPath,
            pageCount, nowUnix, nowUnix, metadata.Author, metadata.Publisher);
        _store.Upsert(item);
        return item;
    }

    public void Remove(LibraryItem item)
    {
        _store.Remove(item.DocumentId);
        TryDelete(item.StoredPath);
        if (item.ThumbPath != null) TryDelete(item.ThumbPath);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
