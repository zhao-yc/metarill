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
        var run=Text(settings,"RunId");if(!Regex.IsMatch(run,@"\A[a-f0-9]{16}\z"))throw new InvalidOperationException("TIME字段运行标识不规范。");
        if(!settings.TryGetProperty("TimeValuePreparation",out var preparation))throw new InvalidOperationException("TIME字段完整包证明缺失。");
        var path=Text(preparation,"PackageRoot");if(!Path.IsPathFullyQualified(path)||Path.GetFullPath(path)!=Path.GetFullPath(preparedRoot))throw new InvalidOperationException("TIME字段固定包根不符。");
        _=ReadInstancePin(settings.TryGetProperty("InstancePin",out var pin)?pin:default);
        return run;
    }
    /// <summary>原TIME实例字段只接受完整规范形状，在包复制和连接之前拒绝误用其它执行器字段。</summary>
    internal static TimeValueInstancePin ReadInstancePin(JsonElement pin)
    {
        const string message="TIME实例标识字段必须使用完整规范的expectedHostname与expectedServerUuid。";
        if(pin.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException(message);
        var names=pin.EnumerateObject().Select(property=>property.Name).ToArray();
        if(names.Length!=2||names.Distinct(StringComparer.Ordinal).Count()!=2||names.Any(name=>name is not ("expectedHostname" or "expectedServerUuid")))throw new InvalidOperationException(message);
        var host=Text(pin,"expectedHostname");var uuid=Text(pin,"expectedServerUuid");
        if(host.Length is <1 or >253||host.Any(value=>!char.IsAsciiLetterOrDigit(value)&&value is not ('.' or '-' or '_'))||!Guid.TryParseExact(uuid,"D",out var parsed)||uuid!=parsed.ToString("D"))throw new InvalidOperationException(message);
        return new(host,uuid);
    }
    /// <summary>缺字段/坏类型统一拒绝，不把异常配置解释成默认授权。</summary>
    private static string Text(JsonElement value,string key)=>value.ValueKind==JsonValueKind.Object&&value.TryGetProperty(key,out var text)&&text.ValueKind==JsonValueKind.String?text.GetString()!:"";
}

/// <summary>仅保存已经校验的实例身份，不持有数据库连接或认证秘密。</summary>
internal sealed record TimeValueInstancePin(string ExpectedHostname,string ExpectedServerUuid);
