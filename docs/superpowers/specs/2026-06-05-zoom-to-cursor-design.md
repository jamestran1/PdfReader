# Zoom to Cursor Design Specification

## 1. Objective
Refine the zoom functionality in the PDF Reader by implementing standard "Ctrl + Mouse Wheel" zooming. Crucially, the zoom action must anchor on the user's mouse cursor, ensuring the content beneath the cursor remains stationary during the zoom operation (similar to modern browsers and map applications).

## 2. Event Handling
- **Target Event:** Subscribe to the `PreviewMouseWheel` event on the outer container (e.g., the `UserControl` or `ScrollViewer`) in `PdfViewerControl.xaml.cs`.
- **Modifier Check:** Check if `Keyboard.Modifiers.HasFlag(ModifierKeys.Control)`. If true, proceed with the zoom logic and set `e.Handled = true` to prevent the default vertical scrolling behavior.

## 3. Zoom Calculation Logic
- Determine zoom direction from `e.Delta` (positive = zoom in, negative = zoom out).
- Adjust the `ZoomLevel` by a defined step (e.g., +/- 10% or 0.1).
- **Constraints:** Clamp the `ZoomLevel` between a minimum (e.g., 0.1 / 10%) and a maximum (e.g., 5.0 / 500%).

## 4. Cursor Anchoring Algorithm
To keep the content under the cursor stationary, the scroll offsets must be adjusted after the zoom level changes.

1. **Capture Initial State:**
   - Get the mouse position relative to the `ScrollViewer` viewport: `mousePos = e.GetPosition(PagesScrollViewer)`.
   - Record the current scroll offsets: `oldHOffset = PagesScrollViewer.HorizontalOffset`, `oldVOffset = PagesScrollViewer.VerticalOffset`.
   - Calculate the absolute point on the content: 
     `absoluteX = oldHOffset + mousePos.X`
     `absoluteY = oldVOffset + mousePos.Y`
   - Calculate the relative position of the mouse on the content (independent of zoom):
     `relX = absoluteX / OldZoomLevel`
     `relY = absoluteY / OldZoomLevel`

2. **Apply Zoom:**
   - Update the `ZoomLevel` property. This triggers the `RefreshLayout` method via the `DependencyProperty` callback, which recalculates the sizes of the internal canvases.

3. **Adjust Scroll Offsets:**
   - Calculate the new absolute point where the cursor *should* point:
     `newAbsoluteX = relX * NewZoomLevel`
     `newAbsoluteY = relY * NewZoomLevel`
   - Calculate the new scroll offsets required to place `newAbsoluteX/Y` under the original `mousePos`:
     `newHOffset = newAbsoluteX - mousePos.X`
     `newVOffset = newAbsoluteY - mousePos.Y`
   - Force an immediate layout update if necessary (using `UpdateLayout()`) so the `ScrollViewer` recognizes the new content boundaries.
   - Apply the new offsets: `PagesScrollViewer.ScrollToHorizontalOffset(newHOffset)` and `PagesScrollViewer.ScrollToVerticalOffset(newVOffset)`.

## 5. UI Synchronization
- Because `ZoomLevel` is a two-way bound `DependencyProperty`, updating it programmatically during the mouse wheel event will automatically update the percentage text displayed in the top toolbar in `MainWindow.xaml`.