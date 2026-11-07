using System.Text.Json;
using System.Text.RegularExpressions;
namespace Metarill.Generation.Tests;

/// <summary>本轮用途及单次批准状态先于包复制、进程或数据库连接；普通测试配置无权启用。</summary>
internal static class ManagementNativeLease
{
    internal const string Purpose="full-management-native-v1";
    /// <summary>完整管理专属租约不能复用旧Agent/文件/时间测试用途或其他固定包根。</summary>
    internal static string Validate(JsonElement settings,string preparedRoot)
    {
        if(Text(settings,"Purpose")!=Purpose||Text(settings,"AuthorizationStatus")!="approved"||Text(settings,"LeaseStatus")!="running"||Text(settings,"ProductCommit")!=ManagementNativePreparation.ProductCommit)throw new InvalidOperationException("完整管理正式验收未取得本轮专属运行租约。");
        var run=Text(settings,"RunId");if(!Regex.IsMatch(run,"^[a-f0-9]{16}$"))throw new InvalidOperationException("完整管理运行编号不规范。");
        if(!settings.TryGetProperty("ManagementPreparation",out var proof))throw new InvalidOperationException("完整管理固定包证明缺失。");
        var path=Text(proof,"PackageRoot");if(!Path.IsPathFullyQualified(path)||Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))!=path||path!=Path.TrimEndingDirectorySeparator(Path.GetFullPath(preparedRoot)))throw new InvalidOperationException("完整管理固定包根不符。");
        return run;
    }
    /// <summary>空值、缺字段和错误类型都不解释成默认授权。</summary>
    private static string Text(JsonElement value,string key)=>value.ValueKind==JsonValueKind.Object&&value.TryGetProperty(key,out var field)&&field.ValueKind==JsonValueKind.String?field.GetString()!:"";
}
