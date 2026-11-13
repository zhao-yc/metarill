using System.Text.Json;
using System.Text.RegularExpressions;
namespace Metarill.Generation.Tests;

/// <summary>原三按钮正式用例只消费本轮专属批准，连接配置/复制/子进程前先核对精确来源。</summary>
internal static class SampleActionsNativeLease
{
    internal const string Purpose="sample-actions-native-v1";
    /// <summary>旧通用租约、待批准、结束、错产品/运行号及包根均拒绝，不能默认为已授权。</summary>
    internal static string Validate(JsonElement settings,string? requestedRun,string sourceRoot)
    {
        if(Text(settings,"Purpose")!=Purpose||Text(settings,"AuthorizationStatus")!="approved"||Text(settings,"LeaseStatus")!="running"||Text(settings,"SampleActionsProductCommit")!=SampleActionsNativePreparation.ProductCommit)
            throw new InvalidOperationException("原三按钮尚无本轮专属运行租约，未读取连接配置。");
        var run=Text(settings,"RunId");if(!Regex.IsMatch(run,"^[a-f0-9]{16}$")||requestedRun!=run)throw new InvalidOperationException("原三按钮独立运行号不符，未读取连接配置。");
        var declared=Text(settings,"SampleActionsSourceRoot");if(!Path.IsPathFullyQualified(declared)||Path.GetFullPath(declared)!=declared||!Path.IsPathFullyQualified(sourceRoot)||Path.GetFullPath(sourceRoot)!=sourceRoot||declared!=sourceRoot)
            throw new InvalidOperationException("原三按钮固定来源根不符。");
        if(!settings.TryGetProperty("SampleActionsPreparation",out var proof)||proof.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("缺少原三按钮固定包证明。");
        var package=Text(proof,"PackageRoot");if(!Path.IsPathFullyQualified(package)||Path.GetFullPath(package)!=package||package!=Path.GetFullPath(Path.Combine(sourceRoot,".local/sample-actions-native/prepared")))
            throw new InvalidOperationException("原三按钮固定包根不符。");
        return run;
    }
    /// <summary>只有明确字符串被接受；坏类型、缺字段和空值均不产生授权。</summary>
    private static string Text(JsonElement root,string name)=>root.ValueKind==JsonValueKind.Object&&root.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()!:"";
}
