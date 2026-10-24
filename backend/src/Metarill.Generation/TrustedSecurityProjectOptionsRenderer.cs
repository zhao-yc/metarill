namespace Metarill.Generation;

/// <summary>隐藏项目关系保原POST合同；只读取包内真实项目，不借此注册项目CRUD页或更改原DOM。</summary>
internal static class TrustedSecurityProjectOptionsRenderer
{
    /// <summary>三个固定路由共用同一Service与真实授权；formParameters、请求项目和后台均不授予权限。</summary>
    internal static string Controller(GenerationModel model,TableModel table,PageModel page)
    {
        if(TrustedSecurityProjectOptionsCompilation.ForTable(model,table) is not{} plan||plan.Page!=page)return "";
        return $$"""
    /// <summary>保原隐藏字段候选合同，只支持本应用项目，不能由query指定目录。</summary>
    [HttpPost("{{TrustedSecurityProjectOptionsPlan.Endpoint}}")] [RequestSizeLimit(16384)]
    public async Task<R<IReadOnlyList<GeneratedOption>>> TrustedProjectOptions([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] System.Text.Json.JsonElement? request,CancellationToken cancellationToken)
    {
        var actor=RoleCheckboxAuthorization.Require(authorization,"{{plan.Page.Source.Id}}",{{(plan.Page.Source.IsNew==1).ToString().ToLowerInvariant()}},{{Metarill.Core.Metadata.PageUpdatePolicy.CanUpdate(plan.Page.Source).ToString().ToLowerInvariant()}});
        if(Request.Query.Count!=0)throw new ArgumentException("项目候选不接受query参数。");
        return R<IReadOnlyList<GeneratedOption>>.Success(await service.TrustedProjectOptionsAsync({{table.Name}}Service.ReadTrustedProjectLegacy(request),actor,cancellationToken));
    }
    /// <summary>仅对同一本应用项目分页/关键词，不开放其它项目或目标页权限。</summary>
    [HttpPost("{{TrustedSecurityProjectOptionsPlan.Endpoint}}/page")] [RequestSizeLimit(16384)]
    public async Task<R<PageResult<GeneratedOption>>> TrustedProjectOptionsPage([FromBody] System.Text.Json.JsonElement? request,CancellationToken cancellationToken)
    {
        var actor=RoleCheckboxAuthorization.Require(authorization,"{{plan.Page.Source.Id}}",{{(plan.Page.Source.IsNew==1).ToString().ToLowerInvariant()}},{{Metarill.Core.Metadata.PageUpdatePolicy.CanUpdate(plan.Page.Source).ToString().ToLowerInvariant()}});
        if(Request.Query.Count!=0)throw new ArgumentException("项目候选不接受query参数。");
        return R<PageResult<GeneratedOption>>.Success(await service.TrustedProjectOptionsPageAsync({{table.Name}}Service.ReadTrustedProjectPage(request),actor,cancellationToken));
    }
    /// <summary>已选回读只接受本应用ID；空选项不扩大范围，关键词不参与回读。</summary>
    [HttpPost("{{TrustedSecurityProjectOptionsPlan.Endpoint}}/selected")] [RequestSizeLimit(16384)]
    public async Task<R<IReadOnlyList<GeneratedOption>>> TrustedProjectOptionsSelected([FromBody] System.Text.Json.JsonElement? request,CancellationToken cancellationToken)
    {
        var actor=RoleCheckboxAuthorization.Require(authorization,"{{plan.Page.Source.Id}}",{{(plan.Page.Source.IsNew==1).ToString().ToLowerInvariant()}},{{Metarill.Core.Metadata.PageUpdatePolicy.CanUpdate(plan.Page.Source).ToString().ToLowerInvariant()}});
        if(Request.Query.Count!=0)throw new ArgumentException("项目候选不接受query参数。");
        return R<IReadOnlyList<GeneratedOption>>.Success(await service.TrustedProjectOptionsSelectedAsync({{table.Name}}Service.ReadTrustedProjectSelected(request),actor,cancellationToken));
    }

""";
    }

