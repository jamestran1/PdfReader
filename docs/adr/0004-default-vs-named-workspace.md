---
status: accepted
---

# Default vs named Workspace: mô hình phiên, chat theo Workspace, surface "Tài liệu Workspace" thống nhất

Đối chiếu thiết kế Claude Design ("Trí Thư") với các story Tab (#47/#48/#49) làm rõ ba quyết định mô hình; tinh chỉnh và **thay thế một phần** ADR 0003.

- **Mô hình phiên: luôn-là-Workspace, hai chế độ qua `IsWorkspaceSession`.**
  - `false` = **Default Workspace** — đọc một Document lẻ: KHÔNG Tab Strip, KHÔNG nút "+". Mở Document từ Library → vào Default Workspace, **thay thế** doc hiện tại (giữ hành vi đọc lẻ). Default Workspace là tạm/ẩn, không liệt kê ở màn Workspaces; muốn đa tài liệu phải **tạo Workspace có tên**.
  - `true` = **named Workspace** — Open Set nhiều Tab + Tab Strip + "+". Tab chrome CHỈ xuất hiện ở chế độ này.
- **Chat theo Workspace** (thay thế phần "chat per-tab/per-Document" của ADR 0003). Một luồng chat cho mỗi Workspace, RAG xuyên mọi Document thành viên; Document active chỉ là neo trích dẫn mặc định. Lý do: nghiên cứu đa tài liệu cần hỏi xuyên nguồn, không phải mỗi tab một hội thoại rời rạc. Lịch sử bền + RAG thật vẫn để #26 (AIService còn placeholder).
- **Một surface "Tài liệu Workspace" thống nhất** thay cho cả Thumbnail Gallery lẫn màn chi tiết Workspace cũ (mà S2 đã bypass). Hai vùng: (1) Document thành viên — click mở/kích hoạt Tab + nút gỡ; (2) Thêm từ Library — thêm vào membership. **Add = thêm membership VÀ mở thành Tab active.** Cùng một component render ở 3 nơi: modal nút "+", canvas empty-state của named Workspace, và lưới re-open khi đóng Tab cuối.

## Considered options

- **Luôn có Tab Strip (kể cả đọc lẻ):** đồng nhất nhưng thêm chrome thừa cho ca đọc một tài liệu phổ biến; loại — đọc lẻ giữ tối giản.
- **Chat per-Document (như ADR 0003):** hợp khi mỗi tab là ngữ cảnh tách biệt, nhưng phá giá trị RAG đa nguồn của Workspace; loại.
- **Giữ Thumbnail Gallery (chỉ mở) và màn chi tiết (chỉ add/remove) riêng:** hai lưới tài liệu song song, trùng lặp; gộp thành một surface hai vùng.

## Consequences

- `IsWorkspaceSession=false` dùng viewer đọc-lẻ (single); `true` dùng viewer-per-tab (ADR 0003). **Không đổi kiến trúc viewer** — #48 chỉ thêm gate-nạp-theo-hiển-thị + LRU eviction (không chuyển sang một-viewer-swap).
- Promote Default → named Workspace (mang doc hiện tại sang làm Tab đầu) là một affordance riêng (issue follow-up).
- Bỏ màn chi tiết Workspace cũ (rename / docs list / add-remove); add/remove dời vào surface hai vùng (#47).
- Cập nhật `CONTEXT.md`: chat = per-Workspace (gỡ khỏi per-tab view-state); "Workspace Documents surface" thay "Thumbnail Gallery"; Default Workspace là tạm + không tab chrome.
- Phần đổi nghĩa: ADR 0003 mục "Chat per-tab" và term "Thumbnail Gallery" được ADR này thay thế.
