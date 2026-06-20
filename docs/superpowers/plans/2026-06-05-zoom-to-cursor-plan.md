# Zoom to Cursor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement Ctrl + Mouse Wheel zooming that anchors the document to the user's cursor position.

**Architecture:** Intercept `PreviewMouseWheel` events on the outer container (`PdfViewerControl`), calculate the exact relative point under the cursor, update the `ZoomLevel`, and then aggressively update the `ScrollViewer`'s horizontal and vertical offsets to keep that point under the cursor.

**Tech Stack:** C#, WPF

---

### Task 1: Intercept Mouse Wheel Events

**Files:**
- Modify: `src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs`

- [ ] **Step 1: Subscribe to PreviewMouseWheel**

In the constructor of `PdfViewerControl.xaml.cs`, add the subscription:

```csharp
    public PdfViewerControl()
    {
        InitializeComponent();
        this.Unloaded += PdfViewerControl_Unloaded;
        InteractionCanvas.MouseDown += OnCanvasMouseDown;
        
        this.SizeChanged += PdfViewerControl_SizeChanged;
        this.PreviewMouseWheel += PdfViewerControl_PreviewMouseWheel;
        
        TextEditor.EditingFinished += TextEditor_EditingFinished;
        TextEditor.EditingCancelled += TextEditor_EditingCancelled;
    }
```

Update the `Dispose` method to unsubscribe:

```csharp
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                InteractionCanvas.MouseDown -= OnCanvasMouseDown;
                this.SizeChanged -= PdfViewerControl_SizeChanged;
                this.PreviewMouseWheel -= PdfViewerControl_PreviewMouseWheel;
                DisposeCurrentDocument();
                _renderEngine.Dispose();
            }
            _disposed = true;
        }
    }
```

- [ ] **Step 2: Implement the Zoom logic**

Add the event handler method below the constructor:

```csharp
    private void PdfViewerControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;

            if (_currentDocument == null) return;

            // 1. Capture initial state
            Point mousePos = e.GetPosition(PagesScrollViewer);
            double oldHOffset = PagesScrollViewer.HorizontalOffset;
            double oldVOffset = PagesScrollViewer.VerticalOffset;
            double oldZoom = ZoomLevel;

            double absoluteX = oldHOffset + mousePos.X;
            double absoluteY = oldVOffset + mousePos.Y;

            double relX = absoluteX / oldZoom;
            double relY = absoluteY / oldZoom;

            // 2. Calculate new zoom level
            double zoomDelta = e.Delta > 0 ? 0.1 : -0.1;
            double newZoom = oldZoom + zoomDelta;

            // Constrain zoom
            newZoom = Math.Max(0.1, Math.Min(5.0, newZoom));
            
            // If hitting limits, don't jump layout
            if (Math.Abs(newZoom - oldZoom) < 0.01) return;

            // This updates DependencyProperty and triggers RefreshLayout via OnZoomLevelChanged
            ZoomLevel = newZoom;

            // Force layout update so ScrollViewer knows about new InteractionCanvas size
            this.UpdateLayout();

            // 3. Adjust scroll offsets to anchor cursor
            double newAbsoluteX = relX * newZoom;
            double newAbsoluteY = relY * newZoom;

            double newHOffset = newAbsoluteX - mousePos.X;
            double newVOffset = newAbsoluteY - mousePos.Y;

            PagesScrollViewer.ScrollToHorizontalOffset(newHOffset);
            PagesScrollViewer.ScrollToVerticalOffset(newVOffset);
        }
    }
```

- [ ] **Step 3: Verify compilation**

Run: `dotnet build src/PdfReaderApp/PdfReaderApp.csproj`
Expected: Build succeeds with no errors.

- [ ] **Step 4: Commit changes**

```bash
git add src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs
git commit -m "feat: Implement Ctrl+MouseWheel zoom to cursor"
```