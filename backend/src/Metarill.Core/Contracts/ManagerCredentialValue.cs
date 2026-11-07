using System.Text.Json;

namespace Metarill.Core.Contracts;

/// <summary>固定原账号页的凭据与启用值协议，不向其他表或认证入口注册全局转换。</summary>
public static class ManagerCredentialValue
{
    public const string PasswordMask = "********";
    /// <summary>只有真实新口令可进入摘要钩子；掩码、空值和预编码格式不能成为凭据。</summary>
    public static string Input(string? value)
    {
        if (value is null || value.Length is < 12 or > 50 || value == PasswordMask ||
            value.StartsWith("pbkdf2-", StringComparison.OrdinalIgnoreCase) || value.StartsWith("$argon2", StringComparison.Ordinal) ||
            value.StartsWith("$2", StringComparison.Ordinal) || value.StartsWith("scrypt$", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("md5$", StringComparison.OrdinalIgnoreCase) || value.StartsWith("sha256$", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("密码必须为12至50个字符的真实新密码，不能提交回显掩码或预编码摘要。");
        return value;
    }
    /// <summary>可选partial缺失时明确拒写，不能把原文随BackendOnly生成服务直接保存。</summary>
    public static void RequireDigest(string? value)
    {
        try
        {
            var parts = value?.Split('$');
            if (parts is { Length: 4 } && parts[0] == "pbkdf2-sha256" && parts[1] == "350000" &&
                Convert.FromBase64String(parts[2]).Length == 24 && Convert.FromBase64String(parts[3]).Length == 32) return;
        }
        catch (FormatException) { }
        throw new InvalidOperationException("账号密码摘要钩子未完成，已拒绝保存凭据。");
    }
    /// <summary>额外账号页凭据声明必须有独立证明，不能通过复用物理Entity暴露摘要。</summary>
    public static void RequirePublishedPage(bool supported) { if (!supported) throw new InvalidOperationException("账号凭据查询或页面用途尚未证明，当前请求已拒绝。"); }
    /// <summary>旧页面“启用”1对应数据库disabled0；null仍为显式清空，不推断为0或1。</summary>
    public static int? ToDisabled(int? enabled) => enabled switch { null => null, 0 => 1, 1 => 0, _ => throw new ArgumentException("启用状态只接受0、1或null。") };
    /// <summary>物理存储与认证不变，只在固定原账号页响应还原“启用”的数值。</summary>
    public static int? ToEnabled(int? disabled) => disabled switch { null => null, 0 => 1, 1 => 0, _ => throw new InvalidOperationException("账号启用状态不是已支持的0、1或null。") };
    /// <summary>筛选只接收精确0/1词元，不经double或宽松整数转换。</summary>
    public static int? Filter(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
        return text switch { "0" => 1, "1" => 0, _ => throw new ArgumentException("启用筛选只接受精确0或1。") };
    }
    /// <summary>只由固定原页面调用：旧更新掩码转省略，废弃凭据字段仅接受空回显。</summary>
    public static JsonElement NormalizeLegacy(JsonElement entity, LegacyPageWriteOperation operation)
    {
        if (entity.ValueKind != JsonValueKind.Object) throw new ArgumentException("写入请求必须为实体对象。");
        var seen = new HashSet<string>(StringComparer.Ordinal);var result = new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach (var field in entity.EnumerateObject())
        {
            if (!seen.Add(field.Name)) throw new ArgumentException("请求字段重复。");
            if (field.Name is "salt" or "oldPassword")
            {
                if (field.Value.ValueKind == JsonValueKind.Null || field.Value.ValueKind == JsonValueKind.String && field.Value.GetString() == "") continue;
                throw new ArgumentException("密钥和旧密码仅接受空回显，不能由客户端设置。");
            }
            if (field.Name == "password" && operation == LegacyPageWriteOperation.Update && field.Value.ValueKind == JsonValueKind.String && field.Value.GetString() == PasswordMask) continue;
            result.Add(field.Name,field.Value.Clone());
        }
        return JsonSerializer.SerializeToElement(result);
    }
}
