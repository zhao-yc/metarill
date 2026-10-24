using System.Text;
using System.Text.Json;

namespace Metarill.Data.Schema;

public sealed record SchemaAuditEntry(SchemaChangePlan Plan, SchemaApplyResult Result);

/// <summary>宿主可把结构执行记录写入独立审计存储，不能依赖 MySQL DDL 事务回滚。</summary>
public interface ISchemaChangeJournal
{
    Task AppendAsync(SchemaAuditEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>默认文件实现写入 JSONL；路径由宿主配置，不接收客户端文件名。</summary>
public sealed class FileSchemaChangeJournal : ISchemaChangeJournal
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileSchemaChangeJournal(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("必须配置结构审计文件路径。", nameof(path));
        _path = Path.GetFullPath(path);
    }

    /// <summary>每条记录包含预览指令和实际执行数量；关闭流前刷新到磁盘。</summary>
    public async Task AppendAsync(SchemaAuditEntry entry, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + "\n");
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }
}
