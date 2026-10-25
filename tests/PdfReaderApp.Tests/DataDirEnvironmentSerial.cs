namespace PdfReaderApp.Tests;

/// <summary>
/// Nhóm test đổi biến môi trường TRITHU_DATA_DIR của cả tiến trình; phải chạy một mình, không song song với test khác.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DataDirEnvironmentSerial
{
    public const string Name = "TRITHU_DATA_DIR";
}
