using iText.Kernel.Pdf;
using iText.Kernel.XMP;
using iText.Kernel.XMP.Properties;

namespace PdfReaderApp.Services;

public static class PdfMetadataReader
{
    public readonly record struct Metadata(string? Title, string? Author, string? Publisher);

    public static Metadata Read(string path)
    {
        try
        {
            using var reader = new PdfReader(path);
            using var document = new PdfDocument(reader);
            PdfDocumentInfo info = document.GetDocumentInfo();
            return new Metadata(
                Title: Normalize(info.GetTitle()),
                Author: Normalize(info.GetAuthor()),
                Publisher: ReadDublinCorePublisher(document));
        }
        catch (Exception)
        {
            // Metadata là phụ: file lỗi/iText từ chối KHÔNG được làm hỏng import (giống thumbnail best-effort).
            return new Metadata(null, null, null);
        }
    }

    // Publisher không có trong Info dictionary chuẩn; nguồn thật duy nhất là XMP dc:publisher (mảng).
    // Producer/Creator là tên phần mềm, KHÔNG phải nhà xuất bản, nên không dùng.
    private static string? ReadDublinCorePublisher(PdfDocument document)
    {
        try
        {
            byte[]? rawXmp = document.GetXmpMetadata();
            if (rawXmp is null) return null;
            XMPMeta xmp = XMPMetaFactory.ParseFromBuffer(rawXmp);
            XMPProperty? first = xmp.GetArrayItem(XMPConst.NS_DC, "publisher", 1);
            return Normalize(first?.GetValue());
        }
        catch (XMPException)
        {
            return null;
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
