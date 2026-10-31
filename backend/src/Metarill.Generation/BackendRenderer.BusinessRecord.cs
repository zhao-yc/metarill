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
            .AppendLine(RuleRecordFields(model,table,page).Count>0?$"    public async Task<{table.EntityType}?> ReadButtonRecord{suffix}Async(long id,string? parentId,string buttonId,RuntimeActor actor,CancellationToken cancellationToken){{RequireButtonRecordAccess{suffix}(id,buttonId,actor);return await ReadRuleRecord{suffix}Async(id,parentId,buttonId,actor,true,false,cancellationToken);}}":$"    public async Task<{table.EntityType}?> ReadButtonRecord{suffix}Async(long id,string? parentId,string buttonId,RuntimeActor actor,CancellationToken cancellationToken)=>await ButtonRecordQuery{suffix}(id,parentId,buttonId,actor,false).FirstAsync(cancellationToken);")
            .AppendLine("    /// <summary>业务外层事务中按真实范围锁定当前实体，后续标准Service写入加入同一事务。</summary>")
            .AppendLine(RuleRecordFields(model,table,page).Count>0?$"    public async Task<{table.EntityType}?> LockButtonRecord{suffix}Async(long id,string? parentId,string buttonId,RuntimeActor actor,CancellationToken cancellationToken){{await RequireButtonCurrent{suffix}Async(id,buttonId,actor,cancellationToken);var stored=await ButtonRecordQuery{suffix}(id,parentId,buttonId,actor,true).FirstAsync(cancellationToken);return stored is null?null:await ReadRuleRecord{suffix}Async(id,parentId,buttonId,actor,true,true,cancellationToken);}}":$"    public async Task<{table.EntityType}?> LockButtonRecord{suffix}Async(long id,string? parentId,string buttonId,RuntimeActor actor,CancellationToken cancellationToken){{await RequireButtonCurrent{suffix}Async(id,buttonId,actor,cancellationToken);return await ButtonRecordQuery{suffix}(id,parentId,buttonId,actor,true).FirstAsync(cancellationToken);}}")
            .AppendLine("    /// <summary>服务端固定按钮及真实主体校验在打开写事务前也可执行。</summary>")
            .AppendLine($"    private void RequireButtonRecordAccess{suffix}(long id,string buttonId,RuntimeActor actor)")
            .AppendLine("    {").AppendLine("        ValidateActor(actor);if(id<=0)throw new ArgumentException(\"业务记录标识无效。\");")
            .AppendLine($"        if(buttonId is not ({string.Join(" or ",buttons.Select(Literal))}))throw new UnauthorizedAccessException(\"按钮不属于当前已发布业务页面。\");")
            .AppendLine("        var authorization=relationAuthorization??throw new UnauthorizedAccessException(\"业务记录需要宿主真实授权。\");")
            .AppendLine("        if(authorization.RequireActor()!=actor)throw new UnauthorizedAccessException(\"业务记录身份与当前真实身份不一致。\");")
            .AppendLine($"        authorization.RequireButtonPermission({Literal(suffix)},buttonId);")
            .AppendLine("    }")
            .AppendLine("    /// <summary>锁记录与实际写入前，沿原事务当前读重新证明按钮及请求范围。</summary>")
            .AppendLine($"    private Task RequireButtonCurrent{suffix}Async(long id,string buttonId,RuntimeActor actor,CancellationToken cancellationToken)")
            .AppendLine("    {").AppendLine($"        RequireButtonRecordAccess{suffix}(id,buttonId,actor);")
            .AppendLine("        var transaction=session.CurrentTransaction??throw new InvalidOperationException(\"业务按钮当前权限必须加入真实写事务。\");")
            .AppendLine($"        return relationAuthorization!.RequireButtonCurrentAsync({Literal(suffix)},buttonId,actor,transaction,cancellationToken);").AppendLine("    }")
            .AppendLine("    /// <summary>主键、按钮及范围均由服务端验证；请求筛选、普通页面角色与旧授权器不能扩大记录集合。</summary>")
            .AppendLine($"    private ISelect<{table.EntityType}> ButtonRecordQuery{suffix}(long id,string? parentId,string buttonId,RuntimeActor actor,bool locking)")
            .AppendLine("    {").AppendLine($"        RequireButtonRecordAccess{suffix}(id,buttonId,actor);")
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
    /// <summary>业务按钮与普通更新复用一个生成核心；页面关闭普通更新时也不伪造页面授权。</summary>
    private static string BusinessUpdateEntry(TableModel table,PageModel page,bool standardUpdate)
    {
        var suffix=page.Source.Id;var result=new StringBuilder();
        if(standardUpdate)result.AppendLine($"    public Task<{page.Name}Vo> Update{suffix}Async({page.Name}UpdateRequest request,string? parentId,RuntimeActor actor,CancellationToken cancellationToken)=>Update{suffix}CoreAsync(request,parentId,actor,null,cancellationToken);");
        return result.AppendLine("    /// <summary>只供服务端固定业务动作调用；仍使用本页字段存在性、对象/关系规则和同一事务。</summary>")
            .AppendLine($"    public Task<{page.Name}Vo> UpdateButtonRecord{suffix}Async({page.Name}UpdateRequest request,string? parentId,string buttonId,RuntimeActor actor,CancellationToken cancellationToken)")
            .AppendLine("    {").AppendLine("        ArgumentNullException.ThrowIfNull(request);")
            .AppendLine($"        RequireButtonRecordAccess{suffix}(request.Id,buttonId,actor);")
            .AppendLine($"        return Update{suffix}CoreAsync(request,parentId,actor,null,cancellationToken,buttonId);").AppendLine("    }")
            .AppendLine("    /// <summary>普通/导入/按钮来源显式进入共同写入核心，客户端无来源开关。</summary>")
            .AppendLine($"    private Task<{page.Name}Vo> Update{suffix}CoreAsync({page.Name}UpdateRequest request,string? parentId,RuntimeActor actor,RecordMutationOperation? authorizationOperation,CancellationToken cancellationToken,string? businessButtonId=null)=>session.InTransactionAsync(async()=>")
            .ToString();
    }

}
