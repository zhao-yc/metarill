using Metarill.Core.Security;
namespace Metarill.Core.Exports;

public sealed record PageExportRequest(string PageId,string Title,IReadOnlyList<string> Headers,IReadOnlyList<IReadOnlyList<object?>> Rows);
/// <summary>生成层负责读取范围与列投影，基础设施负责文件和有身份约束的下载票据。</summary>
public interface IPageExportService
{
    Task<string> CreateAsync(PageExportRequest request,RuntimeActor actor,CancellationToken cancellationToken=default);
    /// <summary>先取得资源额度再准备数据；尚未实现门禁的宿主默认拒绝，不执行查询委托。</summary>
    Task<string> CreateAsync(RuntimeActor actor,Func<int,CancellationToken,Task<PageExportRequest>> prepare,CancellationToken cancellationToken=default)
        => throw new NotSupportedException("导出宿主尚未实现读取前资源门禁。");
}
