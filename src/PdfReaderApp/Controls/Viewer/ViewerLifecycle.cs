namespace PdfReaderApp.Controls.Viewer;

/// <summary>
/// Máy trạng thái thuần cho vòng đời Loaded/Unloaded/Dispose của PdfViewerControl (tách để test, CS-1).
/// </summary>
internal sealed class ViewerLifecycle
{
    public bool IsAttached { get; private set; } = true;

    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Gọi khi Unloaded; trả về true nếu control phải giải phóng tài nguyên native.
    /// Không phải trạng thái cuối: WPF có thể gắn control lại cây giao diện (Loaded lần nữa).
    /// </summary>
    public bool OnUnloaded()
    {
        if (IsDisposed || !IsAttached) return false;
        IsAttached = false;
        return true;
    }

    /// <summary>Gọi khi Loaded; trả về true nếu control vừa được gắn lại và phải dựng lại tài nguyên.</summary>
    public bool OnLoaded()
    {
        if (IsDisposed || IsAttached) return false;
        IsAttached = true;
        return true;
    }

    /// <summary>Gọi khi Dispose thật từ chủ sở hữu; trả về true ở lần đầu (trạng thái cuối).</summary>
    public bool OnDispose()
    {
        if (IsDisposed) return false;
        IsAttached = false;
        IsDisposed = true;
        return true;
    }
}
