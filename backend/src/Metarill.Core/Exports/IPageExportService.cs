using Metarill.Core.Security;
namespace Metarill.Core.Exports;

public sealed record PageExportRequest(string PageId,string Title,IReadOnlyList<string> Headers,IReadOnlyList<IReadOnlyList<object?>> Rows);
/// <summary>生成层负责读取范围与列投影，基础设施负责文件和有身份约束的下载票据。</summary>
public interface IPageExportService
{
    Task<string> CreateAsync(PageExportRequest request,RuntimeActor actor,CancellationToken cancellationToken=default);
}
