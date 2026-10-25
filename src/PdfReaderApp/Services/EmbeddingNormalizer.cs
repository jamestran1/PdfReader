namespace PdfReaderApp.Services;

/// <summary>Chuẩn hoá vector về độ dài L2 bằng 1. Thuần, không I/O.</summary>
public static class EmbeddingNormalizer
{
    /// <summary>Trả vector mới có độ dài 1; vector không (hoặc không hữu hạn) giữ nguyên để không sinh NaN.</summary>
    public static float[] Normalize(ReadOnlySpan<float> vector)
    {
        double sumOfSquares = 0;
        foreach (float value in vector)
            sumOfSquares += (double)value * value;

        float[] result = vector.ToArray();
        if (sumOfSquares <= 0 || double.IsInfinity(sumOfSquares) || double.IsNaN(sumOfSquares))
            return result;

        float norm = (float)Math.Sqrt(sumOfSquares);
        for (int i = 0; i < result.Length; i++)
            result[i] /= norm;

        return result;
    }
}
