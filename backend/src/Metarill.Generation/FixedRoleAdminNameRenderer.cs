namespace Metarill.Generation;

/// <summary>原固定页后台名称按整页去重批量读取，不持久化显示值、不增加目标页query门槛。</summary>
internal static class FixedRoleAdminNameRenderer
{
    /// <summary>固定动作来自生成方法，不接受客户端传入授权操作。</summary>
    internal static string ProjectCall(PageModel page,string entities,string operation) => $"await ProjectRoleAdminNames{page.Source.Id}Async({entities}, actor, \"{operation}\", cancellationToken)";

    /// <summary>仅两个已知原输入槽进入兼容分支，普通页面的筛选白名单保持。</summary>
    internal static string FilterCases(GenerationModel model,TableModel table,PageModel page)=>NameFilterCases(model,table,page)+IdFilterCases(model,table,page);

    /// <summary>原单选ID保留字符串大整数、空值及标准整数字面量，不接受数组或SQL文本。</summary>
    private static string IdFilterCases(GenerationModel model,TableModel table,PageModel page)=>FixedRoleAdminNameCompilation.LegacyIdFilter(model,table,page) is {} definition?$$"""
                case "yzAdminTypeId":
                {
                    if(filter.Value.ValueKind==JsonValueKind.Null||filter.Value.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(filter.Value.GetString()))break;
                    long value=0;var valid=filter.Value.ValueKind==JsonValueKind.Number?filter.Value.TryGetInt64(out value):filter.Value.ValueKind==JsonValueKind.String&&long.TryParse(filter.Value.GetString(),NumberStyles.Integer,CultureInfo.InvariantCulture,out value);
                    if(!valid)throw new ArgumentException("后台类型筛选只接受单个整数ID。");
                    query=query.Where(entity=>entity.{{definition.Source.Property}}==value);
                    break;
                }

""":"";

    /// <summary>原配置表名称筛选只生成当前项目活动后台的相关EXISTS；请求只提供文本。</summary>
    private static string NameFilterCases(GenerationModel model,TableModel table,PageModel page)=>FixedRoleAdminNameCompilation.LegacyNameFilter(model,table,page) is {} definition?$$"""
                case "yzAdminTypeName":
                {
                    if(filter.Value.ValueKind==JsonValueKind.Null||filter.Value.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(filter.Value.GetString()))break;
                    if(filter.Value.ValueKind!=JsonValueKind.String)throw new ArgumentException("后台名称筛选只接受文本。");
                    var value=filter.Value.GetString()!;var project=long.Parse(actor.ProjectId,NumberStyles.None,CultureInfo.InvariantCulture);
                    query=query.Where(entity=>entity.{{definition.Source.Property}}!=null&&session.Orm.Select<{{definition.Admins.EntityType}}>()
                        .Where(admin=>admin.Id==entity.{{definition.Source.Property}}&&admin.YzProjectId==project&&admin.DeleteNo==null&&admin.Name!=null&&admin.Name.Contains(value)).Any());
                    break;
                }

""":"";

    /// <summary>按钮更新回显沿原按钮当前证明，不暗中增加普通页面编辑或query门槛。</summary>
    internal static string ButtonService(GenerationModel model,TableModel table,PageModel page) => FixedRoleAdminNameCompilation.ForPage(model,table,page) is {} def && FixedRoleAdminNameCompilation.Used(page,def,"update") ? $$"""
    /// <summary>只供固定按钮写核心回执调用；观察者后再次证明同一事务当前权限。</summary>
    private async Task<{{page.Name}}Vo> ProjectButtonRoleAdminName{{page.Source.Id}}Async({{table.EntityType}} entity,string buttonId,RuntimeActor actor,CancellationToken cancellationToken)
    {
        await RequireButtonCurrent{{page.Source.Id}}Async(entity.Id,buttonId,actor,cancellationToken);
        if (!long.TryParse(actor.ProjectId,NumberStyles.None,CultureInfo.InvariantCulture,out var project) || project <= 0) throw new UnauthorizedAccessException("当前项目无效。");
        if (entity.{{def.Source.Property}} is not {} id) return {{page.Name}}Vo.FromEntity(entity,null);
        if (id <= 0) throw new InvalidOperationException("后台名称标识无效。");
        var names=await session.Select<{{def.Admins.EntityType}}>().Where(admin=>admin.Id==id&&admin.DeleteNo==null&&admin.YzProjectId==project)
            .ToListAsync(admin=>new {admin.Id,admin.Name},cancellationToken);
        return {{page.Name}}Vo.FromEntity(entity,names.FirstOrDefault()?.Name);
    }

""" : "";
    /// <summary>读取仍要求当前真实来源动作，目标仅取当前项目未删除后台的ID和名称。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page) => FixedRoleAdminNameCompilation.ForPage(model,table,page) is {} def ? $$"""
    /// <summary>来源动作已经由对应入口确定；原冗余名读取不额外要求后台页面query。</summary>
    private ISelect<{{def.Admins.EntityType}}> RoleAdminNameScope{{page.Source.Id}}(RuntimeActor actor,string operation)
    {
        ValidateActor(actor);
        if (relationAuthorization is null || relationAuthorization.RequireActor() != actor) throw new UnauthorizedAccessException("后台名称要求真实来源身份。");
        if (operation is not ("list" or "detail" or "create" or "update" or "copy" or "export")) throw new UnauthorizedAccessException("后台名称读取动作无效。");
        relationAuthorization.RequirePagePermission("{{page.Source.Id}}",operation);
        if (!long.TryParse(actor.ProjectId,NumberStyles.None,CultureInfo.InvariantCulture,out var project) || project <= 0) throw new UnauthorizedAccessException("当前项目无效。");
        return session.Select<{{def.Admins.EntityType}}>().Where(admin => admin.DeleteNo == null && admin.YzProjectId == project);
    }
    /// <summary>最多5000个唯一后台ID，每200个串行一次；空值与不可见/已删除目标的名称为null。</summary>
    private async Task<{{page.Name}}Vo[]> ProjectRoleAdminNames{{page.Source.Id}}Async(IReadOnlyList<{{table.EntityType}}> entities,RuntimeActor actor,string operation,CancellationToken cancellationToken)
    {
        _ = RoleAdminNameScope{{page.Source.Id}}(actor,operation);
        var selected=entities.Select(entity=>entity.{{def.Source.Property}}).OfType<long>().Distinct().Take(5001).ToArray();
        if (selected.Length > 5000 || selected.Any(id=>id<=0)) throw new InvalidOperationException("后台名称标识无效或超过5000项完整读取上限。");
        var names=new Dictionary<long,string?>();
        foreach(var chunk in selected.Chunk(200))
            foreach(var row in await RoleAdminNameScope{{page.Source.Id}}(actor,operation).Where(admin=>chunk.Contains(admin.Id)).ToListAsync(admin=>new {admin.Id,admin.Name},cancellationToken)) names[row.Id]=row.Name;
        return entities.Select(entity=>{{page.Name}}Vo.FromEntity(entity,entity.{{def.Source.Property}} is {} id?names.GetValueOrDefault(id):null)).ToArray();
    }

""" : "";
}
