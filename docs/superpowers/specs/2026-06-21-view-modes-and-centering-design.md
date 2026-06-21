# Spec — View modes, page centering, zoom slider

**Ngày:** 2026-06-21
**Trạng thái:** Đã duyệt thiết kế, chờ review spec → writing-plans
**Nhánh:** `feature/view-modes` (stack trên `feature/search-display-highlight`)

## Mục tiêu

1. Thêm 4 chế độ xem: **Single Page, Continuous, Facing, Continuous Facing**.
2. Trang luôn **canh giữa** theo chiều ngang (hiện đang lệch trái).
3. Toggle **tách bìa** cho các chế độ Facing.
4. Thêm **thanh trượt zoom** (40%–400%) cạnh nút +/- hiện có.

## Đánh giá độ ảnh hưởng

Mức: **TRUNG BÌNH**, khu trú ở viewer + toolbar.
- Chạm: `PdfViewerControl` (layout + paint + navigation/scroll), `MainWindow.xaml` (toolbar), `MainViewModel` (state), + class layout mới + converters.
- Không đụng search/AI/index. Không breaking API ngoài.
- Rủi ro: đồng bộ trang↔scroll, đặc biệt Single Page/Facing (single-unit). Layout từng có bug centering + zoom-jump nên cần test kỹ phần tính rect.

## Mô hình

Hai trục độc lập tạo nên 4 chế độ:
- **Paired**: 1 trang/đơn vị (đơn) hay 2 trang/đơn vị (facing).
- **Continuous**: xếp dọc tất cả đơn vị, cuộn mượt; hay **single-unit**: chỉ hiện 1 đơn vị.

| Chế độ | Paired | Continuous |
|---|---|---|
| Continuous (mặc định) | không | có |
| Single Page | không | không |
| Continuous Facing | có | có |
| Facing | có | không |

**Ghép cặp facing** theo `ShowCover`:
- Tách bìa (mặc định `true`): `(0)`, `(1,2)`, `(3,4)`…
- Không tách (`false`): `(0,1)`, `(2,3)`…

## Kiến trúc

### `PageLayoutCalculator` (class thuần, testable) — MỚI
`src/PdfReaderApp/Core/PageLayoutCalculator.cs`

```csharp
public enum PdfViewMode { Continuous, SinglePage, ContinuousFacing, Facing }

public sealed record PageSlot(int PageIndex, double X, double Y, double Width, double Height);
public sealed record LayoutResult(
    IReadOnlyList<PageSlot> Slots,   // rect (px) đã canh giữa, theo thứ tự trang
    double ContentWidth,
    double ContentHeight,
    IReadOnlyList<int> UnitStartIndex); // chỉ số trang bắt đầu mỗi "đơn vị" (row), để snap/navigate

public static class PageLayoutCalculator
{
    // pageSizesPt: kích thước từng trang (point). spacing/gap: px. viewportWidth: px (bề rộng paint thực).
    // currentUnitPageIndex: trang hiện tại (dùng cho single-unit để chỉ xuất 1 đơn vị).
    public static LayoutResult Compute(
        PdfViewMode mode, bool showCover,
        IReadOnlyList<(double WidthPt, double HeightPt)> pageSizesPt,
        double scale, double viewportWidth,
        double pageGap, double unitGap, int currentPageIndex);
}
```

Quy tắc:
- Gom trang thành **đơn vị (unit)**: đơn → mỗi unit 1 trang; facing → ghép cặp theo `showCover`.
- **Continuous / ContinuousFacing:** xuất MỌI unit, xếp dọc, cách nhau `unitGap`.
- **SinglePage / Facing:** chỉ xuất unit chứa `currentPageIndex`.
- Mỗi unit: các trang trong unit đặt cạnh nhau (cách `pageGap`), bề rộng unit = tổng; **canh giữa**: offset X của unit = `max(0, (viewportWidth - unitWidth) / 2)`.
- `ContentWidth = max(viewportWidth, unit rộng nhất)`; `ContentHeight` = tổng chiều cao (single-unit thì = chiều cao unit hiện tại).
- Mỗi trang: `Width = WidthPt * scale`, `Height = HeightPt * scale`.

### `PdfViewerControl`
- DP mới: `ViewMode` (PdfViewMode), `ShowCover` (bool) — đổi → `RefreshLayout`.
- `RefreshLayout`: lấy `viewportWidth = skiaCanvas.ActualWidth` (bề rộng paint thực, không phải `ScrollViewer.ViewportWidth`), gọi `PageLayoutCalculator.Compute(...)`, set `_pageRects` từ `Slots`, `InteractionCanvas.Width/Height` = `ContentWidth/ContentHeight`.
- `OnPaintCanvas`: vẽ như cũ theo `_pageRects` (đã canh giữa); giữ `RefreshLayoutPreservingAnchor` cho zoom.
- **Single-unit nav (SinglePage/Facing):** chỉ unit hiện tại được layout. Next/Prev và mouse-wheel chuyển unit: wheel xuống ở đáy unit → unit kế; wheel lên ở đỉnh → unit trước; nếu unit cao hơn viewport (zoom lớn) thì wheel cuộn trong unit trước, tới biên mới chuyển. `CurrentPage` = trang đầu unit.
- **Continuous nav:** cuộn tự do; `CurrentPage` = trang tại tâm viewport (như hiện tại, dựa trên `_pageRects`).

### `MainViewModel`
- `[ObservableProperty] PdfViewMode viewMode = PdfViewMode.Continuous;`
- `[ObservableProperty] bool showCoverSeparately = true;`
- (ZoomLevel đã có; slider bind vào nó.)

### `MainWindow.xaml` toolbar (trên cùng, cụm zoom)
- Slider zoom: `Minimum=0.4 Maximum=4.0 Value="{Binding ZoomLevel, Mode=TwoWay}"`, đặt giữa nút − và +.
- 4 ToggleButton chế độ (nhóm radio qua converter `ViewModeToBoolConverter` hoặc `IsChecked` so khớp enum) bind `ViewMode`.
- 1 ToggleButton "tách bìa" bind `ShowCoverSeparately`, chỉ enabled khi `ViewMode` là Facing/ContinuousFacing.
- Icon: dùng MaterialDesign PackIcon gần nghĩa (vd `Note`, `ViewSequential`, `BookOpenPageVariant`, `ViewGrid`).

## Chiến lược test

- **Unit (`PageLayoutCalculatorTests`)** — trọng tâm:
  - Continuous: N trang → N slot xếp dọc, mỗi slot canh giữa (X = (vw-w)/2).
  - SinglePage: chỉ 1 slot = currentPage.
  - Facing + showCover=true: unit đầu 1 trang (bìa), kế tiếp cặp (1,2)…; X của cặp canh giữa.
  - Facing + showCover=false: cặp (0,1),(2,3)…
  - ContinuousFacing: mọi cặp xếp dọc.
  - Canh giữa: trang hẹp hơn viewport → X>0 và đối xứng; rộng hơn → X=0, ContentWidth=unitWidth.
  - `UnitStartIndex` đúng cho navigation/snap.
- **VM** (`MainViewModelTests`): `ViewMode`/`ShowCoverSeparately` mặc định + đổi giá trị.
- **Thủ công (GUI):** 4 chế độ hiển thị đúng; trang canh giữa; slider zoom; tách bìa; Single/Facing snap Next/Prev/wheel; zoom không nhảy.

## Ngoài phạm vi (YAGNI)
- Không thumbnails, không "fit width/page" tự động, không đổi search/AI/index.
