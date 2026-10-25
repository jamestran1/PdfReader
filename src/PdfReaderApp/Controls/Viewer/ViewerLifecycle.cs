namespace PdfReaderApp.Controls.Viewer;

/// <summary>
/// Máy trạng thái thuần cho vòng đời Loaded/Unloaded/Dispose của PdfViewerControl (tách để test, CS-1).
/// </summary>
internal sealed class ViewerLifecycle
{
    public bool IsAttached { get; private set; } = true;

    public bool IsDisposed { get; private set; }

    /// <summary>Gọi khi Unloaded; trả về true nếu control phải giải phóng tài nguyên.</summary>
    public bool OnUnloaded()
    {
        if (IsDisposed) return false;
        IsAttached = false;
        IsDisposed = true;
        return true;
    }

    /// <summary>Gọi khi Loaded; trả về true nếu control phải dựng lại tài nguyên.</summary>
    public bool OnLoaded() => false;

    /// <summary>Gọi khi Dispose thật; trả về true ở lần đầu.</summary>
    public bool OnDispose()
    {
        if (IsDisposed) return false;
        IsAttached = false;
        IsDisposed = true;
        return true;
    }
}
