using System.Text.Json;
using System.Text.RegularExpressions;
namespace Metarill.Generation.Tests;

/// <summary>公开Full必须取得自己的精确新库租约，不能借用B121内部包或A30数值授权。</summary>
internal static class PublicR0CrossExcelNativeLease
{
    internal const string EnvironmentKey="METARILL_PUBLIC_R0_CROSS_EXCEL_NATIVE_SETTINGS";
    internal const string Purpose="public-r0-cross-excel-native-v1";
    internal const string RedundantEnvironmentKey="METARILL_PUBLIC_R1_CROSS_EXCEL_NATIVE_SETTINGS";
    internal const string RedundantPurpose="public-r1-cross-excel-native-v1";
    internal const string OverwriteEnvironmentKey="METARILL_PUBLIC_R0_CROSS_OVERWRITE_NATIVE_SETTINGS";
    internal const string OverwritePurpose="public-r0-cross-overwrite-v1";
    internal const int OverwriteMaximumTables=44;
    internal const int MaximumBusinessRows=512,MaximumTables=43;

    /// <summary>用途、来源、固定包、六模型及32业务表预算必须在文件复制／宿主／连接之前校验。</summary>
    internal static string Validate(JsonElement settings,string preparedRoot,bool overwrite=false,bool redundant=false)
    {
        var purpose=redundant?RedundantPurpose:overwrite?OverwritePurpose:Purpose;
        var product=redundant?PublicR0CrossExcelNativePreparation.RedundantBaseCommit:overwrite?PublicR0CrossExcelNativePreparation.OverwriteProductCommit:PublicR0CrossExcelNativePreparation.ProductCommit;
        if(Text(settings,"Purpose")!=purpose||Text(settings,"AuthorizationStatus")!="approved"||Text(settings,"LeaseStatus")!="running"||Text(settings,"ProductCommit")!=product)
            throw new InvalidOperationException("公开R0六模型尚无本轮独占新库授权。");
        var run=Text(settings,"RunId");if(!Regex.IsMatch(run,"\\A[a-f0-9]{16}\\z"))throw new InvalidOperationException("公开R0运行标识不规范。");
        if(!settings.TryGetProperty("PublicCrossExcelPreparation",out var proof)||proof.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("公开R0固定完整包缺失。");
        var root=Text(proof,"PackageRoot");if(!Path.IsPathFullyQualified(root)||Path.GetFullPath(root)!=Path.GetFullPath(preparedRoot))throw new InvalidOperationException("公开R0固定包根不符。");
        var expected=GlobalCachedCrossExcelMySqlLease.Profiles.Select(p=>GlobalCachedCrossExcelMySqlLease.Profile(p.Role,p.Key,p.Parent,p.AllGlobal,p.Shared,p.Shorter)).ToArray();
        RequireNames(settings,"Profiles",expected);RequireNames(settings,"BusinessTables",PublicR0CrossExcelNativePreparation.Catalog(overwrite,redundant).ConfigTables.Select(table=>table.TableName).ToArray());
        RequireBudget(settings,"MaximumBusinessRows",MaximumBusinessRows);RequireBudget(settings,"MaximumTables",redundant||overwrite?OverwriteMaximumTables:MaximumTables);return run;
    }
    /// <summary>集合必须完整、唯一、无额外项，不接受排序第一项或空值回退。</summary>
    private static void RequireNames(JsonElement settings,string key,IReadOnlyList<string> expected)
    {
        if(!settings.TryGetProperty(key,out var values)||values.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("公开R0范围集合缺失："+key);
        var actual=values.EnumerateArray().Select(value=>value.ValueKind==JsonValueKind.String?value.GetString()!:"").ToArray();
        if(actual.Length!=expected.Count||actual.Distinct(StringComparer.Ordinal).Count()!=expected.Count||!actual.ToHashSet(StringComparer.Ordinal).SetEquals(expected))throw new InvalidOperationException("公开R0范围集合不符："+key);
    }
    /// <summary>预算只接受固定整数，字符串或其它数值不能扩大本轮对象范围。</summary>
    private static void RequireBudget(JsonElement settings,string key,int expected)
    {
        if(!settings.TryGetProperty(key,out var value)||value.ValueKind!=JsonValueKind.Number||!value.TryGetInt32(out var number)||number!=expected)throw new InvalidOperationException("公开R0样本预算不符："+key);
    }
    /// <summary>缺失和错误JSON类型均拒绝，不读取连接信息。</summary>
    private static string Text(JsonElement value,string key)=>value.ValueKind==JsonValueKind.Object&&value.TryGetProperty(key,out var item)&&item.ValueKind==JsonValueKind.String?item.GetString()!:"";
}
