# Panel chat: ẩn khi ở thư viện + cho chỉnh kích cỡ — Thiết kế

**Ngày:** 2026-06-22
**Trạng thái:** Đã duyệt thiết kế, chờ review spec

## Mục tiêu

1. Khi đang ở giao diện thư viện (`ShowLibrary == true`), KHÔNG hiện panel AI chat bên phải.
2. Panel chat kéo chỉnh được kích cỡ (bề rộng), nhớ trong phiên làm việc.

## Bối cảnh hiện trạng

`MainWindow.xaml` root là `Grid` 3 cột:
- Cột 0 (`Auto`): nav trái (`ColorZone` chứa nút Read/Edit).
- Cột 1 (`*`): vùng chính (toolbar + PDF viewer + lưới thư viện phủ lên khi `ShowLibrary`).
- Cột 2 (`Auto`): `materialDesign:Card` chat AI, `Width="350"` cố định.

`ShowLibrary` là `[ObservableProperty] bool` của `MainViewModel`, mặc định `true` (mở app vào thẳng thư viện). Đã có sẵn converter `InverseBoolToVisibilityConverter` (true → Collapsed) trong `MainWindow.xaml.cs`.

## Quyết định thiết kế

1. **Ẩn panel khi ở thư viện:** bind `Visibility` của Card chat (và của `GridSplitter`) vào `ShowLibrary` qua `InverseBoolToVisibilityConverter`.
2. **Resize:** thêm `GridSplitter`; bề rộng cột chat đưa vào VM dưới dạng `GridLength` bind hai chiều, để logic ẩn/hiện + nhớ-trong-phiên nằm ở VM (test được), không dùng code-behind.
3. **Nhớ kích cỡ:** chỉ trong phiên (không lưu settings). Đóng/mở lại app về mặc định 350px.
4. **Giới hạn kéo:** 280–700px.

## Kiến trúc & thành phần

### `MainWindow.xaml`

Root `Grid` đổi thành 4 cột:
- Cột 0 (`Auto`): nav trái (giữ nguyên).
- Cột 1 (`*`): vùng chính (giữ nguyên `Grid.Column="1"`).
- Cột 2 (`Auto`): `GridSplitter` mới.
- Cột 3: cột chat, `Width="{Binding ChatColumnWidth, Mode=TwoWay}"`, `MinWidth="{Binding ChatColumnMinWidth}"`, `MaxWidth="700"`.

`GridSplitter` (cột 2):
- `Width="6"`, `HorizontalAlignment="Stretch"`, `VerticalAlignment="Stretch"`.
- `ResizeDirection="Columns"`, `ResizeBehavior="PreviousAndNext"`.
- `Visibility="{Binding ShowLibrary, Converter={StaticResource InverseBoolToVisibilityConverter}}"`.

Card chat:
- Chuyển từ `Grid.Column="2"` sang `Grid.Column="3"`.
- BỎ `Width="350"` (để bề rộng cột điều khiển).
- Thêm `Visibility="{Binding ShowLibrary, Converter={StaticResource InverseBoolToVisibilityConverter}}"`.
- Giữ nguyên `Margin`, nội dung bên trong.

### `MainViewModel`

Thêm:
- `[ObservableProperty] private GridLength _chatColumnWidth = new GridLength(0);`
- `[ObservableProperty] private double _chatColumnMinWidth = 0;`
- `private double _savedChatWidthPx = 350;` (bề rộng px để khôi phục khi rời thư viện)
- `private const double DefaultChatWidthPx = 350;`, `private const double MinChatWidthPx = 280;`

Khởi tạo khớp trạng thái mở app (mặc định ở thư viện): width `0`, minWidth `0`, saved `350`. Nhờ vậy lúc mở app panel đã ẩn, và lần đầu mở sách sẽ khôi phục về 350px.

`partial void OnShowLibraryChanged(bool value)`:
- Nếu `value == true` (vào thư viện): nếu `ChatColumnWidth` đang là pixel và `> 0` thì lưu `_savedChatWidthPx = ChatColumnWidth.Value`; rồi đặt `ChatColumnWidth = new GridLength(0)` và `ChatColumnMinWidth = 0`.
- Nếu `value == false` (rời thư viện, mở sách): `ChatColumnMinWidth = MinChatWidthPx` và `ChatColumnWidth = new GridLength(_savedChatWidthPx)`.

`GridLength` thuộc `System.Windows` — `MainViewModel` đã tham chiếu `System.Windows` (dùng `MessageBox`) nên import được.

### Vì sao MinWidth động (0 ↔ 280)

`ColumnDefinition.MinWidth` cố định 280 sẽ ghì cột không cho thu về 0 khi ẩn. Do đó MinWidth cũng phải về 0 lúc ẩn, và 280 lúc hiện. `MaxWidth=700` thì cố định được (0 và mọi giá trị ≤700 đều hợp lệ).

## Luồng dữ liệu (tóm tắt)

```
Mở app: ShowLibrary=true (mặc định) -> width 0, minWidth 0 -> panel ẩn.
Mở sách (OpenLibraryItem -> ShowLibrary=false) -> OnShowLibraryChanged:
  width = savedChatWidthPx (350 lần đầu), minWidth = 280 -> panel hiện.
Kéo GridSplitter -> ColumnDefinition.Width đổi -> binding TwoWay ghi vào ChatColumnWidth.
Bấm Read (ShowLibraryView -> ShowLibrary=true) -> OnShowLibraryChanged:
  lưu savedChatWidthPx = bề rộng đang kéo, width 0, minWidth 0 -> panel ẩn.
Mở sách lại -> khôi phục đúng bề rộng đã kéo (trong phiên).
```

## Kiểm thử

**`MainViewModel` (unit, không GUI):**
- Mặc định (ShowLibrary=true lúc tạo): `ChatColumnWidth.Value == 0`, `ChatColumnMinWidth == 0`.
- Set `ShowLibrary = false` → `ChatColumnWidth.Value == 350`, `ChatColumnMinWidth == 280`.
- Nhớ trong phiên: `ShowLibrary=false` (về 350) → mô phỏng kéo bằng cách gán `ChatColumnWidth = new GridLength(500)` → `ShowLibrary=true` (lưu 500, width 0) → `ShowLibrary=false` → `ChatColumnWidth.Value == 500`.

**Verify GUI thủ công:**
- Ở thư viện: không thấy panel chat lẫn thanh splitter.
- Mở sách: panel chat hiện lại, kéo thanh splitter đổi được bề rộng (trong khoảng 280–700).
- Bấm Read về thư viện rồi mở sách lại: giữ đúng bề rộng vừa kéo.

## Phạm vi loại trừ (YAGNI)

- Không lưu bề rộng vào settings (chỉ trong phiên).
- Không thêm nút thu/mở panel thủ công (chỉ tự ẩn theo thư viện).
- Không kéo chỉnh chiều cao các vùng con trong panel.
