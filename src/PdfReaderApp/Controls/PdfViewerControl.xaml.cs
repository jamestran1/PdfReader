using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkiaSharp.Views.Desktop;
using PdfiumViewer.Core;
using PdfReaderApp.Core;

namespace PdfReaderApp.Controls;

public partial class PdfViewerControl : UserControl, IDisposable, Viewer.IViewerHost
{
#pragma warning disable MA0046 // Sự kiện gắn trực tiếp từ XAML với chữ ký Action<string>, đổi sang EventHandler phải sửa cả XAML và code-behind
    public event System.Action<string>? LoadFailed;
#pragma warning restore MA0046

    private Viewer.PdfDocumentSession? _session;
    private readonly RenderEngine _renderEngine = new();
    private readonly Viewer.PageBitmapCache _pageCache = new();
    private readonly Viewer.ViewerFrameComposer _frameComposer;
    private readonly Viewer.ViewerLifecycle _lifecycle = new();

    private PdfDocument? _currentDocument => _session?.Document;
    private readonly List<Core.PageSlot> _slots = [];

    public static readonly DependencyProperty DocumentSourceProperty =
        DependencyProperty.Register("DocumentSource", typeof(string), typeof(PdfViewerControl),
            new PropertyMetadata(null, OnDocumentSourceChanged));

    public string DocumentSource
    {
        get => (string)GetValue(DocumentSourceProperty);
        set => SetValue(DocumentSourceProperty, value);
    }

