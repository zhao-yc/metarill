using System.Text;
using Metarill.Core.Contracts;

namespace Metarill.Generation;

/// <summary>仅编译元数据type=2的独立多选页，不将多值关系控件声明为已支持。</summary>
internal static class SelectionPageRenderer
{
    /// <summary>页面类型来自已保存元数据，客户端参数不能启用新回读端点。</summary>
    internal static bool Enabled(PageModel page) => page.Source.Extra.TryGetValue("type", out var value) && value.ToString() == "2";

    /// <summary>已选回读必须逐项完整命中同一真实页范围，不能静默丢弃越权项。</summary>
    internal static string Service(GenerationModel model, PageModel page)
    {
        if (!Enabled(page)) return "";
        var suffix = page.Source.Id;
        var table = model.Tables.Single(table => table.Source.Id == page.Source.ConfigTableId);
        if (!table.IsGlobal && !table.Columns.Any(column => column.Source.ColumnName == "yz_project_id")) throw new InvalidOperationException("多选页目标未声明项目归属或明确全局范围。");
        var result = new StringBuilder()
            .AppendLine("    /// <summary>多选页各读取入口共享真实父记录校验，读取不申请记录写锁。</summary>")
            .AppendLine($"    private async Task ValidateSelectionParents{suffix}Async(string? parentId, RuntimeActor actor, CancellationToken cancellationToken)")
            .AppendLine("    {").AppendLine("        ValidateActor(actor); await Task.CompletedTask;");
        var number = 0;
        foreach (var (column, _) in page.Scopes.Where(scope => scope.Source == "parent"))
        {
            var redundantParents=SharedRedundantParentCompilation.ForColumn(model,table,page,column);
            if(redundantParents.Count>0)
            {
                // 原多选页同样按父字段的固定角色读取，同一目标表的其它层不能替代。
                foreach(var parentPath in redundantParents)result.Append(SharedRedundantParentCompilation.Call(parentPath,page,"false"));
                continue;
            }
            if(NonRedundantCrossParentCompilation.Managed(model,table,page,column))
            {
                var path=NonRedundantCrossParentCompilation.Find(model,table,page,column)!.Value;
                result.AppendLine($"        _=LogicalParentId{suffix}(parentId);await CrossReader{suffix}_{path.Read.Source.Source.Id}(actor,false,false).PathAsync({path.Index},long.Parse(parentId!,NumberStyles.None,CultureInfo.InvariantCulture),cancellationToken);");continue;
            }
            if(MultiGlobalParentCompilation.PhysicalScopeTarget(model,table,page) is not null||
                GlobalCachedParentCompilation.ForSelfTree(model,table,page) is not null)
            {
                result.AppendLine($"        await ValidateParents{suffix}Async(parentId,actor,cancellationToken,locking:false);");
                continue;
            }
            var parent = model.Tables.SingleOrDefault(candidate => !string.IsNullOrWhiteSpace(column.Source.LinkConfigTableId)
                ? candidate.Source.Id == column.Source.LinkConfigTableId : candidate.Source.TableName + "_id" == column.Source.ColumnName)
                ?? throw new InvalidOperationException("多选页父范围缺少明确目标表：" + column.Source.ColumnName);
            var project = parent.Columns.SingleOrDefault(candidate => candidate.Source.ColumnName == "yz_project_id")
                ?? throw new InvalidOperationException("多选页父表未声明项目归属。");
            number++;
            result.AppendLine("        if (!long.TryParse(parentId, NumberStyles.None, CultureInfo.InvariantCulture, out var parentKey" + number + ") || parentKey" + number + " <= 0) throw new ArgumentException(\"当前多选页要求有效parentId。\");")
                .AppendLine($"        var parent{number} = session.Select<{parent.EntityType}>().Where(row => row.Id == parentKey{number}).Where(row => row.{project.Property} == {(project.Type == "string?" ? "actor.ProjectId" : "long.Parse(actor.ProjectId, CultureInfo.InvariantCulture)")});");
            if (parent.Columns.Any(candidate => candidate.Source.ColumnName == "delete_no")) result.AppendLine($"        parent{number} = parent{number}.Where(row => row.DeleteNo == null);");
            result.AppendLine($"        if (!await parent{number}.AnyAsync(cancellationToken)) throw new InvalidOperationException(\"多选页父记录不存在、已删除或不属于当前项目。\");");
        }
        result.AppendLine("    }").AppendLine("    /// <summary>多选确认前重新读取本页完整可见行，任何失效项整体拒绝。</summary>")
            .AppendLine($"    public {(CascadeTreeQueryCompilation.Snapshot(model,table,page)?"Task":"async Task")}<IReadOnlyList<{page.Name}Vo>> SelectionSelected{suffix}Async(SelectionRowsRequest request, RuntimeActor actor, CancellationToken cancellationToken){(CascadeTreeQueryCompilation.Snapshot(model,table,page)?$" => {GlobalCachedSourceCompilation.ReadSession(model)}.InReadSnapshotAsync<IReadOnlyList<{page.Name}Vo>>(async () =>":"")}")
            .AppendLine("    {").AppendLine("        ArgumentNullException.ThrowIfNull(request); var selected = request.ParseIds();")
            .AppendLine($"        await ValidateSelectionParents{suffix}Async(request.ParentId, actor, cancellationToken);");
        if(NonRedundantReadCompilation.Requires(model,table))
        {
            var globalCandidate=NonRedundantReadCompilation.GlobalSelectionPage(table,page);
            var bindAccount=globalCandidate&&(CrossTableWriteCompilation.ForService(model,table,page).Count>0||FixedPageConditionCompilation.HasAccountVariables(page)||
                NonRedundantReadCompilation.SelfTreeNameQueries(model,table,page).Any(tree=>FixedPageConditionCompilation.HasAccountVariables(tree.TargetPage)));
            return result.AppendLine($"        var filters=ReadFilters{suffix}(new PageRequest{{ParentId=request.ParentId}},actor{(globalCandidate?",globalCandidate:true":"")});")
                .AppendLine("        if(selected.Length==0)return [];")
                .AppendLine($"        var selectedRows=await new Metarill.Data.Queries.NonRedundantReader(session).KeyRowsAsync({(globalCandidate?"CandidateReadPlan":"ReadPlan")}{suffix}{(bindAccount?".BindAccount(actor)":"")},filters,selected,cancellationToken,projectId:actor.ProjectId{(globalCandidate?"":FixedPageConditionCompilation.ReadArgument(page,true))});")
                .AppendLine("        if(selectedRows.Count!=selected.Length)throw new InvalidOperationException(\"已选记录不存在、已删除或不在当前多选页范围，请重新确认选择。\");")
                .AppendLine("        var rows=selectedRows.ToDictionary(row=>Metarill.Data.Queries.NonRedundantReads.Value<long>(row,\"id\"));")
                .AppendLine($"        return {BackendRenderer.NonRedundantProjectionCall(model,table,page,"selected.Select(id=>rows[id]).ToArray()","false")};").AppendLine(CascadeTreeQueryCompilation.Snapshot(model,table,page)?"    }, cancellationToken);":"    }").ToString();
        }
        // 固定计算名称的实际来源动作仍是本页list；先拒绝再读源行，不借候选的任一动作权限。
        if (FixedRoleCheckboxCompilation.ForPage(model,table,page) is not null) result.AppendLine($"        RequireRoleNameAction{suffix}(actor,\"list\");");
        else if (FixedRoleAdminNameCompilation.ForPage(model,table,page) is not null) result.AppendLine($"        _ = RoleAdminNameScope{suffix}(actor,\"list\");");
        var projected = FixedRoleCheckboxCompilation.ForPage(model,table,page) is not null ? FixedRoleCheckboxRenderer.ProjectCall(page,"ordered","list",!ManagerCredentialCompilation.IsOriginal(table,page))
            : FixedRoleAdminNameCompilation.ForPage(model,table,page) is not null ? FixedRoleAdminNameRenderer.ProjectCall(page,"ordered","list") : null;
        if(projected is null&&(CascadeTreeViewRenderer.ForPage(model,table,page).Count>0||CrossTableDisplayRenderer.ForPage(model,table,page).Count>0))projected=CascadeTreeViewRenderer.Decorate(model,table,page,$"ordered.Select({page.Name}Vo.FromEntity).ToArray()","ordered");
        return result.AppendLine($"        var query = {CascadeTreeQueryCompilation.Call(page,$"new PageRequest {{ ParentId = request.ParentId, SelectedIds = selected.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList() }}")};")
            .AppendLine("        if (selected.Length == 0) return [];")
            .AppendLine($"        var entities = await query.Limit(200).ToListAsync({PublishedReadProjectionCompilation.Expression(model, table, page)}, cancellationToken);")
            .AppendLine("        if (entities.Count != selected.Length) throw new InvalidOperationException(\"已选记录不存在、已删除或不在当前多选页范围，请重新确认选择。\");")
            .AppendLine("        var rows = entities.ToDictionary(row => row.Id);")
            .Append(projected is null ? "" : "        var ordered = selected.Select(id => rows[id]).ToArray();\n")
            .AppendLine(projected is not null ? "        return " + projected + ";" : $"        return selected.Select(id => {page.Name}Vo.FromEntity(rows[id]{(ManagerCredentialCompilation.IsOriginal(table,page) ? FixedRoleCheckboxCompilation.ForPage(model,table,page) is null ? ",false" : ",null,false" : "")})).ToArray();").AppendLine(CascadeTreeQueryCompilation.Snapshot(model,table,page)?"    }, cancellationToken);":"    }").ToString();
    }

    /// <summary>回读使用目标页list权限与真实Actor，不借用请求项目或来源控件配置。</summary>
    internal static string Controller(GenerationModel model,TableModel table,PageModel page)
    {
        if(!Enabled(page))return "";
        var labels=ExtraTextLabels(model,table,page);
        if(labels.Length==0)
            return $"    /// <summary>多选页已选行整体回读，确认前再次验证当前范围。</summary>\n    [HttpPost(\"selection/selected\")]\n    public async Task<R<IReadOnlyList<{page.Name}Vo>>> SelectionSelected([FromBody] SelectionRowsRequest request, CancellationToken cancellationToken) => R<IReadOnlyList<{page.Name}Vo>>.Success(await service.SelectionSelected{page.Source.Id}Async(request, Actor(\"list\"), cancellationToken));\n";

        var fields=string.Join("\n",labels.Select(label=>"            item["+System.Text.Json.JsonSerializer.Serialize(label.JsonName)+
            "] = "+MultiSelectExcelRenderer.LabelText(label,"row."+label.Property)+";"));
        return $$"""
    /// <summary>只在已选回执中转换发布的名称字段，浏览器不会先把高精度值解析成浮点数。</summary>
    [HttpPost("selection/selected")]
    public async Task<R<IReadOnlyList<global::System.Text.Json.Nodes.JsonObject>>> SelectionSelected(
        [FromBody] SelectionRowsRequest request,
        [FromServices] global::Microsoft.Extensions.Options.IOptions<global::Microsoft.AspNetCore.Mvc.JsonOptions> json,
        CancellationToken cancellationToken)
    {
        var rows = await service.SelectionSelected{{page.Source.Id}}Async(request, Actor("list"), cancellationToken);
        var response = rows.Select(row =>
        {
            var item = global::System.Text.Json.JsonSerializer.SerializeToNode(row, json.Value.JsonSerializerOptions)!.AsObject();
{{fields}}
            return item;
        }).ToArray();
        return R<IReadOnlyList<global::System.Text.Json.Nodes.JsonObject>>.Success(response);
    }

""";
    }

    /// <summary>只使用真实消费者绑定的名称列；其它回执字段和列表接口继续走原 JSON 合同。</summary>
    private static ColumnModel[] ExtraTextLabels(GenerationModel model,TableModel table,PageModel page)
    {
        if(!page.Readable.Any(column=>column.Type is not ("int?" or "long?" or "long")&&
            MultiValueLabel.IsTyped(column.Source.LogicalType)))return [];
        return model.Tables.SelectMany(source=>source.Pages.SelectMany(sourcePage=>MultiSelectCompilation.ForPage(model,source,sourcePage)))
            .Where(definition=>definition.Target.Source.Id==table.Source.Id&&definition.TargetPage.Source.Id==page.Source.Id&&
                definition.TypedLabel&&!definition.IntegerLabel)
            .Select(definition=>definition.Name).DistinctBy(column=>column.Source.Id).ToArray();
    }
}
