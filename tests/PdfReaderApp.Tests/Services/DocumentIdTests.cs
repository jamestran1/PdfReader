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
}
