namespace PdfReaderApp.Models;

/// <summary>Đầu vào dựng Ngữ cảnh trả lời cho một câu hỏi.</summary>
/// <param name="Question">Câu hỏi của người dùng.</param>
/// <param name="Scope">Phạm vi hỏi đáp.</param>
/// <param name="Snapshot">Ảnh chụp tài liệu đang mở.</param>
/// <param name="LoadWorkspaceDocuments">Đọc Document của Workspace đang mở; chỉ được gọi ở phạm vi Toàn Workspace, trong đường dựng RAG nên lỗi đọc lùi về trang đang đọc.</param>
public sealed record AnswerContextRequest(
    string Question,
    DocumentScope Scope,
    DocumentSnapshot Snapshot,
    Func<IReadOnlyList<WorkspaceDocument>> LoadWorkspaceDocuments);
