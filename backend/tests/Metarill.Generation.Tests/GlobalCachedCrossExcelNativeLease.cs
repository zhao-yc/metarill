using System.Text.Json;
using System.Text.RegularExpressions;
namespace Metarill.Generation.Tests;

/// <summary>六模型正式身份链使用独立单库租约，不能借用B119共享32表或其它原生用例授权。</summary>
internal static class GlobalCachedCrossExcelNativeLease
{
    internal const string EnvironmentKey="METARILL_GLOBAL_CROSS_EXCEL_NATIVE_SETTINGS";
    internal const string Purpose="global-cached-cross-excel-native-v1";
    // 修正后复验使用当前产品基线；旧准备与已结束租约不能用于新的源码。
    internal const string ProductCommit="6f9b3eb4b6e20ec71c2a6f3d06d69352fe06f552";
    /// <summary>本轮、用途、状态、六模型及固定准备目录均在复制文件、进程和数据库连接前核对。</summary>
    internal static string Validate(JsonElement settings,string preparedRoot)
    {
        if(Text(settings,"Purpose")!=Purpose||Text(settings,"AuthorizationStatus")!="approved"||Text(settings,"LeaseStatus")!="running"||Text(settings,"ProductCommit")!=ProductCommit)throw new InvalidOperationException("六模型正式身份验收尚无本轮独占新库授权。");
        var run=Text(settings,"RunId");if(!Regex.IsMatch(run,"^[a-f0-9]{16}$"))throw new InvalidOperationException("六模型运行标识不规范。");
        if(!settings.TryGetProperty("GlobalCrossExcelPreparation",out var proof)||proof.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("六模型固定准备包缺失。");
        var root=Text(proof,"PackageRoot");if(!Path.IsPathFullyQualified(root)||Path.GetFullPath(root)!=Path.GetFullPath(preparedRoot))throw new InvalidOperationException("六模型固定包根不符。");
        if(!settings.TryGetProperty("Profiles",out var profiles)||profiles.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("六模型列表缺失。");
        var actual=profiles.EnumerateArray().Select(value=>value.ValueKind==JsonValueKind.String?value.GetString()!:"").ToArray();
        var expected=GlobalCachedCrossExcelMySqlLease.Profiles.Select(p=>GlobalCachedCrossExcelMySqlLease.Profile(p.Role,p.Key,p.Parent,p.AllGlobal,p.Shared,p.Shorter)).ToArray();
        if(actual.Length!=6||actual.Distinct(StringComparer.Ordinal).Count()!=6||!actual.ToHashSet(StringComparer.Ordinal).SetEquals(expected))throw new InvalidOperationException("六模型范围不完整或混入其它模型。");
        return run;
    }
    /// <summary>坏类型和缺失不降级为默许。</summary>
    private static string Text(JsonElement value,string key)=>value.ValueKind==JsonValueKind.Object&&value.TryGetProperty(key,out var item)&&item.ValueKind==JsonValueKind.String?item.GetString()!:"";
}
