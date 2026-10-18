namespace Metarill.Runtime.Exports;

/// <summary>导出文件始终位于私有目录，票据有效期与资源限额由服务端配置。</summary>
public sealed record ExportOptions
{
    public string RootDirectory { get; init; } = ".local/exports";
    public TimeSpan TimeToLive { get; init; } = TimeSpan.FromMinutes(15);
    public int MaxRows { get; init; } = 50_000;
    public int MaxColumns { get; init; } = 200;
    public int MaxCells { get; init; } = 500_000;
    public int MaxCellCharacters { get; init; } = 32_767;
    public long MaxTotalTextCharacters { get; init; } = 10_000_000;
    public long MaxFileBytes { get; init; } = 64L * 1024 * 1024;
    public int MaxActiveTickets { get; init; } = 1_000;
    public int MaxConcurrentExports { get; init; } = 2;
}

/// <summary>仅暴露已授权读取流和安全显示名，绝不返回本地存储路径。</summary>
public sealed record ExportContentHandle(Stream Stream, string FileName, string ContentType, long Length, DateTimeOffset ExpiresAt);
