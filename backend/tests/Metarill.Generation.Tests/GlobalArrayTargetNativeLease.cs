using System.Text.Json;
using System.Text.RegularExpressions;
namespace Metarill.Generation.Tests;

/// <summary>全局数组目标必须单独批准；旧缓存来源/其它四库租约不能触发本轮副作用。</summary>
internal static class GlobalArrayTargetNativeLease
{
    internal const string Purpose="global-array-targets-v1";
    /// <summary>用途、产品、单次状态、八种模型和固定包路径均在复制、进程和连接之前核对。</summary>
    internal static string Validate(JsonElement settings,string preparedRoot)
    {
        if(settings.ValueKind!=JsonValueKind.Object||settings.EnumerateObject().Select(property=>property.Name).Distinct(StringComparer.Ordinal).Count()!=settings.EnumerateObject().Count())throw new InvalidOperationException("全局数组租约含重复或无效属性。");
        if(Text(settings,"Purpose")!=Purpose||Text(settings,"AuthorizationStatus")!="approved"||Text(settings,"LeaseStatus")!="running"||Text(settings,"ProductCommit")!=GlobalArrayTargetNativePreparation.ProductCommit)throw new InvalidOperationException("全局数组目标尚无本轮专属运行租约。");
        var run=Text(settings,"RunId");if(!Regex.IsMatch(run,"^[a-f0-9]{16}$"))throw new InvalidOperationException("运行编号不规范。");
        if(!settings.TryGetProperty("ArrayTargetPreparation",out var preparation)||preparation.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("缺少八模型固定证明。");
        var suffixes=GlobalArrayTargetNativeModel.All.Select(model=>model.Suffix).ToArray();if(preparation.EnumerateObject().Count()!=8||!preparation.EnumerateObject().Select(property=>property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(suffixes))throw new InvalidOperationException("八模型证明不完整。");
        foreach(var suffix in suffixes)
        {
            var path=Text(preparation.GetProperty(suffix),"PackageRoot");
            if(!Path.IsPathFullyQualified(path)||Path.GetFullPath(path)!=Path.GetFullPath(Path.Combine(preparedRoot,suffix)))throw new InvalidOperationException("固定模型包根不符。");
        }
        return run;
    }
    /// <summary>坏类型和缺字段均拒绝，不能默认为批准。</summary>
    private static string Text(JsonElement value,string name)=>value.ValueKind==JsonValueKind.Object&&value.TryGetProperty(name,out var text)&&text.ValueKind==JsonValueKind.String?text.GetString()!:"";
}
