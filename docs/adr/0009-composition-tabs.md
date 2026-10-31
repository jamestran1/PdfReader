---
status: accepted
---

# Tab có hai loại: Document và Bản soạn, kể cả ở Default Workspace

Bản soạn (#28) mở thành tab ở vùng giữa, cạnh tab Document (#252), nên mô hình tab của ADR 0003 không còn chỉ biết Document. Ta mở rộng nó thay vì dựng một mô hình tab thứ hai (#284).

## Quyết định

- **Một tập tab, hai loại.** `OpenTab` thành lớp cơ sở với `DocumentTab` và `CompositionTab`; dùng chung `TabSetViewModel`, một tab active, một Tab Strip, hai loại xếp xen kẽ và kéo sắp lại được. Tab Bản soạn không giữ handle PDFium nên đứng ngoài hydrate/evict MRU.
- **Default Workspace được mở tab Bản soạn.** Bản soạn thuộc cả Default Workspace (#248). Ở đó vẫn chỉ có đúng một tab Document (mở doc khác thì thay thế, như ADR 0004), nhưng mở thêm được tab Bản soạn; Tab Strip hiện khi có từ hai tab, không có nút "+".
- **Tab Bản soạn không đổi Phiên đọc.** Phiên đọc giữ Document gần nhất: neo trích dẫn của Chat, panel Notes và bộ lọc "chỉ tài liệu này" vẫn bám Document đó. Search của viewer tắt khi đang ở tab Bản soạn. Chat không đọc nội dung Bản soạn.
- **Khôi phục cả tab Bản soạn.** Bảng `open_tab` thêm cột loại tab và đổi `document_id` thành id chung theo loại; khoá chính thành `(workspace_id, tab_kind, item_id)`. Tab Bản soạn nhớ thứ tự, tab active và vị trí cuộn; chế độ Sửa không nhớ.
- **Sửa tự lưu.** Chế độ Sửa lưu sau một quãng ngắn không gõ và khi rời tab, nên đóng tab, đổi Workspace hay thoát app không mất gì và không có hộp thoại "Lưu thay đổi?". Không lặp lại hạn chế "âm thầm bỏ chỉnh sửa" của ADR 0003.

## Considered options

- **Mở Bản soạn thay chỗ viewer ở Default Workspace** (như mở một Document lẻ): đơn giản nhưng mất việc đối chiếu song song với Document; loại.
- **Chỉ cho mở Bản soạn ở Workspace có tên:** đi ngược việc Bản soạn thuộc cả Default Workspace (#248); loại.
- **Tập tab riêng cho Bản soạn:** hai dải tab, hai tab active, khó trả lời "đang xem gì"; loại.

## Consequences

- Xoá Bản soạn đang mở (từ thanh công cụ hay rail, sau khi xác nhận) thì đóng tab và chuyển sang tab MRU, hết tab thì như đóng tab cuối ở ADR 0003.
- Sinh lại mở Bản soạn mới thành tab active ngay bên phải tab cũ; bấm `[n]` là Cross-doc jump (activate-or-open tab Document của Note).
- Nâng schema `open_tab`: dòng cũ là tab Document.
