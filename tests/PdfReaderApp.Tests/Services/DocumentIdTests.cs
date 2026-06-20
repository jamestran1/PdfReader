using System.IO;
using System.Text;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class DocumentIdTests
{
    [Fact]
    public void FromBytes_SameBytes_SameId()
    {
        var a = DocumentId.FromBytes(Encoding.UTF8.GetBytes("hello"));
        var b = DocumentId.FromBytes(Encoding.UTF8.GetBytes("hello"));
        Assert.Equal(a, b);
    }

    [Fact]
    public void FromBytes_DifferentBytes_DifferentId()
    {
        var a = DocumentId.FromBytes(Encoding.UTF8.GetBytes("hello"));
        var b = DocumentId.FromBytes(Encoding.UTF8.GetBytes("world"));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void FromBytes_IsHex64_Sha256()
    {
        var a = DocumentId.FromBytes(Encoding.UTF8.GetBytes("hello"));
        Assert.Equal(64, a.Length);
        Assert.All(a, c => Assert.Contains(c, "0123456789abcdef"));
    }

    [Fact]
    public void FromFile_MatchesFromBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("pdf content here");
        string tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllBytes(tmp, bytes);
        try
        {
            Assert.Equal(DocumentId.FromBytes(bytes), DocumentId.FromFile(tmp));
        }
        finally { File.Delete(tmp); }
    }
}
