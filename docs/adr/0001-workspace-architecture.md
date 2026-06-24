---
status: accepted
---

# Workspace: many-to-many documents, notes scoped to the workspace

Để hỗ trợ nghiên cứu đa tài liệu (hướng NotebookLM), ta đưa vào thực thể **Workspace** tham chiếu **nhiều Document** theo quan hệ **many-to-many** (một document có thể ở nhiều workspace), và **Note thuộc về Workspace** (`owner_key = workspaceId`, `document_id` chỉ là anchor) thay vì thuộc về một document. Mỗi document có một **default workspace tường minh** (bản ghi thật, `IsDefault`, chứa đúng doc đó, ẩn khỏi danh sách, không xóa được) để giữ hành vi "đọc một tài liệu lẻ" và để di trú notes cũ (`owner_key` từ documentId sang default-workspaceId).

## Considered options

- **One-to-many (mỗi document thuộc đúng một workspace, như thư mục):** đơn giản hơn nhưng buộc import lại nếu dùng một paper ở nhiều dự án; loại vì trái thực tế nghiên cứu.
- **Default workspace ngầm = documentId (documentId đóng hai vai owner_key):** không cần migrate nhưng nhập nhằng ngữ nghĩa; loại để giữ domain rõ (chọn default workspace tường minh + di trú một lần, idempotent).

## Consequences

- Cùng một document mở lẻ vs mở trong workspace W cho **hai tập notes khác nhau** (notes thuộc ngữ cảnh) — đúng chủ đích, nhưng cần hiểu khi đọc code.
- Trong workspace nhiều document, panel notes gộp mọi nguồn (kèm nhãn doc) và highlight trên trang phải **lọc theo `document_id`** của doc đang mở.
- Đây là nền cho #26 (RAG/chat đa tài liệu) và #27 (citations); các layer đó phải tôn trọng phạm vi theo Workspace. Trong layer #25 này, **chat vẫn theo từng document** (chưa đa tài liệu).
