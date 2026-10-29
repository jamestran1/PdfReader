namespace PdfReaderApp.Tests.Fakes;

/// <summary>Nhật ký lời gọi dùng chung cho các fake của Phiên đọc, để test kiểm thứ tự giữa các bên.</summary>
internal sealed class CallLog
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries => _entries;

    public void Add(string entry) => _entries.Add(entry);

    public int IndexOf(string entry) => _entries.IndexOf(entry);

    public bool Has(string entry) => _entries.Contains(entry, StringComparer.Ordinal);

    public bool HasLoad() => _entries.Any(e => e.StartsWith("load:", StringComparison.Ordinal));

    public void Clear() => _entries.Clear();
}
