# Thiết kế Chi tiết: Ultimate PDF Reader & Editor (Material Design 3 Edition)

## 1. Tầm nhìn & Mục tiêu
Tạo ra một phần mềm đọc và chỉnh sửa PDF đẳng cấp thế giới, kết hợp hiệu năng render siêu hạng của GPU (Skia) với khả năng can thiệp sâu vào cấu trúc file PDF (PDFium) và ngôn ngữ thiết kế hiện đại nhất (Material Design 3).

## 2. Kiến trúc Hệ thống (Hybrid Engine Architecture)

### 2.1. Lớp Render (Graphics Engine)
- **Công nghệ:** SkiaSharp (C# Wrapper của Skia - nhân đồ họa Chrome/Flutter).
- **VirtualCanvas:** Một vùng làm việc ảo tự quản lý việc vẽ các pixel lên màn hình.
- **Cơ chế Ảo hóa:** Chỉ render những phần đang hiển thị trên Viewport, cho phép cuộn mượt mà qua hàng chục nghìn trang mà không bị giới hạn 32k pixel của WPF truyền thống.
- **Infinite Zoom:** Hỗ trợ thu phóng vô hạn với chất lượng sắc nét nhờ cơ chế render vector trực tiếp.

### 2.2. Lớp Logic (PDF Core)
- **Công nghệ:** PDFium Core API (Cấp thấp).
- **Ghost Mapping:** Ánh xạ từng đối tượng trong PDF (Text, Image, Path) thành các `SkiaObject` tương ứng để có thể tương tác (click, kéo, sửa).
- **Incremental Updates:** Chỉ lưu lại các phần thay đổi vào file gốc thay vì vẽ lại toàn bộ file, giúp tốc độ lưu file cực nhanh.

## 3. Giao diện Người dùng (Material Design 3 Shell)

### 3.1. Ngôn ngữ Thiết kế
- **M3 Standards:** Sử dụng bộ linh kiện (Components) chuẩn MD3 cho WPF.
- **Dynamic Color (Material You):** Tự động điều chỉnh bảng màu (Palette) dựa trên hình nền người dùng hoặc màu sắc chủ đạo của file PDF.
- **Typography:** Sử dụng các chuẩn font hiện đại (ví dụ Roboto hoặc các font hệ thống tối ưu).

### 3.2. Cấu trúc UI
- **Navigation Rail:** Thanh điều hướng hẹp ở bên trái chứa các chức năng chính (Đọc, Sửa, Chat AI, Cài đặt).
- **Contextual Action Bars:** Thanh công cụ "nổi" xuất hiện ngay cạnh phần tử đang được chọn (Text/Image).
- **Cards & Dialogs:** Sử dụng hiệu ứng bo tròn và đổ bóng tầng lớp (Elevation) của M3.

## 4. Tính năng Đẳng cấp

### 4.1. Chỉnh sửa Sâu (Deep Editing)
- **In-place Editing:** Cho phép sửa văn bản trực tiếp trên trang PDF với Micro-editor tự động nhận diện Font/Size gốc.
- **Object Manipulation:** Kéo/thả, thay đổi kích thước, xoay hình ảnh và các đối tượng đồ họa bằng các tay nắm (Handles) trực quan.

### 4.2. Hệ thống AI (NotebookLM-like)
- **Sidebar Chat:** Giao diện chat AI dạng Card chuẩn M3.
- **Structured RAG:** AI có khả năng truy cập vào cấu trúc đối tượng của PDF để cung cấp ngữ cảnh chính xác nhất (hiểu bảng biểu, sơ đồ).
- **Actionable AI:** AI có thể thực hiện lệnh trực tiếp như highlight hoặc phân loại nội dung.

## 5. Kiến trúc Code & Mở rộng
- **Design Patterns:** Sử dụng Bridge (tách View/Core), Strategy (mở rộng công cụ), và Command (Undo/Redo vô hạn).
- **Plug-in System:** Cấu trúc module hóa cho phép thêm các tính năng như Ký số, OCR, Dịch thuật mà không ảnh hưởng đến lõi.
- **Dependency Injection:** Đảm bảo code hiện đại, dễ bảo trì và viết Unit Test.
