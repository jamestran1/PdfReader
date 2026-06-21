# Spec — Cải thiện hiển thị kết quả Search & vòng đời Highlight

**Ngày:** 2026-06-21
**Trạng thái:** Đã duyệt thiết kế, chờ review spec → writing-plans

## Mục tiêu

Cải thiện trải nghiệm tìm kiếm trong Ultimate PDF Reader:

1. Kết quả search hiển thị **to hơn, rõ nội dung hơn** (popup lớn, snippet dài, **có dấu**, từ khóa in đậm).
2. Click kết quả → mở đúng trang (đã có, giữ nguyên).
3. Trang mở ra → **highlight từ khóa** trên trang (đã có, giữ nguyên).
4. Khi người dùng **ngừng search → highlight biến mất**.

## Đánh giá độ ảnh hưởng (impact assessment)

Mức: **THẤP–TRUNG BÌNH**. Không breaking change, không đổi kiến trúc Hybrid Engine.

| Khía cạnh | Đánh giá |
|---|---|
| Breaking change | Không. `SearchResult.Snippet` đổi *ngữ nghĩa* (folded → text gốc có dấu) nhưng là API nội bộ, chỉ popup tiêu thụ. |
| Thay đổi kiến trúc | Không. Vẫn dùng index SQLite/FTS5 + SkiaSharp render hiện tại. |
| Nguy cơ làm code tệ đi | Thấp. Thêm 1 helper thuần (`FoldWithMap`) và 1 UI behavior nhỏ; không tăng coupling giữa các tầng. |
| Phạm vi lan tỏa | 4–5 file: `SearchNormalizer.cs`, `SqliteDocumentIndex.cs`, `MainWindow.xaml`, `MainViewModel.cs`, + 1 behavior/converter UI mới. |

### Hiện trạng (chứng cứ)

- `SelectSearchResult` (`MainViewModel.cs` ~267-273): set `CurrentPage` + `SelectedSearchQuery` → click mở trang **đã hoạt động**.
- `DrawHighlights` (`PdfViewerControl.xaml.cs` ~401-453): tô rect vàng tại dòng khớp → highlight trên trang **đã hoạt động**.
- Popup (`MainWindow.xaml` ~140-160): nhỏ (320×300), mỗi item chỉ số trang + `Snippet`.
- `chunks_fts` index cột `search_text` (đã fold, **mất dấu**); `snippet(chunks_fts,0,'[',']','...',12)` → snippet **mất dấu, viết thường**. Cột `text` gốc có dấu vẫn được lưu trong bảng `chunks`.
- `SelectedSearchQuery` chỉ được **set**, **không bao giờ clear** → highlight không tự mất (gap chính).
- `SearchNormalizer.Fold` **không bảo toàn độ dài** (NFD + drop marks + gộp whitespace + trim) → không thể map offset từ snippet folded sang text gốc nếu không có bản đồ vị trí.

## Thiết kế chi tiết

### 1. Helper `FoldWithMap` (`SearchNormalizer.cs`)

Thêm phương thức trả về chuỗi folded **kèm bản đồ** từ chỉ số folded → chỉ số ký tự gốc, để có thể định vị match trên text gốc.

```csharp
// Trả về (folded, map) với map[i] = chỉ số trong chuỗi gốc của ký tự folded thứ i.
public static (string folded, int[] map) FoldWithMap(string s);
```

Logic giống `Fold` hiện tại nhưng theo dõi chỉ số nguồn qua từng bước (d-stroke, NFD, drop marks, lowercase, gộp whitespace). `Fold(s)` hiện tại giữ nguyên (có thể refactor để gọi chung lõi, không bắt buộc).

### 2. Dựng snippet có dấu (`SqliteDocumentIndex.SearchText`)

- Query trả thêm `c.text` (text gốc) thay vì chỉ dựa `snippet()` trên cột folded.
- Trong C#: với mỗi hit, `FoldWithMap(text)` → tìm vị trí `phrase` (folded query) trong folded text → map về chỉ số gốc → cắt **cửa sổ ngữ cảnh** (vd ~40 ký tự mỗi bên, kèm dấu `...`) quanh match từ **text gốc** → gán vào `SearchResult.Snippet`.
- Nếu không tìm thấy (hiếm), fallback: cắt đầu `text` gốc (substr).
- `SearchResult` giữ nguyên shape `(PageIndex, Snippet, ChunkId)`; `Snippet` giờ là text gốc có dấu.
- Nhánh LIKE fallback (query < 3 ký tự) cũng đổi để cắt từ `text` gốc.

### 3. Popup to hơn + in đậm từ khóa (`MainWindow.xaml`)

