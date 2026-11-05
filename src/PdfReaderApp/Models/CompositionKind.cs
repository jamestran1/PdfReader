namespace PdfReaderApp.Models;

/// <summary>Kiểu Bản soạn (GLOSSARY): Tóm tắt, Outline, Soạn thảo. Tên enum được lưu vào compositions.db, không đổi tên.</summary>
public enum CompositionKind
{
    Summary,
    Outline,
    Draft,
}
