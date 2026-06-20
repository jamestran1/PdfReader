# Thiết kế Tích hợp PDF Viewer thực tế vào ứng dụng

## Tổng quan
Thay thế phần giao diện giữ chỗ (placeholder) bằng một hệ thống hiển thị PDF thực tế sử dụng thư viện PDFium, tuân thủ mô hình MVVM để dễ dàng mở file, chuyển trang và thu phóng.

## Lựa chọn Công nghệ
- **Thư viện:** `PdfiumViewer` (phiên bản fork hỗ trợ .NET Core/8/10) và `PdfiumViewer.Native.x86_64.v8-xfa` (thư viện native của Google).
- **Mô hình:** WPF Custom UserControl với Dependency Properties.

## Các thành phần chính
1. **PdfViewerControl (UserControl):**
   - Chứa một `PdfRenderer` hoặc `Image` control để hiển thị nội dung render từ PDFium.
   - Thuộc tính `DocumentSource` (string): Nhận đường dẫn file PDF từ ViewModel.
   - Thuộc tính `Zoom` (double): Điều khiển mức độ thu phóng.
   - Thuộc tính `CurrentPage` (int): Điều khiển và hiển thị trang hiện tại.

2. **MainViewModel (Cập nhật):**
   - `FilePath`: Lưu đường dẫn file đang mở.
   - `OpenCommand`: Lệnh mở hộp thoại chọn file.
   - `NextPageCommand` / `PreviousPageCommand`: Lệnh điều hướng trang.

## Luồng dữ liệu (Data Flow)
1. Người dùng nhấn "Open PDF" -> `OpenCommand` thực thi.
2. `OpenFileDialog` lấy đường dẫn -> Gán vào `FilePath`.
3. `PdfViewerControl` nhận thay đổi qua Binding -> Gọi thư viện PDFium nạp file.
4. Render trang hiện tại lên giao diện.
5. Khi người dùng zoom hoặc chuyển trang, ViewModel cập nhật trạng thái và UI phản hồi tương ứng qua Binding.

## Xử lý lỗi
- **File không hợp lệ:** Hiển thị thông báo lỗi nếu file bị hỏng hoặc không phải định dạng PDF.
- **Tải file lớn:** Sử dụng Background task để nạp file, tránh làm đứng giao diện chính.
