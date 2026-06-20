# PDF Centering and Horizontal Scrolling Design

## 1. Objective
Ensure that PDF pages are centered within the viewer (along a vertical spine) and that horizontal scrolling functions correctly when the document is zoomed in or when the page width exceeds the viewport.

## 2. Approach: Individual Page Centering (Option A)
Each page in the document will be centered individually based on the total available width. This means smaller pages will be indented more to align their center with the center of larger pages, creating a continuous, visually pleasing "spine".

## 3. Architecture & Logic Changes

### 3.1. Layout Calculation (`RefreshLayout`)
- **Determine Viewport Width:** Retrieve the actual viewport width of the `ScrollViewer` (e.g., `PagesScrollViewer.ViewportWidth`).
- **Calculate `maxWidth`:** Iterate through all pages at the current `ZoomLevel` to find the widest page.
- **Determine Container Width:** The logical width of the document container is `Math.Max(maxWidth, viewportWidth)`.
- **Calculate Page X-Coordinates:** For each page, calculate its X-coordinate to center it within the container:
  `PageX = (ContainerWidth - PageWidth) / 2`
- **Update Canvas Dimensions:** Set `InteractionCanvas.Width = ContainerWidth` so the `ScrollViewer` knows the true horizontal extent.

### 3.2. Render Engine Update (`OnPaintCanvas`)
- The Skia surface coordinate system must account for both vertical and horizontal scroll offsets.
- Retrieve `viewLeft = PagesScrollViewer.HorizontalOffset`.
- Translate the canvas using both offsets: `canvas.Translate((float)-viewLeft, (float)-viewTop)`.
- Use the pre-calculated `PageX` (stored in `_pageRects`) when drawing the cached bitmaps:
  `canvas.DrawBitmap(bitmap, (float)rect.Left, (float)rect.Top)`

### 3.3. Responsive Resizing
- Subscribe to the `SizeChanged` event of the `ScrollViewer` (or the `UserControl`).
- When the window is resized, trigger `RefreshLayout()` so the `PageX` coordinates are recalculated and the pages remain centered dynamically.

## 4. Edge Cases & Error Handling
- **Missing Viewport Width on Init:** If `ViewportWidth` is 0 during initial load, fallback to the `ActualWidth` of the control or re-trigger layout once rendering completes.
- **Double Click Editing:** Ensure `HandleDoubleClick` and the `MicroEditor` positioning logic use the updated `rect.Left` coordinates, which now reflect the centered X-position.