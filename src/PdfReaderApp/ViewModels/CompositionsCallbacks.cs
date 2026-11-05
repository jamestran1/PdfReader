using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>
/// Việc <see cref="CompositionsViewModel"/> nhờ host làm (mở tab, đóng tab, clipboard, hộp xác nhận, báo lỗi, luồng UI).
/// Đi qua constructor (CS-11); phần có mặc định là việc test không cần nối.
/// </summary>
internal sealed record CompositionsCallbacks
{
    /// <summary>Mở Bản soạn thành Tab active; tham số thứ hai là id Bản soạn mà tab mới nằm bên phải (null thì thêm cuối).</summary>
    public required Action<Composition, string?> OpenComposition { get; init; }

    /// <summary>Báo lỗi cho người dùng.</summary>
    public required Action<string> NotifyError { get; init; }

    /// <summary>Tab Bản soạn đang mở của <c>compositionId</c>, null khi chưa mở.</summary>
    public Func<string, CompositionTab?> OpenTabFor { get; init; } = _ => null;

    /// <summary>Đóng tab (Tab dùng gần nhất được kích hoạt, theo ADR 0003).</summary>
    public Action<CompositionTab> CloseTab { get; init; } = _ => { };

    /// <summary>Đặt văn bản lên clipboard.</summary>
    public Action<string> CopyToClipboard { get; init; } = _ => { };

    /// <summary>Hỏi xác nhận xoá Bản soạn có tiêu đề này; mặc định không xoá.</summary>
    public Func<string, bool> ConfirmDelete { get; init; } = _ => false;

    /// <summary>Chạy việc trên luồng UI; null thì đẩy về <see cref="SynchronizationContext"/> lúc dựng, test chạy thẳng.</summary>
    public Action<Action>? RunOnUi { get; init; }

    /// <summary>Ngân sách ký tự theo Chat Model đang chọn (một nguồn: <see cref="CompositionBudget"/>).</summary>
    public Func<int> BudgetChars { get; init; } = () => CompositionBudget.CharsFor(null);
}
