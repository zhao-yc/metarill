using System.Text.Json;
using System.Text.RegularExpressions;
namespace Metarill.Generation.Tests;

/// <summary>纯配置门禁不连接数据库；用途／固定产品／本轮批准／包根缺一即拒绝。</summary>
internal static class CrossTableExcelNativeLease
{
    internal const string Purpose="cross-table-excel-v1";
    /// <summary>配置中的批准形状仍须由外部租约执行器与真实用户授权核对，不自动创建测试对象。</summary>
    internal static string Validate(JsonElement settings,string preparedRoot)
    {
        if(Text(settings,"Purpose")!=Purpose||Text(settings,"AuthorizationStatus")!="approved"||Text(settings,"LeaseStatus")!="running"||Text(settings,"ProductCommit")!=CrossTableExcelNativePreparation.ProductCommit)throw new InvalidOperationException("跨表Excel未取得本轮专属租约。");
        var run=Text(settings,"RunId");if(!Regex.IsMatch(run,"^[a-f0-9]{16}$"))throw new InvalidOperationException("跨表Excel运行标识不规范。");
        if(!settings.TryGetProperty("CrossTableExcelPreparation",out var preparation))throw new InvalidOperationException("跨表Excel准备包证明缺失。");
        var root=Text(preparation,"PackageRoot");if(!Path.IsPathFullyQualified(root)||Path.GetFullPath(root)!=Path.GetFullPath(preparedRoot))throw new InvalidOperationException("跨表Excel固定包根不符。");
        return run;
    }
    /// <summary>坏类型或缺字段不降级为默认许可。</summary>
    private static string Text(JsonElement value,string key)=>value.ValueKind==JsonValueKind.Object&&value.TryGetProperty(key,out var item)&&item.ValueKind==JsonValueKind.String?item.GetString()!:"";
}
