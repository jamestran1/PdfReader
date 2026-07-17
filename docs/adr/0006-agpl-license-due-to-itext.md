---
status: accepted
---

# Toàn bộ app cấp phép AGPL-3.0 (hệ quả của iText 8)

iText 8 (extraction/search-rects/metadata — `ITextPdfDocumentService`, lõi search của app) là AGPL dual-license. Phân phối app tới người dùng (Store, #93) kích hoạt nghĩa vụ copyleft: **combined work phải mang license tương thích AGPL**, không có lựa chọn "chỉ kèm nguồn iText". Quyết định: tuân thủ — cấp phép toàn repo **AGPL-3.0**, vì app free và repo vốn đã public nên chi phí tuân thủ gần bằng 0.

Cụ thể hóa khi phân phối:

- `LICENSE` = AGPL-3.0 ở gốc repo (trước đó repo public nhưng KHÔNG có license — mặc định all-rights-reserved, tự nó đã không tương thích khi phân phối kèm iText).
- `THIRD-PARTY-NOTICES.md` liệt kê license mọi dependency phân phối kèm (iText AGPL, PDFium BSD-3, SkiaSharp/MDT/CommunityToolkit MIT, sqlite-vec MIT/Apache…).
- Store listing ghi rõ license và **link tới mã nguồn đúng bản phân phối** — cơ chế tag `vX.Y.Z` (ADR 0005) chính là "corresponding source" cho từng Release.

## Considered options

- **Thay iText bằng PDFium text APIs / PdfPig (Apache-2.0):** thoát AGPL vĩnh viễn, giữ quyền đóng source; loại cho release đầu — `ITextPdfDocumentService` là lõi search/extraction, migrate là việc lớn, không đáng chặn release. Có thể mở lại sau (các bản ĐÃ phát hành vẫn AGPL mãi; chỉ bản sau khi gỡ iText mới đổi license được).
- **Mua iText commercial:** giữ closed-source, nhưng giá theo năm hàng nghìn USD — vô lý cho app free cá nhân; loại.

## Consequences

- Mọi fork/derivative của Trí Thư buộc phải mở source theo AGPL.
- **Không thể** thương mại hóa closed-source chừng nào còn iText; muốn đổi hướng phải migrate iText trước và chỉ áp dụng cho các bản sau đó.
- Mỗi Release phải đảm bảo tag khớp chính xác binary phân phối (đã có sẵn nhờ versioning tag-driven — ADR 0005).
