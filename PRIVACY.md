# Chính sách quyền riêng tư của Trí Thư

Cập nhật: 2026-10-25

Trí Thư là ứng dụng đọc và chú thích PDF chạy trên máy tính của bạn. Trí Thư **không có máy chủ riêng**, không yêu cầu tài khoản, không có quảng cáo, không gửi số liệu sử dụng (telemetry) hay báo cáo lỗi về cho nhà phát triển.

## Dữ liệu lưu trên máy của bạn

Thư viện tài liệu, Workspace, ghi chú, lịch sử chat, chỉ mục tìm kiếm và cài đặt được lưu trong thư mục dữ liệu của ứng dụng trên máy bạn. Các dữ liệu này không rời khỏi máy, trừ phần gửi tới OpenAI nêu dưới đây.

OpenAI API key bạn nhập được mã hoá bằng Windows DPAPI, gắn với tài khoản Windows của bạn, và chỉ được dùng để gọi OpenAI.

## Dữ liệu gửi tới OpenAI

Các tính năng AI (chat về tài liệu, tìm kiếm theo ngữ nghĩa) dùng dịch vụ OpenAI bằng **API key của chính bạn**. Trí Thư không cung cấp key và không đi qua máy chủ trung gian nào: ứng dụng gọi thẳng OpenAI từ máy bạn.

- **Khi chưa nhập API key:** không có dữ liệu nào được gửi đi. Đọc, chú thích, tìm kiếm văn bản vẫn dùng được.
- **Khi đã nhập API key:**
  - Lúc mở một tài liệu, văn bản của tài liệu được chia đoạn và gửi tới OpenAI để tạo embedding (lập chỉ mục cho tìm kiếm ngữ nghĩa và chat).
  - Lúc bạn chat, câu hỏi của bạn, các đoạn văn bản liên quan trong tài liệu và các lượt hội thoại trước đó về tài liệu này (kể cả lịch sử chat đã lưu từ lần mở trước) được gửi tới OpenAI để tạo câu trả lời.

Dữ liệu gửi tới OpenAI chịu [chính sách quyền riêng tư](https://openai.com/policies/privacy-policy) và [điều khoản API](https://openai.com/policies/business-terms) của OpenAI. Muốn không gửi tài liệu nào đi, hãy không nhập API key.

## Gỡ cài đặt

Gỡ Trí Thư khỏi Windows sẽ xoá thư mục dữ liệu của ứng dụng cùng toàn bộ dữ liệu nêu trên.

## Mã nguồn và liên hệ

Trí Thư là phần mềm mã nguồn mở theo giấy phép AGPL-3.0. Mã nguồn, và nơi đặt câu hỏi về quyền riêng tư: https://github.com/jamestran1/PdfReader (tạo issue).
