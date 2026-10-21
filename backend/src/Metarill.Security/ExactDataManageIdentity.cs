using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

/// <summary>只保留真实账号中当前后台声明的一项值；调用方先验证页/动作，不由请求创建声明。</summary>
public static class ExactDataManageIdentity
{
    /// <summary>普通认证与锁后读取共用窄值快照；不保存完整账号、密码或会话内容。</summary>
    internal static (string Field,JsonElement? Value) Capture(IReadOnlyDictionary<string,object?> admin,IReadOnlyDictionary<string,object?> account,string? publishedField=null)
    {
        string field;try{field=ExactDataManageValues.Field(publishedField??SqlRowStore.Text(admin,"exact_data_manage_match_field"));}catch(ArgumentException){return ("",null);}
        if(field.Length==0||field is "password" or "salt" or "token" or "token_hash" or "old_password")return ("",null);
        var matches=account.Where(item=>string.Equals(item.Key,field,StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length==1?(field,JsonSerializer.SerializeToElement(matches[0].Value)):(field,null);
    }
    /// <summary>每个操作独立决策；匹配字段和已认证后台声明不符时拒绝，不能换用请求字段或任意账号值。</summary>
    public static ExactDataManageValues Resolve(AuthenticatedIdentity identity,string pageId,string operation,string managerField)
    {
        var action=operation switch{"list" or "detail" or "count" or "options"=>"is_query","create"=>"is_new","update"=>"is_update","delete"=>"is_delete","export"=>"is_export","copy" or "import"=>"is_all",_=>throw new UnauthorizedAccessException("操作类型无效。")};
        return ResolveRequired(identity,identity.ExactDataManagePermissions.Contains(pageId+":"+action),managerField);
    }
    /// <summary>文件使用直接/真实父子来源的独立决策，旧身份兼容原直接页合同。</summary>
    public static ExactDataManageValues ResolveFileSource(AuthenticatedIdentity identity,string pageId,string managerField)=>
        ResolveRequired(identity,(identity.FileExactDataManagePermissions??identity.ExactDataManagePermissions).Contains(pageId+":is_query"),managerField);
    /// <summary>只有服务端明确需要范围时读取同后台窄值；缺配置不匹配，冲突声明明确拒绝。</summary>
    private static ExactDataManageValues ResolveRequired(AuthenticatedIdentity identity,bool required,string managerField)
    {
        if(!required)return ExactDataManageValues.Unrestricted;
        var expected=ExactDataManageValues.Field(managerField);
        if(expected.Length==0)return new(true,null);
        if(identity.Actor.ProjectId!=identity.PrincipalProjectId||identity.Actor.AdminTypeId!=identity.PrincipalAdminTypeId||identity.ExactDataManageMatchField!=expected)
            throw new NotSupportedException("精确管理后台匹配字段与已发布计划不一致，请同步配置及运行时。");
        return new(true,identity.ExactDataManageMatchValue?.Clone());
    }
}
