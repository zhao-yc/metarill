using System.Security;

namespace Metarill.Core.Security;

/// <summary>原findNameId候选沿来源页查询、新增或修改任一授权，不借候选授予业务写权。</summary>
public static class RelationOptionAuthorization
{
    /// <summary>身份先建立，只捕获明确拒权；依赖异常原样传播，不把服务故障当作尝试下一权限。</summary>
    public static RuntimeActor Require(IRuntimeAuthorization authorization,string pageId,bool canCreate,bool canUpdate)
    {
        var actor=authorization.RequireActor();var forbidden=false;
        foreach(var operation in new[]{"list",canCreate?"create":null,canUpdate?"update":null}.OfType<string>())
        {
            try{authorization.RequirePagePermission(pageId,operation);return actor;}
            catch(UnauthorizedAccessException){forbidden=false;}
            catch(SecurityException){forbidden=true;}
        }
        const string message="当前页面未授予查询、新增或修改权限。";
        if(forbidden)throw new SecurityException(message);throw new UnauthorizedAccessException(message);
    }
}
