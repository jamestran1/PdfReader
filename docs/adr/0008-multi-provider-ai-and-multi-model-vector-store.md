---
status: accepted
---

# Nhiều AI provider qua SDK chính thức, vector store giữ nhiều model cùng lúc

Trí Thư hỗ trợ OpenAI, Google Gemini và Anthropic Claude cho Chat; OpenAI và Gemini cho Embedding (Anthropic không có API embedding). Người dùng chọn provider và tên model riêng cho Chat và cho Embedding trong Settings; mỗi provider một API key, mã hoá DPAPI (#92).

## Quyết định

- **SDK chính thức, adapter của chính SDK.** Dùng package `OpenAI`, `Google.GenAI` (Google) và `Anthropic` (Anthropic), không dùng gói cộng đồng như `Anthropic.SDK`. Cả ba đã phụ thuộc `Microsoft.Extensions.AI.Abstractions` và cung cấp `IChatClient` / `IEmbeddingGenerator` sẵn; ta dùng adapter đó. Chỉ viết adapter mỏng của riêng mình khi SDK thiếu hẳn một khả năng cần dùng, và khi đó phần ánh xạ message là hàm thuần có test.
- **Một điểm rẽ nhánh theo provider.** `IChatClientFactory` và `IEmbeddingGeneratorFactory` nhận `AiClientOptions(Provider, Model, ApiKey)` và `switch` theo `AiProvider`. Service gọi AI (`AiChatService`, `DocumentIndexingService`, `RagContextService`) đọc provider, model và key từ `ISettingsService`, không tự biết provider nào.
- **Phân loại lỗi tập trung.** `AiErrorClassifier` nhận exception gốc của từng SDK và trả `AiChatError`; adapter không bắt rồi ném lại exception chung.
- **Vector store đa model.** Một Document có thể có nhiều Vector Index, mỗi Embedding Model một index. Trạng thái index theo cặp (document, model) nằm trong bảng `document_models`; `documents` không còn cột `status` / `embedding_model`. Vector nằm trong bảng `vec0` theo số chiều (`vec_chunks_1536`, `vec_chunks_768`...), tạo khi cần, vì `vec0` cố định số chiều lúc tạo bảng. Truy vấn KNN lọc theo cả document lẫn model nên hai model cùng số chiều không lẫn nhau. Chunk văn bản dùng chung giữa các model; lập chỉ mục cho model mới không xoá vector của model cũ.
- **Nâng schema một lần.** Lên schema mới thì xoá index cũ và lập lại, không chuyển dữ liệu (`vec_chunks` một bảng 1536 chiều cũ bị bỏ).

## Consequences

- Đổi Embedding Model rồi đổi lại không tốn tiền lập chỉ mục lại; đổi lại, `index.db` lớn dần theo số model đã dùng.
- Thêm provider mới là thêm một nhánh `switch` và một nhánh trong `AiErrorClassifier`, không đụng ViewModel.
- Gemini embedding được gọi với số chiều đầu ra 768 để vừa bảng `vec_chunks_768` và giữ index nhỏ.
- PRD #92 gọi quyết định này là "ADR 0005" và đề xuất `Anthropic.SDK` với adapter tự viết; số 0005 đã dùng cho phát hành MSIX, và gói Anthropic chính thức đã có adapter `Microsoft.Extensions.AI`, nên ADR này thay thế hai điểm đó.
