# Thiết kế Ứng dụng đọc file PDF tích hợp AI (PDF Reader with AI)

## Tổng quan
Ứng dụng đọc, chỉnh sửa và chú thích file PDF dành cho nền tảng Desktop (Windows), tích hợp tính năng AI "take note" và hỏi đáp tương tự Google NotebookLM.

## Kiến trúc & Các thành phần (Architecture & Components)
1. **Framework (Giao diện):** Sử dụng .NET (WPF/WinUI 3) kết hợp mô hình MVVM (Model-View-ViewModel) để đảm bảo hiệu năng cao và giao diện mượt mà trên Windows.
2. **Các thành phần chính:**
   - **UI Layer (Giao diện):** Vùng hiển thị PDF chính (Document Viewer), thanh công cụ (Toolbar) cho chú thích/chỉnh sửa, và thanh bên (Sidebar) dành cho tính năng AI Chat.
   - **PDF Engine:** Sử dụng thư viện `PdfiumViewer` (hoặc tương đương) để render trang PDF và `PdfSharp` / `iText7` để xử lý các tính năng nâng cao (chú thích, cắt/ghép, chỉnh sửa text).
   - **AI & RAG Engine:** Hệ thống nhúng (Embeddings) và CSDL Vector cục bộ để lưu trữ ngữ cảnh file PDF. Tương tác với LLM thông qua API (OpenAI/Gemini/Semantic Kernel).

## Luồng dữ liệu (Data Flow)
1. **Thao tác PDF Cơ bản:**
   - Đọc file: `Pdfium` xử lý stream và render bitmap lên UI.
   - Chú thích: Bắt tọa độ trên UI và dùng thư viện PDF để ghi đối tượng Annotation chuẩn vào file PDF (tương thích ngược với Acrobat/Foxit).
2. **Tính năng AI (NotebookLM-like):**
   - **Xử lý ngầm:** Trích xuất text, chia nhỏ thành các đoạn (chunks) ngay khi mở file bằng một Background Worker.
   - **Lưu trữ Vector:** Mã hóa (Embed) các đoạn text và lưu vào Vector Database cục bộ (ví dụ SQLite tích hợp vector).
   - **Truy vấn (RAG):** Tìm kiếm vector ngữ cảnh liên quan nhất dựa trên truy vấn của người dùng, đưa vào Prompt và gọi LLM. Trả kết quả theo dạng stream về giao diện.

## Xử lý lỗi (Error Handling)
- **Graceful Degradation:** Đảm bảo tính năng xem và chỉnh sửa PDF luôn hoạt động mượt mà, ngay cả khi không có kết nối internet hoặc API AI gặp lỗi.
- Quá trình tính toán AI được cô lập hoàn toàn trên luồng nền để không gây đứng màn hình (freeze UI).
