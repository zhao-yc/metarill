using System.Globalization;
using System.Text.Json;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

/// <summary>真实账号所属后台与开发工作区分开；后台、项目和角色均来自当前未删除记录。</summary>
public sealed record PrincipalRoleRealm(string AdminTypeId, string ProjectId, bool IsSuper, bool IsPlatformRealm, bool IsPlatformAdministrator,
    IReadOnlySet<string> RoleIds, IReadOnlySet<string> Permissions, IReadOnlySet<string> MenuIds, IReadOnlySet<string> ButtonIds)
{
    /// <summary>逐页面/动作的有效授权全部来自is_scope角色时记录；只保留决策，不代表查询已经执行记录过滤。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlySet<string> ExactDataManagePermissions {get;init;}=new HashSet<string>(StringComparer.Ordinal);
    /// <summary>继承的全功能来源只供复制/导入使用，不能绕过来源页标准动作开关。</summary>
    [System.Text.Json.Serialization.JsonIgnore] public IReadOnlySet<string> InheritedAllPermissions {get;init;}=new HashSet<string>(StringComparer.Ordinal);
    /// <summary>文件来源仅聚合直接/真实父子查询，选择页来源不能授予独立文件读取。</summary>
    [System.Text.Json.Serialization.JsonIgnore] public IReadOnlySet<string> FileQueryPermissions {get;init;}=new HashSet<string>(StringComparer.Ordinal);
    /// <summary>独立保留文件来源查询的范围决策，不能借选择页普通角色取消限制。</summary>
    [System.Text.Json.Serialization.JsonIgnore] public IReadOnlySet<string> FileExactDataManagePermissions {get;init;}=new HashSet<string>(StringComparer.Ordinal);
    /// <summary>真实后台仅声明一项账号匹配值，永不参与HTTP或会话序列化。</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string ExactDataManageMatchField {get;init;}="";
    [System.Text.Json.Serialization.JsonIgnore] public System.Text.Json.JsonElement? ExactDataManageMatchValue {get;init;}
}

/// <summary>Web认证与Agent复核共用同一身份后台策略，不把客户端工作区或账号兼容项目列当授权。</summary>
public static partial class PrincipalRolePolicy
{
    private static readonly HashSet<string> Actions = ["is_query", "is_new", "is_update", "is_delete", "is_export", "is_all"];

    /// <summary>每次请求读取真实后台链和本后台角色；授权目录仅使用宿主已发布的固定资源投影。</summary>
    public static async Task<PrincipalRoleRealm?> LoadAsync(SqlRowStore store, ISecurityResourceRegistry registry,
        IReadOnlyDictionary<string,object?> account, CancellationToken token=default, bool includePermissions=true)
    {
        if (!AuthenticationService.AccountActive(account) || account.GetValueOrDefault("delete_no") is not null ||
            !PositiveId(SqlRowStore.Text(account,"yz_admin_type_id"), out var adminId)) return null;
        var admin=(await store.ReadAsync("yz_admin_type",new Dictionary<string,object?>{{"id",adminId}},token)).SingleOrDefault();
        if (admin is null || !PositiveId(SqlRowStore.Text(admin,"yz_project_id"),out var projectId)) return null;
        if ((await store.ReadAsync("yz_project",new Dictionary<string,object?>{{"id",projectId}},token)).Count!=1) return null;
        var roles=await store.ReadAsync("yz_role",new Dictionary<string,object?>{{"yz_admin_type_id",adminId}},token);
        var requested=Identifiers(SqlRowStore.Text(account,"yz_role_ids"));
        var grants=includePermissions&&roles.Any(row=>requested.Contains(SqlRowStore.Text(row,"id")))
            ? await store.ReadAsync("yz_role_permission",cancellationToken:token):[];
        return Evaluate(registry,account,admin,projectId,roles,grants);
    }

    /// <summary>操作表保留现有is_all语义，具体生成路由和Service仍负责页面开关及数据范围。</summary>
    private static HashSet<string> PageActions(SecurityPageResource page)
    {
        var result=new HashSet<string>(StringComparer.Ordinal){"is_query","is_all"};
        if(page.CanCreate)result.Add("is_new");if(page.CanUpdate)result.Add("is_update");if(page.CanDelete)result.Add("is_delete");
        if(page.CanExport)result.Add("is_export");
        return result;
    }
    /// <summary>损坏、对象、浮点或非规范角色ID整体按无角色处理，不能借字符串转换提权。</summary>
    internal static HashSet<string> Identifiers(string encoded)
    {
        var result=new HashSet<string>(StringComparer.Ordinal);
        if(string.IsNullOrWhiteSpace(encoded))return result;
        if(encoded.Length>65536)return result;
        try
        {
            using var document=JsonDocument.Parse(encoded);if(document.RootElement.ValueKind==JsonValueKind.Null)return result;
            if(document.RootElement.ValueKind!=JsonValueKind.Array || document.RootElement.GetArrayLength()>1000)return result;
            foreach(var value in document.RootElement.EnumerateArray())
            {
                var text=value.ValueKind is JsonValueKind.String or JsonValueKind.Number?value.ToString():null;
                if(text is null || !PositiveId(text,out var id) || text!=id.ToString(CultureInfo.InvariantCulture))return [];
                result.Add(text);
            }
        }
        catch(JsonException){return [];}
        return result;
    }
    /// <summary>权限只接受固定字符串动作；损坏文档不会转换成布尔、对象或旧菜单授权。</summary>
    private static HashSet<string> PermissionValues(string encoded)
    {
        try
        {
            using var document=JsonDocument.Parse(encoded);if(document.RootElement.ValueKind!=JsonValueKind.Array)return [];
            var values=document.RootElement.EnumerateArray().ToArray();if(values.Any(value=>value.ValueKind!=JsonValueKind.String))return [];
            return values.Select(value=>value.GetString()!).Where(Actions.Contains).ToHashSet(StringComparer.Ordinal);
        }
        catch(JsonException){return [];}
    }
    private static bool PositiveId(string text,out long id)=>long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out id)&&id>0;
}
