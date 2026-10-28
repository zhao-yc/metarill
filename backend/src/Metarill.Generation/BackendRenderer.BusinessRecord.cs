using System.Text;
using System.Text.Json;
namespace Metarill.Generation;

internal static partial class BackendRenderer
{
    /// <summary>只有固定目录中actionType=1的业务按钮生成独立记录入口，普通CRUD与无按钮页面保持原产物。</summary>
    private static IReadOnlyList<string> BusinessRecordButtons(GenerationModel model,PageModel page)=>model.Catalog.ConfigButtons
        .Where(button=>button.ValueKind==JsonValueKind.Object&&button.TryGetProperty("configPageId",out var id)&&id.ToString()==page.Source.Id
            &&button.TryGetProperty("actionType",out var action)&&action.ToString()=="1")
        .Select(button=>button.GetProperty("id").ToString()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    /// <summary>独立业务记录访问固定主键/项目/父/页面范围，按钮授权与范围在SQL前验证，不索取页面query权限。</summary>
    private static string BusinessRecordAccess(GenerationModel model,TableModel table,PageModel page)
    {
        var buttons=BusinessRecordButtons(model,page);if(buttons.Count==0)return "";
        var suffix=page.Source.Id;var fixedQuery=FixedPageQueryCompilation.For(table,page);
        var result=new StringBuilder().AppendLine("    /// <summary>只供固定业务动作读取实际实体，不暴露HTTP查询/锁定开关。</summary>")
            .AppendLine($"    public async Task<{table.Name}?> ReadButtonRecord{suffix}Async(long id,string? parentId,string buttonId,RuntimeActor actor,CancellationToken cancellationToken)=>await ButtonRecordQuery{suffix}(id,parentId,buttonId,actor,false).FirstAsync(cancellationToken);")
            .AppendLine("    /// <summary>业务外层事务中按真实范围锁定当前实体，后续标准Service写入加入同一事务。</summary>")
            .AppendLine($"    public async Task<{table.Name}?> LockButtonRecord{suffix}Async(long id,string? parentId,string buttonId,RuntimeActor actor,CancellationToken cancellationToken)=>await ButtonRecordQuery{suffix}(id,parentId,buttonId,actor,true).FirstAsync(cancellationToken);")
            .AppendLine("    /// <summary>主键、按钮及范围均由服务端验证；请求筛选、普通页面角色与旧授权器不能扩大记录集合。</summary>")
            .AppendLine($"    private ISelect<{table.Name}> ButtonRecordQuery{suffix}(long id,string? parentId,string buttonId,RuntimeActor actor,bool locking)")
            .AppendLine("    {").AppendLine("        ValidateActor(actor);if(id<=0)throw new ArgumentException(\"业务记录标识无效。\");")
            .AppendLine($"        if(buttonId is not ({string.Join(" or ",buttons.Select(Literal))}))throw new UnauthorizedAccessException(\"按钮不属于当前已发布业务页面。\");")
            .AppendLine("        var authorization=relationAuthorization??throw new UnauthorizedAccessException(\"业务记录需要宿主真实授权。\");")
            .AppendLine("        if(authorization.RequireActor()!=actor)throw new UnauthorizedAccessException(\"业务记录身份与当前真实身份不一致。\");")
            .AppendLine($"        authorization.RequireButtonPermission({Literal(suffix)},buttonId);")
            .AppendLine("        if(locking&&session.CurrentTransaction is null)throw new InvalidOperationException(\"业务记录锁定必须加入真实写事务。\");")
            .AppendLine("        var query=mapper.Query().Where(entity=>entity.Id==id);");
        foreach(var (column,source) in fixedQuery.IdentityScopes)
        {
            if(source=="parent"&&NonRedundantCrossParentCompilation.Managed(model,table,page,column))continue;
            if(source=="parent")result.AppendLine("        if(string.IsNullOrWhiteSpace(parentId))throw new ArgumentException(\"当前页面要求parentId。\");");
            result.AppendLine($"        query=query.Where(entity=>entity.{column.Property}=={ScopeValue(column,source=="project"?"actor.ProjectId":"parentId!")});");
        }
        if(NonRedundantCrossParentCompilation.Enabled(model,table,page))result.AppendLine($"        query=LogicalParentScope{suffix}(query,actor,parentId,locking);");
        result.Append(FixedPageConditionCompilation.Apply(page,"PageCondition"+suffix,"button_page_"+suffix))
            .Append(ExactDataManageCompilation.Query(model,table,page,operation:"buttonId",prefix:"buttonExact",button:true,locking:"locking"));
        if(ManagerCredentialCompilation.Unsupported(table,page))result.AppendLine("        ManagerCredentialValue.RequirePublishedPage(false);");
        return result.AppendLine("        return locking?query.ForUpdate():query;").AppendLine("    }").ToString();
    }
}
