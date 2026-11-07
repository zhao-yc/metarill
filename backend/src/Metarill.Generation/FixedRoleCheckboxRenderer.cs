namespace Metarill.Generation;

/// <summary>原复选只补固定后端合同，不生成新页面、弹窗或后台切换交互。</summary>
internal static class FixedRoleCheckboxRenderer
{
    /// <summary>仅生成固定页面名称投影调用，实体参数不来自HTTP表达式。</summary>
    internal static string ProjectCall(PageModel page, string entities, string operation, bool credentials=true) => $"await ProjectRoleNames{page.Source.Id}Async({entities}, actor, \"{operation}\", cancellationToken{(credentials ? "" : ",false")})";

    /// <summary>候选仅使用源页动作与真实服务器工作区；表单上下文不能选择项目、表或SQL。</summary>
    internal static string Controller(GenerationModel model, TableModel table, PageModel page) => FixedRoleCheckboxCompilation.ForPage(model,table,page) is {} def ? $$"""
    /// <summary>原角色复选返回完整有界四字段数组，不按页面截断；后台提示不构成授权。</summary>
    [HttpPost("{{def.Endpoint}}")] [RequestSizeLimit(16384)]
    public async Task<R<IReadOnlyList<RoleCheckboxOption>>> {{def.Endpoint}}([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] System.Text.Json.JsonElement? request, CancellationToken cancellationToken)
    {
        var actor = RoleCheckboxAuthorization.Require(authorization,"{{page.Source.Id}}",{{(page.Source.IsNew==1).ToString().ToLowerInvariant()}},{{Metarill.Core.Metadata.PageUpdatePolicy.CanUpdate(page.Source).ToString().ToLowerInvariant()}});
        if (Request.Query.Count != 0) throw new ArgumentException("角色候选不接受query参数。");
        var selected = RoleCheckboxValue.ReadCandidateRequest(request);
        return R<IReadOnlyList<RoleCheckboxOption>>.Success(await service.RoleOptions{{page.Source.Id}}Async(selected, actor, cancellationToken));
    }

""" : "";

    /// <summary>名字读取与候选使用同一真实归属子查询；兼容角色项目列不参与授权。</summary>
    internal static string Service(GenerationModel model, TableModel table, PageModel page)
    {
        if (FixedRoleCheckboxCompilation.ForService(model,table,page) is not {} def) return "";
        var scope = $$"""
    /// <summary>当前actor必须与宿主认证一致，读取仍经过原来源页面权限。</summary>
    private ISelect<{{def.Roles.EntityType}}> RoleNameScope{{page.Source.Id}}(RuntimeActor actor)
    {
        ValidateActor(actor);
        if (relationAuthorization is null || relationAuthorization.RequireActor() != actor) throw new UnauthorizedAccessException("角色候选要求真实当前身份。");
        if (!long.TryParse(actor.ProjectId, NumberStyles.None, CultureInfo.InvariantCulture, out var project) || project <= 0) throw new UnauthorizedAccessException("当前项目无效。");
        // 后台的真实项目是归属事实，不能用角色的兼容项目列扩大或缩小范围。
        return session.Select<{{def.Roles.EntityType}}>().Where(role => role.DeleteNo == null && session.Orm.Select<{{def.Admins.EntityType}}>()
            .Where(admin => admin.Id == role.YzAdminTypeId && admin.DeleteNo == null && admin.YzProjectId == project).Any());
    }
    /// <summary>候选沿用原查询/新增/修改任一授权；最终关系校验也保留此原入口合同。</summary>
    private ISelect<{{def.Roles.EntityType}}> RoleOptionsScope{{page.Source.Id}}(RuntimeActor actor)
    {
        if (relationAuthorization is null || RoleCheckboxAuthorization.Require(relationAuthorization,"{{page.Source.Id}}",{{(page.Source.IsNew==1).ToString().ToLowerInvariant()}},{{Metarill.Core.Metadata.PageUpdatePolicy.CanUpdate(page.Source).ToString().ToLowerInvariant()}}) != actor) throw new UnauthorizedAccessException("角色读取身份已变化。");
        return RoleNameScope{{page.Source.Id}}(actor);
    }

""";
        // 页面未开放读取时仅保留内部校验范围，不公开候选方法或计算名称VO。
        if (FixedRoleCheckboxCompilation.ForPage(model,table,page) is null) return scope;
        return scope + $$"""
    /// <summary>全选必须取得完整数组，超过上限明确拒绝；普通超级角色不被额外隐藏。</summary>
    public async Task<IReadOnlyList<RoleCheckboxOption>> RoleOptions{{page.Source.Id}}Async(long[]? selected, RuntimeActor actor, CancellationToken cancellationToken)
    {
        var query = RoleOptionsScope{{page.Source.Id}}(actor);
        if (selected is not null && (selected.Length > 250 || selected.Any(id => id <= 0))) throw new ArgumentException("角色候选指定项必须为有界正整数。");
        if (selected is not null) { if (selected.Length == 0) return []; query = query.Where(role => selected.Contains(role.Id)); }
        var rows = await query.OrderBy(role => role.Id).Limit(5001).ToListAsync(role => new { role.Id, role.Name }, cancellationToken);
        if (rows.Count > 5000) throw new InvalidOperationException("角色候选超过5000项，原全选不能返回截断结果。");
        return rows.Select(row => { var id = row.Id.ToString(CultureInfo.InvariantCulture); return new RoleCheckboxOption(id,row.Name,id,row.Name); }).ToArray();
    }
    /// <summary>名称投影必须通过实际来源动作；不能借候选的新增/修改权限读取列表或选择确认。</summary>
    private void RequireRoleNameAction{{page.Source.Id}}(RuntimeActor actor,string operation)
    {
        _ = RoleNameScope{{page.Source.Id}}(actor);
        if (operation is not ("list" or "detail" or "create" or "update" or "copy")) throw new UnauthorizedAccessException("角色名称读取动作无效。");
        relationAuthorization!.RequirePagePermission("{{page.Source.Id}}",operation);
    }
    /// <summary>批量读取仅投影角色ID与名称；原数组顺序及缺名位置不随数据库排序变化。</summary>
    private async Task<{{page.Name}}Vo[]> ProjectRoleNames{{page.Source.Id}}Async(IReadOnlyList<{{table.EntityType}}> entities, RuntimeActor actor, string operation, CancellationToken cancellationToken{{(ManagerCredentialCompilation.IsOriginal(table,page) ? ",bool includeCredentials=true" : "")}})
    {
        RequireRoleNameAction{{page.Source.Id}}(actor,operation);
        var selected = RoleCheckboxValue.ProjectionIds(entities.Select(entity => entity.{{def.Source.Property}}));
        var names = new Dictionary<long,string?>();
        foreach (var chunk in selected.Chunk(RoleCheckboxValue.ProjectionBatchSize))
        {
            // 每批重新核对当前来源权限，不用此前候选成功或前一批结果代替当前授权。
            RequireRoleNameAction{{page.Source.Id}}(actor,operation);
            foreach (var row in await RoleNameScope{{page.Source.Id}}(actor).Where(role => chunk.Contains(role.Id)).ToListAsync(role => new { role.Id, role.Name }, cancellationToken)) names[row.Id] = row.Name;
        }
        return entities.Select(entity => {{page.Name}}Vo.FromEntity(entity,RoleCheckboxValue.ProjectNames(entity.{{def.Source.Property}},names){{(ManagerCredentialCompilation.IsOriginal(table,page) ? ",includeCredentials" : "")}})).ToArray();
    }

""";
    }
}
