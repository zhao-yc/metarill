namespace Metarill.Runtime.Files;

/// <summary>公开基地址只接受可信宿主配置，地址校验错误不回显配置内容。</summary>
internal static class LegacyUploadAddress
{
    /// <summary>只接受HTTP(S)绝对地址，禁止认证、query、fragment和控制字符。</summary>
    internal static string? NormalizeBase(string? value)
    {
        if (value is null or "") return null;
        if (value != value.Trim() || value.Any(char.IsControl) || value.Contains('\\') || value.Contains('?') || value.Contains('#')
            || !Uri.IsWellFormedUriString(value, UriKind.Absolute) || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("LegacyPublicBaseUrl必须为不含认证、query和fragment的HTTP(S)公开基地址。");
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    /// <summary>固定追加API公开文件路由，不能用Uri相对解析意外丢弃配置的路径前缀。</summary>
    internal static string Build(string? normalizedBase, string route, string? pathBase)
    {
        if (!route.StartsWith("/admin/sys/file-library/public-content/", StringComparison.Ordinal)) throw new InvalidOperationException("旧上传只能返回服务端公开文件路由。");
        return (normalizedBase ?? pathBase ?? "") + "/api" + route;
    }
}
