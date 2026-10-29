using System.Text.Json;
using System.Text.RegularExpressions;
namespace Metarill.Generation.Tests;

/// <summary>纯配置门禁先于包复制、连接和子进程；内存合成批准形状不代表真实授权。</summary>
internal static class TimeValueNativeLease
{
    internal const string Purpose="time-values-v1";
    /// <summary>本轮专属用途、产品、单次状态和固定包路径全部满足才返回规范运行标识。</summary>
    internal static string Validate(JsonElement settings,string preparedRoot)
    {
        if(Text(settings,"Purpose")!=Purpose||Text(settings,"AuthorizationStatus")!="approved"||Text(settings,"LeaseStatus")!="running"||Text(settings,"ProductCommit")!=TimeValueNativePreparation.ProductCommit)
            throw new InvalidOperationException("TIME字段验收未取得本轮专属运行租约。");
        var run=Text(settings,"RunId");if(!Regex.IsMatch(run,"^[a-f0-9]{16}$"))throw new InvalidOperationException("TIME字段运行标识不规范。");
        if(!settings.TryGetProperty("TimeValuePreparation",out var preparation))throw new InvalidOperationException("TIME字段完整包证明缺失。");
        var path=Text(preparation,"PackageRoot");if(!Path.IsPathFullyQualified(path)||Path.GetFullPath(path)!=Path.GetFullPath(preparedRoot))throw new InvalidOperationException("TIME字段固定包根不符。");
        return run;
    }
    /// <summary>缺字段/坏类型统一拒绝，不把异常配置解释成默认授权。</summary>
    private static string Text(JsonElement value,string key)=>value.ValueKind==JsonValueKind.Object&&value.TryGetProperty(key,out var text)&&text.ValueKind==JsonValueKind.String?text.GetString()!:"";
}
