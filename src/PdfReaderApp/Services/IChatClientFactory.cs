using Microsoft.Extensions.AI;

namespace PdfReaderApp.Services;

public interface IChatClientFactory
{
    IChatClient Create(string apiKey);
}
