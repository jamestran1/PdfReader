namespace PdfReaderApp.Core;

/// <summary>Trạng thái viewer tại lúc ScrollChanged, đủ để quyết định có ghi <c>ScrollNorm</c> hay không.</summary>
public sealed record ScrollPersistContext(bool IsVisible, double ViewportHeight, bool IsSettlePending, bool IsContinuous);
