using Microsoft.Extensions.AI;

namespace PdfReaderApp.Services;

/// <summary>
/// Giữ client SDK gốc cùng vòng đời với bộ sinh embedding bọc nó: adapter không đảm bảo
/// giải phóng client được truyền vào, nên dispose cả hai để không rò HttpClient bên dưới.
/// </summary>
public sealed class OwningEmbeddingGenerator(IEmbeddingGenerator<string, Embedding<float>> inner, IDisposable owned)
    : DelegatingEmbeddingGenerator<string, Embedding<float>>(inner)
{
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            owned.Dispose();

        base.Dispose(disposing);
    }
}
