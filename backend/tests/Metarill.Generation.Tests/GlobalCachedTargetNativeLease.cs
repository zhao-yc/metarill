using System.Text.Json;
using System.Text.RegularExpressions;
namespace Metarill.Generation.Tests;

/// <summary>全局缓存目标必须单独批准；旧缓存来源/其它四库租约不能触发本轮副作用。</summary>
internal static class GlobalCachedTargetNativeLease
{
    internal const string Purpose="global-cached-target-v1";
    /// <summary>用途、产品、单次状态、四种模型和固定包路径均在复制、进程和连接之前核对。</summary>
    internal static string Validate(JsonElement settings,string preparedRoot)
    {
        if(Text(settings,"Purpose")!=Purpose||Text(settings,"AuthorizationStatus")!="approved"||Text(settings,"LeaseStatus")!="running"||Text(settings,"ProductCommit")!=GlobalCachedTargetNativePreparation.ProductCommit)throw new InvalidOperationException("全局缓存目标尚无本轮专属运行租约。");
        var run=Text(settings,"RunId");if(!Regex.IsMatch(run,"^[a-f0-9]{16}$"))throw new InvalidOperationException("运行编号不规范。");
        if(!settings.TryGetProperty("CachedTargetPreparation",out var preparation)||preparation.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("缺少四模型固定证明。");
        var suffixes=new[]{"cross_ss","cross_sl","cross_ls","cross_ll"};if(!preparation.EnumerateObject().Select(property=>property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(suffixes))throw new InvalidOperationException("四模型证明不完整。");
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
