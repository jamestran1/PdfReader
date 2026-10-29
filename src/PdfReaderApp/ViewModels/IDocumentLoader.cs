using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>Port nạp Document từ đường dẫn. Nạp lỗi thì ném exception; người gọi báo cho người dùng.</summary>
public interface IDocumentLoader
{
    /// <summary>Nạp Document tại <paramref name="path"/>. Chạy đồng bộ; chữ ký không được giả định điều đó ở nơi gọi.</summary>
    LoadedDocument Load(string path);

    /// <summary>Text engine dùng chung mà viewer hỏi để tô từ khoá và Đoạn nguồn. Trạng thái của nó đi theo Document nạp gần nhất.</summary>
    IPdfDocumentService MatchSource { get; }
}
