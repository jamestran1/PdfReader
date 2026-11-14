---
status: accepted
---

# Markdown dùng Markdig và renderer WPF tự viết

Chat AI và nội dung Note cần hiển thị Markdown gần chuẩn CommonMark + GFM, trong khi app chỉ có parser Markdown tự viết cho một tập nhỏ của Bản soạn. Ta dùng Markdig để parse Markdown, rồi tự viết renderer WPF từ syntax tree thay vì dùng WebView2 hoặc HTML engine.

## Quyết định

- **Parse chuẩn, render có kiểm soát.** Markdig xử lý CommonMark + GFM (heading, emphasis, list, table, strikethrough, task list, blockquote, code và autolink); renderer WPF tự quyết định bố cục theo Material Design của app.
- **Không render nội dung không tin cậy.** Raw HTML, image và script bị loại bỏ hoặc escape; link chỉ mở bên ngoài qua `http` / `https`.
- **Renderer dùng chung cho tin AI trong Chat và nội dung Note.** Đoạn văn bản PDF trong `Quote` vẫn hiển thị nguyên văn, không interpret Markdown.
- **Citation RAG `[n]` thắng cú pháp Markdown** khi khớp nguồn trong bảng trích dẫn, để link nhảy tới tài liệu không bị Markdown nuốt mất.
- **Giữ Markdown raw ở dữ liệu.** DB, clipboard và chế độ Sửa dùng Markdown raw. Chỉ chế độ đọc mới render.

## Consequences

- Thay thế dần `MarkdownBlockParser` tự viết; không bảo trì hai parser cho cùng một ngôn ngữ hiển thị.
- Streaming render lại tin AI sau mỗi chunk. Block chưa hoàn chỉnh có thể hiển thị gần raw cho tới khi cú pháp đủ điều kiện parse.
- Test phải phủ các cấu trúc CommonMark + GFM chính, loại bỏ HTML/image, precedence của citation `[n]`, và hành vi raw Markdown khi lưu, sửa, copy.
