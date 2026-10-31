# Prototype UI cho Trí Thư

Skill `prototype` nhánh UI giả định app web (`?variant=` trên route có sẵn). Trí Thư là WPF, nên prototype UI làm thế này:

- **Hình thức:** một file HTML độc lập, mock lại khung app (danh sách Document, trang PDF, panel phải) với dữ liệu giả, các biến thể đổi bằng `?variant=` và thanh nổi dưới đáy. Gắn biến thể vào WPF tốn hơn nhiều mà câu hỏi thường chỉ là bố cục và luồng thao tác.
- **Màu:** lấy từ `src/PdfReaderApp/Themes/TriThuTokens.xaml` (bản sáng), khai báo thành biến CSS. Nút dài bo góc 12 (UI-2).
- **Màn hình đã có thiết kế:** lấy từ Claude Design trước khi mock, không vẽ lại theo trí nhớ.
- **Vị trí và tên:** cạnh view liên quan, tên `prototype-<issue>-<chủ-đề>.html` (vd `src/PdfReaderApp/prototype-251-chon-tap-note.html`).
- **Lưu giữ:** commit lên nhánh vứt đi `prototype/<issue>-<chủ-đề>`, push, không mở PR, không merge. Ghi biến thể được chọn và tên nhánh vào comment kết luận của issue.

Mẫu đầy đủ: nhánh `prototype/251-chon-tap-note`.