    /// <summary>固定物理图和真实身份决定范围；所有SQL仅投影Id/Name，既有认证全行读取不在本片改变。</summary>
    internal static string Service(GenerationModel model,TableModel table)
    {
        if(TrustedSecurityProjectOptionsCompilation.ForTable(model,table) is not{} plan)return "";
        return $$"""
    /// <summary>与Full同一个Scoped授权实例取真实持久身份，Agent/开发假身份或包外项目均在查询前拒绝。</summary>
    private void RequireTrustedProjectActor(RuntimeActor actor)
    {
        ValidateActor(actor);
        if(relationAuthorization is not ISecurityIdentityAccessor identities)throw new UnauthorizedAccessException("项目候选缺少真实Web身份。");
        var identity=identities.RequireIdentity();
        if(identity.Actor!=actor||relationAuthorization.RequireActor()!=actor||identity.SessionId<=0||identity.GrantId is not null||actor.IsAdministrator
            ||actor.ProjectId!="{{model.Catalog.YzProjectId}}"||identity.PrincipalProjectId!="{{model.Catalog.YzProjectId}}"||actor.AdminTypeId!="{{plan.Page.Source.YzAdminTypeId}}"||identity.PrincipalAdminTypeId!="{{plan.Page.Source.YzAdminTypeId}}")
            throw new UnauthorizedAccessException("项目候选身份或应用范围不符。");
        if(RoleCheckboxAuthorization.Require(relationAuthorization,"{{plan.Page.Source.Id}}",{{(plan.Page.Source.IsNew==1).ToString().ToLowerInvariant()}},{{Metarill.Core.Metadata.PageUpdatePolicy.CanUpdate(plan.Page.Source).ToString().ToLowerInvariant()}})!=actor)throw new UnauthorizedAccessException("项目候选身份已变化。");
    }
    /// <summary>本字段没有父记录；参数只校验不能转成授权或动态Where。</summary>
    private static void ValidateTrustedProjectParents(string? parent,string? target)
    {
        if(!string.IsNullOrWhiteSpace(parent)||!string.IsNullOrWhiteSpace(target))throw new ArgumentException("本应用项目候选不使用父范围。");
    }
    /// <summary>真实项目行不存在/墓碑直接拒绝，不用编译名称或外项目作为替代。</summary>
    private async Task<IReadOnlyList<GeneratedOption>> ReadTrustedProjectAsync(RuntimeActor actor,CancellationToken cancellationToken,string? keyword=null)
    {
        RequireTrustedProjectActor(actor);
        var currentProject={{plan.ProjectId}}L;
        var query=session.Select<{{plan.Project.Name}}>().Where(project=>project.Id==currentProject&&project.DeleteNo==null);
        if(keyword is not null)query=query.Where(project=>project.Name!=null&&project.Name.Contains(keyword));
        var rows=await query.Limit(2).ToListAsync(project=>new{project.Id,project.Name},cancellationToken);
        RequireTrustedProjectActor(actor);
        if(rows.Count>1||keyword is null&&rows.Count!=1)throw new InvalidOperationException("本应用项目不存在、已删除或状态异常。");
        return rows.Select(project=>new GeneratedOption(project.Id,project.Name,project.Id,project.Name)).ToArray();
    }
    /// <summary>旧数组端点仍保省略与显式空ID之别，不把page参数解释成全库分页。</summary>
    public async Task<IReadOnlyList<GeneratedOption>> TrustedProjectOptionsAsync(RelationOptionsLegacyRequest request,RuntimeActor actor,CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);RequireTrustedProjectActor(actor);request.Validate();ValidateTrustedProjectParents(request.ParentId,request.TargetParentId);
        var selected=request.ToSelectedRequest();
        return selected is null?await ReadTrustedProjectAsync(actor,cancellationToken):await TrustedProjectOptionsSelectedAsync(selected,actor,cancellationToken);
    }
    /// <summary>已选仅本应用；空选择仍验证当前真实项目，任一外项目整体拒绝。</summary>
    public async Task<IReadOnlyList<GeneratedOption>> TrustedProjectOptionsSelectedAsync(RelationOptionsSelectedRequest request,RuntimeActor actor,CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);RequireTrustedProjectActor(actor);ValidateTrustedProjectParents(request.ParentId,request.TargetParentId);var ids=request.ParseIds();
        if(ids.Any(id=>id!={{plan.ProjectId}}L))throw new ArgumentException("已选项目不在本应用候选范围。");
        var row=await ReadTrustedProjectAsync(actor,cancellationToken);return ids.Length==0?Array.Empty<GeneratedOption>():row;
    }
    /// <summary>至多一个项目，空关键词复用已验证行；非空关键词仍由参数化数据库Contains匹配。</summary>
    public async Task<PageResult<GeneratedOption>> TrustedProjectOptionsPageAsync(RelationOptionsPageRequest request,RuntimeActor actor,CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);RequireTrustedProjectActor(actor);request.Validate();ValidateTrustedProjectParents(request.ParentId,request.TargetParentId);
        var live=await ReadTrustedProjectAsync(actor,cancellationToken);
        var rows=string.IsNullOrEmpty(request.Keyword)?live:await ReadTrustedProjectAsync(actor,cancellationToken,request.Keyword);
        return new(request.PageNo==1?rows:Array.Empty<GeneratedOption>(),rows.Count,rows.Count,request.PageNo,request.PageSize);
    }
    /// <summary>旧表单仅接受原已知槽；复杂formParameters不会解释成连接、SQL或身份。</summary>
    public static RelationOptionsLegacyRequest ReadTrustedProjectLegacy(JsonElement? body)=>ReadTrustedProjectRequest<RelationOptionsLegacyRequest>(body,["pageNo","pageSize","filters","parentId","targetParentId","orderBy","selectedIds","formParameters","id"],true);
    /// <summary>分页请求沿用既有受限DTO外形，不接受任意项目、后台、字段或SQL。</summary>
    public static RelationOptionsPageRequest ReadTrustedProjectPage(JsonElement? body)=>ReadTrustedProjectRequest<RelationOptionsPageRequest>(body,["pageNo","pageSize","keyword","parentId","targetParentId"],false);
    /// <summary>已选请求与关键词分离，数量和精确ID继续由原DTO核验。</summary>
    public static RelationOptionsSelectedRequest ReadTrustedProjectSelected(JsonElement? body)=>ReadTrustedProjectRequest<RelationOptionsSelectedRequest>(body,["selectedIds","parentId","targetParentId"],false);
    /// <summary>重复/未知键及超大上下文在Service解析边界拒绝，错误不回显请求值。</summary>
    private static T ReadTrustedProjectRequest<T>(JsonElement? body,string[] fields,bool allowEmpty)where T:new()
    {
        if(body is null||body.Value.ValueKind==JsonValueKind.Null){if(allowEmpty)return new();throw new ArgumentException("项目候选请求不能为空。");}
        var root=body.Value;if(root.ValueKind!=JsonValueKind.Object||root.GetRawText().Length>16384)throw new ArgumentException("项目候选请求必须为有界对象。");
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in root.EnumerateObject())
        {
            if(!names.Add(field.Name)||!fields.Contains(field.Name,StringComparer.Ordinal))throw new ArgumentException("项目候选请求字段重复或未开放。");
            if(field.Name=="formParameters"&&(field.Value.GetRawText().Length>8192||field.Value.ValueKind is not(JsonValueKind.Null or JsonValueKind.Object or JsonValueKind.String)))throw new ArgumentException("项目候选表单上下文无效。");
        }
        return JsonSerializer.Deserialize<T>(root.GetRawText(),JsonOptions)??throw new ArgumentException("项目候选请求无效。");
    }

""";
    }
}
