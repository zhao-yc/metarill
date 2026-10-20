namespace Metarill.Generation;

/// <summary>原角色页后台名称按整页去重批量读取，不持久化显示值、不增加目标页query门槛。</summary>
internal static class FixedRoleAdminNameRenderer
{
    /// <summary>固定动作来自生成方法，不接受客户端传入授权操作。</summary>
    internal static string ProjectCall(PageModel page,string entities,string operation) => $"await ProjectRoleAdminNames{page.Source.Id}Async({entities}, actor, \"{operation}\", cancellationToken)";

    /// <summary>读取仍要求当前真实来源动作，目标仅取当前项目未删除后台的ID和名称。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page) => FixedRoleAdminNameCompilation.ForPage(model,table,page) is {} def ? $$"""
    /// <summary>来源动作已经由对应入口确定；原冗余名读取不额外要求后台页面query。</summary>
    private ISelect<{{def.Admins.Name}}> RoleAdminNameScope{{page.Source.Id}}(RuntimeActor actor,string operation)
    {
        ValidateActor(actor);
        if (relationAuthorization is null || relationAuthorization.RequireActor() != actor) throw new UnauthorizedAccessException("后台名称要求真实来源身份。");
        if (operation is not ("list" or "detail" or "create" or "update" or "copy" or "export")) throw new UnauthorizedAccessException("后台名称读取动作无效。");
        relationAuthorization.RequirePagePermission("{{page.Source.Id}}",operation);
        if (!long.TryParse(actor.ProjectId,NumberStyles.None,CultureInfo.InvariantCulture,out var project) || project <= 0) throw new UnauthorizedAccessException("当前项目无效。");
        return session.Select<{{def.Admins.Name}}>().Where(admin => admin.DeleteNo == null && admin.YzProjectId == project);
    }
    /// <summary>最多5000个唯一后台ID，每200个串行一次；空值与不可见/已删除目标的名称为null。</summary>
    private async Task<{{page.Name}}Vo[]> ProjectRoleAdminNames{{page.Source.Id}}Async(IReadOnlyList<{{table.Name}}> entities,RuntimeActor actor,string operation,CancellationToken cancellationToken)
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
