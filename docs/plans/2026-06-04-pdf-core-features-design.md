# Thiết kế Hoàn thiện Tính năng PDF Cốt lõi (PDF Core Features Design)

## Tổng quan
Bổ sung các tính năng thiết yếu cho trình đọc PDF bao gồm: Điều hướng, Thu phóng, Tìm kiếm, Chú thích, Xoay và In ấn, đảm bảo trải nghiệm người dùng tương đương với các phần mềm chuyên nghiệp.

## 1. Điều hướng & Thu phóng (Navigation & Zoom)
- **UI:** Toolbar có các nút Zoom In/Out, ComboBox chọn tỷ lệ Zoom, Nút Previous/Next Page, và TextBox hiển thị/nhập số trang hiện tại.
- **Logic:**
    - Cập nhật `MainViewModel` với các thuộc tính: `ZoomLevel`, `CurrentPage`, `TotalPages`.
    - `PdfViewerControl` sẽ đồng bộ hóa các thuộc tính này với control `PDFViewer` của thư viện.
    - Hỗ trợ cuộn chuột (Mouse Wheel) kết hợp phím Ctrl để Zoom.

## 2. Tìm kiếm Văn bản (Text Search)
- **UI:** Ô tìm kiếm trên Toolbar với các nút mũi tên "Next" và "Previous".
- **Logic:** Sử dụng hàm `Search` có sẵn của `PDFViewer` để tìm và highlight văn bản. Tự động cuộn đến vị trí kết quả.

## 3. Chú thích & Đánh dấu (Annotation)
- **UI:** Các nút công cụ Highlight, Underline, Add Note.
- **Logic:**
    - Cho phép chọn văn bản trên `PDFViewer`.
    - Thêm đối tượng Annotation vào `PdfDocument`.
    - Hỗ trợ lưu thay đổi chú thích vào file PDF (Save/Save As).

## 4. Xoay, In ấn & Tiện ích (Utilities)
- **Xoay:** Hỗ trợ xoay tài liệu 90 độ trái/phải thông qua thuộc tính `Rotation`.
- **In ấn:** Tích hợp `PrintDialog` của Windows để in tài liệu.
- **Lưu file:** Lệnh `SaveCommand` để ghi lại các thay đổi vào file gốc hoặc file mới.

## Quản lý Trạng thái & Trải nghiệm
- Tất cả các lệnh điều khiển sẽ được triển khai dưới dạng `RelayCommand` trong ViewModel để đảm bảo tính module và dễ kiểm thử.
- Đảm bảo hiệu năng mượt mà khi xử lý các file PDF lớn hoặc có nhiều chú thích.