- Tăng width (~320 → ~420), tăng `MaxHeight` hợp lý, item cao hơn với padding rộng, font lớn hơn.
- Số trang ở trên (đậm/nhỏ), snippet ở dưới (lớn, có dấu).
- **In đậm từ khóa:** thêm 1 attached behavior/markup nhỏ (vd `HighlightTextBehavior`) gắn lên `TextBlock` của snippet. Behavior nhận `Snippet` + `Query` (query đã thực thi), dùng `SearchNormalizer.Fold` (accent-insensitive) để tìm **mọi** occurrence trong snippet và dựng các `Run` (thường / **đậm**). Re-fold trên chuỗi snippet ngắn → rẻ.
- Query cho behavior: bind tới một property VM mới `ExecutedSearchQuery` (query tại thời điểm chạy `Search()`), qua `RelativeSource` tới `DataContext` của `ItemsControl`.

### 4. Nút X (clear) trong ô search (`MainWindow.xaml`)

- Thêm button X ở cuối `SearchBox`, hiển thị khi `SearchQuery` khác rỗng (dùng converter/trigger sẵn có hoặc đơn giản luôn hiện).
- Click X → đặt `SearchQuery = ""` (kéo theo logic clear ở mục 5).

### 5. Vòng đời highlight (`MainViewModel.cs`)

- Thêm property `ExecutedSearchQuery` (string) — set trong `Search()` = query vừa chạy (để UI bold).
- Thêm partial method `OnSearchQueryChanged(string value)`:
  - **Luôn** clear `SelectedSearchQuery = ""` (tắt highlight trên trang) mỗi khi `SearchQuery` đổi.
  - Nếu `string.IsNullOrWhiteSpace(value)` → `SearchResults.Clear()` và `ExecutedSearchQuery = ""`.
- Quy tắc đơn lẻ này thỏa cả 3 trigger người dùng chọn:
  - Xóa rỗng ô search → highlight tắt + kết quả tắt.
  - Nhấn X (set query = "") → highlight tắt + kết quả tắt.
  - Đổi/gõ query mới → highlight cũ tắt ngay; chỉ bật lại khi click kết quả mới.
- `SelectSearchResult` giữ nguyên: set `CurrentPage` (cuộn tới **đầu trang**) + `SelectedSearchQuery` (bật highlight mọi match trên trang).

*Không* clear khi Esc / click ra ngoài (theo lựa chọn người dùng).

## Luồng dữ liệu

```
gõ query ──OnSearchQueryChanged──► clear SelectedSearchQuery (+clear results nếu rỗng)
   │
  Enter ──► Search() ──► ExecutedSearchQuery=query; nạp SearchResults (snippet có dấu)
   │
popup hiện (to, có dấu, từ khóa đậm)
   │
click kết quả ──► SelectSearchResult ──► CurrentPage=trang (cuộn đầu trang)
                                     └─► SelectedSearchQuery=query ──► DrawHighlights tô vàng mọi match
   │
sửa/xóa query hoặc nhấn X ──► OnSearchQueryChanged ──► SelectedSearchQuery="" ──► highlight biến mất
```

## Chiến lược test

- **Unit test (`MainViewModel`)** — phần testable không cần UI:
  - Đổi `SearchQuery` (khác rỗng) → `SelectedSearchQuery` về `""`.
  - Set `SearchQuery = ""` → `SearchResults` rỗng và `ExecutedSearchQuery == ""`.
  - `Search()` set `ExecutedSearchQuery` đúng query.
  - `SelectSearchResult(result)` → `CurrentPage == result.PageIndex + 1` và `SelectedSearchQuery == SearchQuery`.
- **Unit test (`SearchNormalizer.FoldWithMap`)**:
  - `folded` khớp `Fold(s)`.
  - `map` đúng: với input có dấu tiếng Việt + nhiều khoảng trắng, `map[i]` trỏ về ký tự gốc hợp lệ; map có dấu (vd "bảo hiểm") định vị lại đúng substring gốc.
- **Unit test (snippet builder trong `SqliteDocumentIndex`)** *(nếu tách được hàm thuần)*:
  - Snippet trả về chứa text **có dấu** quanh match; có `...` khi cắt; fallback khi không tìm thấy.
- **Thủ công (XAML/vẽ):** popup hiển thị to + snippet có dấu + từ khóa đậm; click → cuộn + highlight vàng; xóa ô/nhấn X/đổi query → highlight mất.

## Ngoài phạm vi (YAGNI)

- Không cuộn tới đúng dòng match (chỉ đầu trang).
- Không tô màu khác cho occurrence được click.
- Không thêm bounding box/tọa độ vào `SearchResult` hay index.
- Không đổi sidebar/panel; giữ dạng popup.
