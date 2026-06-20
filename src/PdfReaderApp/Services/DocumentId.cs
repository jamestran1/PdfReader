using System.IO;
using System.Security.Cryptography;

namespace PdfReaderApp.Services;

public static class DocumentId
{
    public static string FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string FromFile(string path)
    {
        using var stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
