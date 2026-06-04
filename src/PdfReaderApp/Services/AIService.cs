using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PdfReaderApp.Services;

public class AIService
{
    public async Task<string> AskQuestionAsync(string question, string context)
    {
        // Placeholder for LLM call
        await Task.Delay(1000); // Simulate API call
        
        return $"[AI Response based on context]: Bạn vừa hỏi về '{question}'. Trong tài liệu có nhắc đến nội dung này ở phần: {context.Substring(0, Math.Min(100, context.Length))}...";
    }
}