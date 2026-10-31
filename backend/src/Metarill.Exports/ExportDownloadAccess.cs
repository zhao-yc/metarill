using Microsoft.AspNetCore.Http;

namespace Metarill.Runtime.Exports;

/// <summary>原下载入口的窄例外；任意显式认证头均不得退回无头凭证授权。</summary>
public static class ExportDownloadAccess
{
    public const string DownloadPath = "/api/admin/sys/export/export";
    /// <summary>只接受原精确GET/HEAD、唯一随机票据参数，不能携带动态账号、项目、路径或SQL。</summary>
    public static bool CanUseGrant(HttpRequest request)
    {
        if (!(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) || request.Path.Value != DownloadPath
            || request.Headers.ContainsKey("loginStateId") || request.Headers.ContainsKey("Authorization")
            || request.Query.Count != 1 || !request.Query.TryGetValue("exportId", out var values) || values.Count != 1) return false;
        var value = values[0];
        return value is { Length: 64 } && value.All(character => character is >= 'a' and <= 'f' or >= '0' and <= '9');
    }
}
