# PDF Centering and Horizontal Scrolling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Center PDF pages individually along a vertical spine and enable horizontal scrolling when zoomed in.

**Architecture:** Calculate layout based on the `ScrollViewer`'s `ViewportWidth` instead of just the maximum page width. Shift the Skia canvas coordinate system horizontally using the scroll offset to render the correct view portion. Handle viewport size changes responsively.

**Tech Stack:** C#, WPF, SkiaSharp.

---

### Task 1: Responsive Layout & Container Calculation

**Files:**
- Modify: `src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs`

- [ ] **Step 1: Update RefreshLayout logic for centering**

```csharp
    private void RefreshLayout()
    {
        if (_currentDocument == null) return;

        _pageRects.Clear();
        _pageCache.Values.ToList().ForEach(b => b.Dispose());
        _pageCache.Clear();

        double currentY = 0;
        double maxPageWidth = 0;
        float scale = (float)ZoomLevel;

        // Pass 1: Find the maximum page width
        for (int i = 0; i < _currentDocument.PageCount; i++)
        {
            var pageSize = _currentDocument.Pages[i].Size;
            double w = pageSize.Width * scale;
            maxPageWidth = Math.Max(maxPageWidth, w);
        }

        // Determine container width. Fallback to ActualWidth if ViewportWidth is 0.
        double viewportWidth = PagesScrollViewer.ViewportWidth;
        if (viewportWidth == 0) viewportWidth = this.ActualWidth;
        
        double containerWidth = Math.Max(maxPageWidth, viewportWidth);

        // Pass 2: Calculate centered rects
        for (int i = 0; i < _currentDocument.PageCount; i++)
        {
            var pageSize = _currentDocument.Pages[i].Size;
            double w = pageSize.Width * scale;
            double h = pageSize.Height * scale;
            
            // Center the page horizontally
            double x = (containerWidth - w) / 2;
            
            _pageRects.Add(new Rect(x, currentY, w, h));
            currentY += h + 20; // 20px spacing
        }

        InteractionCanvas.Width = containerWidth;
        InteractionCanvas.Height = currentY;
        
        skiaCanvas.InvalidateVisual();
    }
```

- [ ] **Step 2: Add SizeChanged event handler**

In the constructor:
```csharp
    public PdfViewerControl()
    {
        InitializeComponent();
        this.Unloaded += PdfViewerControl_Unloaded;
        InteractionCanvas.MouseDown += OnCanvasMouseDown;
        
        // Add size changed event
        this.SizeChanged += PdfViewerControl_SizeChanged;
        
        TextEditor.EditingFinished += TextEditor_EditingFinished;
        TextEditor.EditingCancelled += TextEditor_EditingCancelled;
    }
```

Add the event handler method:
```csharp
    private void PdfViewerControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_currentDocument != null)
        {
            RefreshLayout();
        }
    }
```

Update `Dispose` to unsubscribe:
```csharp
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                InteractionCanvas.MouseDown -= OnCanvasMouseDown;
                this.SizeChanged -= PdfViewerControl_SizeChanged;
                DisposeCurrentDocument();
                _renderEngine.Dispose();
            }
            _disposed = true;
        }
    }
```

- [ ] **Step 3: Build the project to verify compilation**

Run: `dotnet build src/PdfReaderApp/PdfReaderApp.csproj`
Expected: Build succeeds with no new errors.

- [ ] **Step 4: Commit changes**

```bash
git add src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs
git commit -m "feat: Calculate layout with centered pages based on viewport width"
```

---

### Task 2: Implement Horizontal Rendering Translation

**Files:**
- Modify: `src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs`

- [ ] **Step 1: Update OnPaintCanvas to handle horizontal offset**

```csharp
    private void OnPaintCanvas(object sender, SKPaintSurfaceEventArgs e)
    {
        if (_currentDocument == null || e.Surface == null) return;

        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.DimGray);

        float scale = (float)ZoomLevel;
        
        double viewTop = PagesScrollViewer.VerticalOffset;
        double viewBottom = viewTop + PagesScrollViewer.ViewportHeight;
        
        // Retrieve horizontal offset
        double viewLeft = PagesScrollViewer.HorizontalOffset;

        // Translate canvas for BOTH X and Y scrolling
        canvas.Translate((float)-viewLeft, (float)-viewTop);

        for (int i = 0; i < _pageRects.Count; i++)
        {
            var rect = _pageRects[i];
            
            // Only vertical culling is strictly necessary here, but horizontal culling could be added for performance if pages are very wide.
            if (rect.Bottom >= viewTop && rect.Top <= viewBottom)
            {
                _objectManager.MapPage(_currentDocument.Pages[i], i);

                if (!_pageCache.ContainsKey(i))
                {
                    _pageCache[i] = _renderEngine.RenderPage(_currentDocument.Pages[i], scale);
                }

                var bitmap = _pageCache[i];
                
                // Draw at the pre-calculated centered coordinates
                canvas.DrawBitmap(bitmap, (float)rect.Left, (float)rect.Top);
                
                using var paint = new SKPaint { Color = SKColors.Black, IsStroke = true, StrokeWidth = 1 };
                canvas.DrawRect((float)rect.Left, (float)rect.Top, (float)rect.Width, (float)rect.Height, paint);
            }
            else
            {
                if (_pageCache.Count > 20 && _pageCache.ContainsKey(i))
                {
                    _pageCache[i].Dispose();
                    _pageCache.Remove(i);
                }
            }
        }
    }
```

- [ ] **Step 2: Build the project to verify compilation**

Run: `dotnet build src/PdfReaderApp/PdfReaderApp.csproj`
Expected: Build succeeds with no new errors.

- [ ] **Step 3: Commit changes**

```bash
git add src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs
git commit -m "feat: Add horizontal translation to rendering engine for centered scrolling"
```