    public static readonly DependencyProperty CurrentPageProperty =
        DependencyProperty.Register("CurrentPage", typeof(int), typeof(PdfViewerControl),
            new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCurrentPageChanged));

    public int CurrentPage
    {
        get => (int)GetValue(CurrentPageProperty);
        set => SetValue(CurrentPageProperty, value);
    }

    public static readonly DependencyProperty TotalPagesProperty =
        DependencyProperty.Register("TotalPages", typeof(int), typeof(PdfViewerControl),
            new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public int TotalPages
    {
        get => (int)GetValue(TotalPagesProperty);
        set => SetValue(TotalPagesProperty, value);
    }

    public static readonly DependencyProperty ZoomLevelProperty =
        DependencyProperty.Register("ZoomLevel", typeof(double), typeof(PdfViewerControl),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnZoomLevelChanged));

    public double ZoomLevel
    {
        get => (double)GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    // HighlightQuery: text to highlight on the visible page (bound to SelectedSearchQuery)
    public static readonly DependencyProperty HighlightQueryProperty =
        DependencyProperty.Register(nameof(HighlightQuery), typeof(string), typeof(PdfViewerControl),
            new PropertyMetadata(string.Empty, OnHighlightChanged));

    public string HighlightQuery
    {
        get => (string)GetValue(HighlightQueryProperty);
        set => SetValue(HighlightQueryProperty, value);
    }

    private static void OnHighlightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PdfViewerControl)d).skiaCanvas.InvalidateVisual();

    // MatchSource: text engine that locates exact keyword rectangles (bound to MainViewModel.PdfService).
    public static readonly DependencyProperty MatchSourceProperty =
        DependencyProperty.Register(nameof(MatchSource), typeof(Services.IPdfDocumentService), typeof(PdfViewerControl),
            new PropertyMetadata(null, OnHighlightChanged));

    public Services.IPdfDocumentService? MatchSource
    {
        get => (Services.IPdfDocumentService?)GetValue(MatchSourceProperty);
        set => SetValue(MatchSourceProperty, value);
    }

    // DependencyProperty: command nhận NoteSelection khi người dùng bấm "Thêm ghi chú"
    public static readonly DependencyProperty AddNoteFromSelectionCommandProperty =
        DependencyProperty.Register(nameof(AddNoteFromSelectionCommand), typeof(System.Windows.Input.ICommand),
            typeof(PdfViewerControl), new PropertyMetadata(null));

    public System.Windows.Input.ICommand? AddNoteFromSelectionCommand
    {
        get => (System.Windows.Input.ICommand?)GetValue(AddNoteFromSelectionCommandProperty);
        set => SetValue(AddNoteFromSelectionCommandProperty, value);
    }

    // S3: DependencyProperty lọc highlight theo tài liệu đang mở (bound từ MainViewModel.CurrentDocumentId)
    public static readonly DependencyProperty CurrentDocumentIdProperty =
        DependencyProperty.Register(nameof(CurrentDocumentId), typeof(string),
            typeof(PdfViewerControl), new PropertyMetadata(null, OnCurrentDocumentIdChanged));

    public string? CurrentDocumentId
    {
        get => (string?)GetValue(CurrentDocumentIdProperty);
        set => SetValue(CurrentDocumentIdProperty, value);
    }

    private static void OnCurrentDocumentIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PdfViewerControl)d).RepaintOverlay();

    // DependencyProperty: danh sach highlight da luu (bound tu NotesViewModel.Highlights)
    public static readonly DependencyProperty HighlightsProperty =
        DependencyProperty.Register(nameof(Highlights), typeof(System.Collections.IEnumerable),
            typeof(PdfViewerControl), new PropertyMetadata(null, OnHighlightsChanged));

    public System.Collections.IEnumerable? Highlights
    {
        get => (System.Collections.IEnumerable?)GetValue(HighlightsProperty);
        set => SetValue(HighlightsProperty, value);
    }

    private static void OnHighlightsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (PdfViewerControl)d;
        if (e.OldValue is System.Collections.Specialized.INotifyCollectionChanged oldCol)
            oldCol.CollectionChanged -= c.OnHighlightsCollectionChanged;
        if (e.NewValue is System.Collections.Specialized.INotifyCollectionChanged newCol)
            newCol.CollectionChanged += c.OnHighlightsCollectionChanged;
        c.RepaintOverlay();
    }

    private void OnHighlightsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => RepaintOverlay();

    private void RepaintOverlay() => skiaCanvas.InvalidateVisual();

    // Chuột, phím, chọn text và sửa text tại chỗ
    private readonly Viewer.ViewerPointerInput _pointerInput;

    public static readonly DependencyProperty ViewModeProperty =
        DependencyProperty.Register(nameof(ViewMode), typeof(Core.PdfViewMode), typeof(PdfViewerControl),
            new PropertyMetadata(Core.PdfViewMode.Continuous, OnViewOptionChanged));

    public Core.PdfViewMode ViewMode
    {
        get => (Core.PdfViewMode)GetValue(ViewModeProperty);
        set => SetValue(ViewModeProperty, value);
    }

    public static readonly DependencyProperty ShowCoverProperty =
        DependencyProperty.Register(nameof(ShowCover), typeof(bool), typeof(PdfViewerControl),
            new PropertyMetadata(true, OnViewOptionChanged));

    public bool ShowCover
    {
        get => (bool)GetValue(ShowCoverProperty);
        set => SetValue(ShowCoverProperty, value);
    }

    private static void OnViewOptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfViewerControl c && c._currentDocument != null)
        {
            c.RefreshLayout(keepCache: false);
            // Single-unit modes show one unit at Y=0; reset scroll so it is in view after switching.
            // Chế độ cuộn liên tục: giữ trang hiện tại (RefreshLayout dựng lại slot nên offset cũ map sai,
            // thường về cover) -> cuộn lại tới CurrentPage.
            if (c.ViewMode is Core.PdfViewMode.SinglePage or Core.PdfViewMode.Facing)
                c.PagesScrollViewer.ScrollToVerticalOffset(0);
            else
                c.ScheduleScrollToCurrentPage();
        }
    }

    public PdfViewerControl()
    {
        _frameComposer = new Viewer.ViewerFrameComposer(_pageCache, _renderEngine);
        _pointerInput = new Viewer.ViewerPointerInput(this);
        InitializeComponent();
        this.Loaded += PdfViewerControl_Loaded;
        this.Unloaded += PdfViewerControl_Unloaded;
        AttachInput();
    }

    private void AttachInput()
    {
        _pointerInput.Attach(InteractionCanvas, this, TextEditor);

        this.SizeChanged += PdfViewerControl_SizeChanged;
        this.PreviewMouseWheel += PdfViewerControl_PreviewMouseWheel;

        PdfReaderApp.Services.MaterialDesignThemeService.ThemeChanged += OnThemeChanged;
    }

    private void DetachInput()
    {
        _pointerInput.Detach();
        this.SizeChanged -= PdfViewerControl_SizeChanged;
        this.PreviewMouseWheel -= PdfViewerControl_PreviewMouseWheel;

        PdfReaderApp.Services.MaterialDesignThemeService.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
        => Dispatcher.Invoke(() => skiaCanvas?.InvalidateVisual());

    private void PdfViewerControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_currentDocument != null)
        {
            RefreshLayout(keepCache: true);
        }
    }

    private void PdfViewerControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            ZoomAtCursor(e);
        }
        else if (ViewMode is Core.PdfViewMode.SinglePage or Core.PdfViewMode.Facing && _currentDocument != null)
        {
            NavigatePageByWheel(e);
        }
    }

    private void ZoomAtCursor(MouseWheelEventArgs e)
    {
        if (_currentDocument == null) return;

        Point mousePos = e.GetPosition(PagesScrollViewer);
        var anchor = new Core.WheelZoomAnchor(
            PagesScrollViewer.HorizontalOffset, PagesScrollViewer.VerticalOffset, mousePos.X, mousePos.Y);
        double oldZoom = ZoomLevel;

        if (Core.ViewerWheelMath.NextZoom(oldZoom, e.Delta) is not double newZoom) return;

        // This updates DependencyProperty and triggers RefreshLayout via OnZoomLevelChanged
        ZoomLevel = newZoom;

        // Force layout update so ScrollViewer knows about new InteractionCanvas size
        this.UpdateLayout();

        var (newHOffset, newVOffset) = Core.ViewerWheelMath.AnchoredOffsets(anchor, oldZoom, newZoom);
        PagesScrollViewer.ScrollToHorizontalOffset(newHOffset);
        PagesScrollViewer.ScrollToVerticalOffset(newVOffset);
    }

    private void NavigatePageByWheel(MouseWheelEventArgs e)
    {
        // The current unit fills the view. If it is taller than the viewport, let the wheel scroll
        // within it first; only advance to the next/prev unit when already at the boundary.
        var decision = Core.ViewerWheelMath.ResolvePageChange(new Core.WheelPageContext(
            e.Delta,
            AtTop: PagesScrollViewer.VerticalOffset <= 0.5,
            AtBottom: PagesScrollViewer.VerticalOffset >= PagesScrollViewer.ScrollableHeight - 0.5,
            CurrentPage, TotalPages, ViewMode == Core.PdfViewMode.Facing, ShowCover));
        if (!decision.Handled) return;

        e.Handled = true;
        if (decision.NewPage is int page) CurrentPage = page;
        if (decision.ScrollToTop) PagesScrollViewer.ScrollToVerticalOffset(0);
    }

    private void AddNoteButton_Click(object sender, RoutedEventArgs e) => _pointerInput.AddNoteFromSelection();

    // IViewerHost: ViewerPointerInput chỉ YÊU CẦU; control là nơi duy nhất vẽ lại và đổi giao diện.
    Viewer.PdfDocumentSession? Viewer.IViewerHost.Session => _session;

    IReadOnlyList<Core.PageSlot> Viewer.IViewerHost.Slots => _slots;

    double Viewer.IViewerHost.ZoomLevel => ZoomLevel;

    System.Windows.Input.ICommand? Viewer.IViewerHost.AddNoteFromSelectionCommand => AddNoteFromSelectionCommand;

    bool Viewer.IViewerHost.IsEditorVisible => TextEditor.Visibility == Visibility.Visible;

    void Viewer.IViewerHost.RequestRepaint() => skiaCanvas.InvalidateVisual();

    void Viewer.IViewerHost.ShowEditor(Point topLeft, Size size, string text)
    {
        Canvas.SetLeft(TextEditor, topLeft.X);
        Canvas.SetTop(TextEditor, topLeft.Y);
        TextEditor.Visibility = Visibility.Visible;
        TextEditor.StartEditing(text, new Rect(0, 0, size.Width, size.Height));
    }

    void Viewer.IViewerHost.HideEditor()
    {
        TextEditor.Visibility = Visibility.Collapsed;
        skiaCanvas.Focus();
    }

    void Viewer.IViewerHost.ShowAddNoteButton(Point topLeft)
    {
        Canvas.SetLeft(AddNoteButton, topLeft.X);
        Canvas.SetTop(AddNoteButton, topLeft.Y);
        AddNoteButton.Visibility = Visibility.Visible;
    }

    void Viewer.IViewerHost.HideAddNoteButton()
    {
        if (AddNoteButton != null)
            AddNoteButton.Visibility = Visibility.Collapsed;
    }

    void Viewer.IViewerHost.CapturePointer() => InteractionCanvas.CaptureMouse();

    void Viewer.IViewerHost.ReleasePointer() => InteractionCanvas.ReleaseMouseCapture();

    private static void OnDocumentSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfViewerControl control && e.NewValue is string path && !string.IsNullOrEmpty(path))
        {
            // Hoãn nạp tới khi MỌI binding của control đã gắn (CurrentPage/TotalPages/Zoom).
            // Trong DataTemplate per-tab, DocumentSource có thể gắn TRƯỚC các binding kia -> LoadDocument
            // sẽ kẹp CurrentPage về 1 (mất trang đích cross-doc) và set TotalPages vào DP chưa-bound
            // (mất tổng trang). Hoãn ở mức Loaded đảm bảo đọc đúng OpenTab.Page/đẩy đúng TotalPages.
            _ = control.Dispatcher.BeginInvoke(
                new Action(() => { if ((control.DocumentSource as string) == path) control.LoadDocument(path); }),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private static void OnCurrentPageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfViewerControl control && control._currentDocument != null)
        {
            // Chế độ single-unit chỉ layout một đơn vị nên đổi trang phải re-layout.
            if (control.ViewMode is Core.PdfViewMode.SinglePage or Core.PdfViewMode.Facing)
                control.RefreshLayout(keepCache: true);
            else
                control.ScrollToPage((int)e.NewValue);
        }
    }

    private static void OnZoomLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfViewerControl control && control._currentDocument != null)
        {
            control.RefreshLayoutPreservingAnchor();
        }
    }

    private void LoadDocument(string path)
    {
        try
        {
            DisposeCurrentDocument();

            if (!File.Exists(path)) return;

            _session = Viewer.PdfDocumentSession.Open(path);
            _frameComposer.ResetMatches();
            TotalPages = _session.PageCount;
            // Honor trang đích do VM đặt trước khi nạp (vd cross-doc jump mở thẳng tại trang neo);
            // mặc định VM đặt 1 cho lần mở thường nên vẫn về trang đầu.
            CurrentPage = System.Math.Clamp(CurrentPage, 1, _session.PageCount);

            // Bật GUARD pending NGAY (trước FitToViewport/RefreshLayout): các bước layout đó phát ScrollChanged
            // đồng bộ scroll->trang (kéo CurrentPage về trang đang hiển thị) -> guard chặn ghi đè trang đích.
            ArmPendingScrollGuard();

            _session.ResetEditState();
            FitToViewport();   // zoom trang đầu vừa khung trước khi layout
            RefreshLayout();

            // Phát cuộn-đích SAU khi RefreshLayout xong: nếu cuộn giữa chừng rồi RefreshLayout chạy sau sẽ
            // reset offset về 0. Timer ổn-định-hoá re-assert cuộn tới đích cho tới khi offset dính (xem
            // ScheduleScrollToCurrentPage/ScrollSettleTimer); guard giữ tới lúc đó.
            ScheduleScrollToCurrentPage();
            System.Diagnostics.Debug.WriteLine($"Successfully loaded PDF via Skia: {path}");
        }
#pragma warning disable CA1031 // ranh giới UI: nạp PDF lỗi (PDFium/IO/layout) thì báo cho người dùng qua LoadFailed
        catch (Exception ex)
        {
            LoadFailed?.Invoke($"Lỗi khi mở file PDF: {ex.Message}");
        }
#pragma warning restore CA1031
    }

    // Zoom changes every page size, so the absolute scroll offset would point at a different page
    // after relayout (the view "jumps far"). Anchor on the document point at the viewport center:
    // capture page + fraction before relayout, then restore the same point afterwards.
    private void RefreshLayoutPreservingAnchor()
    {
        double centerY = PagesScrollViewer.VerticalOffset + PagesScrollViewer.ViewportHeight / 2;
        int anchorSlot = Core.SlotHitTester.CaptureAnchor(_slots, centerY, out double anchorFrac);

        RefreshLayout(keepCache: false);

        // The ScrollViewer extent updates on the next layout pass, so restore after it.
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!Core.SlotHitTester.TryRestoreCenterY(_slots, anchorSlot, anchorFrac, out double newCenterY)) return;
            PagesScrollViewer.ScrollToVerticalOffset(newCenterY - PagesScrollViewer.ViewportHeight / 2);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void RefreshLayout(bool keepCache = false)
    {
        if (_currentDocument == null) return;

        _slots.Clear();
        if (!keepCache)
            _pageCache.Clear();

        var sizes = _session!.GetPageSizes();

        // Canh giữa theo bề rộng thực của canvas Skia (không phải ScrollViewer.ViewportWidth).
        // skiaCanvas a Margin=10, donc on utilise ActualWidth - 20 comme fallback.
        double viewportWidth = skiaCanvas.ActualWidth > 0
            ? skiaCanvas.ActualWidth
            : Math.Max(0, this.ActualWidth - 20);

        int currentPageIndex = Math.Clamp(CurrentPage - 1, 0, _currentDocument.PageCount - 1);
        var layout = Core.PageLayoutCalculator.Compute(
            ViewMode, ShowCover, sizes,
            scale: ZoomLevel, viewportWidth: viewportWidth,
            pageGap: 12, unitGap: 20, currentPageIndex: currentPageIndex);

        _slots.AddRange(layout.Slots);
        InteractionCanvas.Width = layout.ContentWidth;
        InteractionCanvas.Height = layout.ContentHeight;

        skiaCanvas.InvalidateVisual();
    }

    // Đặt zoom ban đầu sao cho trang đầu vừa khung nhìn (fit whole page), chừa lề nhỏ.
    public void FitToViewport()
    {
        if (_currentDocument == null || _currentDocument.PageCount == 0) return;
        double vpW = skiaCanvas.ActualWidth > 0 ? skiaCanvas.ActualWidth : this.ActualWidth - 20;
        double vpH = skiaCanvas.ActualHeight > 0 ? skiaCanvas.ActualHeight : this.ActualHeight - 20;
        if (vpW <= 0 || vpH <= 0) return;
        var size = _session!.GetPageSize(0);
        if (size.Width <= 0 || size.Height <= 0) return;
        double fit = Math.Min((vpW - 24) / size.Width, (vpH - 24) / size.Height);
        ZoomLevel = Math.Clamp(fit, 0.4, 4.0);
    }

    private void ScrollToPage(int page)
    {
        int pageIndex = page - 1;
        var slot = _slots.FirstOrDefault(s => s.PageIndex == pageIndex);
        if (slot != null)
            PagesScrollViewer.ScrollToVerticalOffset(slot.Y);
    }

    private void OnPaintCanvas(object sender, SKPaintSurfaceEventArgs e)
    {
        if (_session == null) return;

        _frameComposer.Paint(_session, CaptureFrameState(), e);
    }

    private Viewer.ViewerFrameState CaptureFrameState() => new(
        _slots, ZoomLevel, (skiaCanvas.ActualWidth, skiaCanvas.ActualHeight),
        (PagesScrollViewer.HorizontalOffset, PagesScrollViewer.VerticalOffset), PagesScrollViewer.ViewportHeight,
        TryFindResource("TriThu.Brush.SurfaceTonalLow"),
        _pointerInput.SelectionPageIndex, _pointerInput.SelectionRects, HighlightQuery, Highlights, CurrentDocumentId, MatchSource);

    // Trong khi pending, ScrollChanged KHÔNG đồng bộ scroll->trang (chặn ghi đè CurrentPage về cover).
    // Timer ổn định hoá re-assert cuộn tới trang đích sau nạp (xem ScrollSettleTimer).
    private Viewer.ScrollSettleTimer? _scrollSettleTimer;

    private Viewer.ScrollSettleTimer ScrollSettle => _scrollSettleTimer ??= new Viewer.ScrollSettleTimer(
        PagesScrollViewer,
        () => _currentDocument != null,
        pageIndex => _slots.FirstOrDefault(s => s.PageIndex == pageIndex)?.Y);

    // Bật guard pending (chặn đồng bộ scroll->trang) NGAY trước các bước layout của LoadDocument để
    // ScrollChanged trung gian không kéo CurrentPage về trang đầu. Việc cuộn thực sự do timer ổn-định-hoá
    // (ScheduleScrollToCurrentPage) lo SAU khi layout xong.
    private void ArmPendingScrollGuard()
    {
        ScrollSettle.Arm((CurrentPage > 1 && ViewMode is Core.PdfViewMode.Continuous or Core.PdfViewMode.ContinuousFacing)
            ? CurrentPage : 0);
    }

    // Cuộn tới CurrentPage ở chế độ cuộn liên tục sau khi nạp tài liệu; guard giữ tới khi offset đã dính tại đích.
    private void ScheduleScrollToCurrentPage()
    {
        if (!(CurrentPage > 1 && ViewMode is Core.PdfViewMode.Continuous or Core.PdfViewMode.ContinuousFacing))
        {
            ScrollSettle.Cancel();
            return;
        }
        ScrollSettle.Begin(CurrentPage);
    }

    private void PagesScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_currentDocument == null || _slots.Count == 0) return;

        // Viewer ẩn (tab không active / đã rời Workspace) -> ScrollViewer reset offset/viewport=0 và phát
        // ScrollChanged; KHÔNG đồng bộ scroll->trang lúc đó, nếu không sẽ ghi đè OpenTab.Page về cover.
        // (Logic tách ở Core.ViewerScrollGate để test hồi quy — xem ViewerScrollGateTests.)
        if (!Core.ViewerScrollGate.ShouldSyncPageFromScroll(IsVisible, PagesScrollViewer.ViewportHeight)) return;

        // Đang chờ cuộn tới trang đích (sau khi nạp): KHÔNG đồng bộ scroll->trang để khỏi bị kéo về cover.
        // Guard được gỡ bởi timer ổn-định-hoá (ScrollSettleTimer) khi offset đã dính tại đích.
        if (ScrollSettle.IsPending)
        {
            skiaCanvas.InvalidateVisual();
            return;
        }

        // Xóa vùng chọn khi cuộn (tránh overlay bị lệch vị trí)
        if (e.VerticalChange != 0 || e.HorizontalChange != 0)
            _pointerInput.ClearSelection();

        skiaCanvas.InvalidateVisual();

        double middleY = PagesScrollViewer.VerticalOffset + PagesScrollViewer.ViewportHeight / 2;
        if (Core.SlotHitTester.AtOffset(_slots, middleY) is { } slot)
        {
            int newPage = slot.PageIndex + 1;
            if (CurrentPage != newPage) CurrentPage = newPage;
        }
    }

    private void DisposeCurrentDocument()
    {
        if (_session != null)
        {
            _pageCache.Clear();
            _session.Dispose();
            _session = null;
        }
    }

    // WPF có thể bắn Unloaded rồi Loaded lần nữa mà control không bị huỷ (gắn lại cây giao diện).
    // Unloaded chỉ nhả tài nguyên và gỡ event; Loaded dựng lại và nạp lại DocumentSource (#193).
    private void PdfViewerControl_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_lifecycle.OnUnloaded()) ReleaseResources();
    }

    private void PdfViewerControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_lifecycle.OnLoaded()) return;

        AttachInput();
        if (!string.IsNullOrEmpty(DocumentSource)) LoadDocument(DocumentSource);
    }

    private void ReleaseResources()
    {
        DetachInput();
        _pageCache.Clear();
        DisposeCurrentDocument();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;

        // Đã Unloaded thì tài nguyên đã được nhả, không nhả lại lần hai.
        bool needRelease = _lifecycle.IsAttached;
        if (_lifecycle.OnDispose())
        {
            if (needRelease) ReleaseResources();
            _pageCache.Dispose();
            _renderEngine.Dispose();
        }
    }
}
