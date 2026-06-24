---
status: accepted
---

# Một SQLite database cho mỗi feature, không FK xuyên-db

Mỗi feature lưu trạng thái trong một file SQLite riêng (`library.db`, `chats.db`, `index.db`, `notes.db`, và nay `workspaces.db`), mỗi cái một `SqliteXStore` dùng connection per-operation `Pooling=False` + lock, có `user_version` migration riêng. **Không có ràng buộc khóa ngoại xuyên các db**; tính toàn vẹn giữa store (vd xóa document kéo theo dọn notes/membership) được **phối hợp trong code**, không phải bằng FK.

## Consequences

- Ranh giới sở hữu rõ, test cô lập từng store (file tạm), migrate độc lập — nhưng không JOIN được giữa các db và phải nhớ dọn dữ liệu liên-store bằng tay ở tầng ViewModel/service.
- Quyết định này được giữ khi thêm Workspace (chọn `workspaces.db` riêng thay vì gộp vào `library.db`): nhất quán pattern, đổi lại mất FK/JOIN cho cặp workspace↔document (chấp nhận vì data nhỏ + đã quen dọn-bằng-code).
