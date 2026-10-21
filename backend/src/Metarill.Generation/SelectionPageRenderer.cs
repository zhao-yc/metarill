using System.Text;

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
            var parent = model.Tables.SingleOrDefault(candidate => !string.IsNullOrWhiteSpace(column.Source.LinkConfigTableId)
                ? candidate.Source.Id == column.Source.LinkConfigTableId : candidate.Source.TableName + "_id" == column.Source.ColumnName)
                ?? throw new InvalidOperationException("多选页父范围缺少明确目标表：" + column.Source.ColumnName);
            var project = parent.Columns.SingleOrDefault(candidate => candidate.Source.ColumnName == "yz_project_id")
                ?? throw new InvalidOperationException("多选页父表未声明项目归属。");
            number++;
            result.AppendLine("        if (!long.TryParse(parentId, NumberStyles.None, CultureInfo.InvariantCulture, out var parentKey" + number + ") || parentKey" + number + " <= 0) throw new ArgumentException(\"当前多选页要求有效parentId。\");")
                .AppendLine($"        var parent{number} = session.Select<{parent.Name}>().Where(row => row.Id == parentKey{number}).Where(row => row.{project.Property} == {(project.Type == "string?" ? "actor.ProjectId" : "long.Parse(actor.ProjectId, CultureInfo.InvariantCulture)")});");
            if (parent.Columns.Any(candidate => candidate.Source.ColumnName == "delete_no")) result.AppendLine($"        parent{number} = parent{number}.Where(row => row.DeleteNo == null);");
            result.AppendLine($"        if (!await parent{number}.AnyAsync(cancellationToken)) throw new InvalidOperationException(\"多选页父记录不存在、已删除或不属于当前项目。\");");
        }
        result.AppendLine("    }").AppendLine("    /// <summary>多选确认前重新读取本页完整可见行，任何失效项整体拒绝。</summary>")
            .AppendLine($"    public async Task<IReadOnlyList<{page.Name}Vo>> SelectionSelected{suffix}Async(SelectionRowsRequest request, RuntimeActor actor, CancellationToken cancellationToken)")
            .AppendLine("    {").AppendLine("        ArgumentNullException.ThrowIfNull(request); var selected = request.ParseIds();")
            .AppendLine($"        await ValidateSelectionParents{suffix}Async(request.ParentId, actor, cancellationToken);");
        if(NonRedundantReadCompilation.Requires(model,table))
            return result.AppendLine($"        var filters=ReadFilters{suffix}(new PageRequest{{ParentId=request.ParentId}},actor);")
                .AppendLine("        if(selected.Length==0)return [];")
                .AppendLine("        filters.Add(new(\"id\",Metarill.Data.Queries.FixedReadComparison.In,selected.Cast<object?>().ToArray()));")
                .AppendLine($"        var selectedRows=await new Metarill.Data.Queries.NonRedundantReader(session).PageAsync(ReadPlan{suffix},filters,Array.Empty<Metarill.Data.Queries.FixedReadOrder>(),1,selected.Length,cancellationToken,projectId:actor.ProjectId{FixedPageConditionCompilation.ReadArgument(page,true)});")
                .AppendLine("        if(selectedRows.Records.Count!=selected.Length)throw new InvalidOperationException(\"已选记录不存在、已删除或不在当前多选页范围，请重新确认选择。\");")
                .AppendLine("        var rows=selectedRows.Records.ToDictionary(row=>Metarill.Data.Queries.NonRedundantReads.Value<long>(row,\"id\"));")
                .AppendLine($"        return selected.Select(id=>{page.Name}Vo.FromReadRow(rows[id])).ToArray();").AppendLine("    }").ToString();
        // 固定计算名称的实际来源动作仍是本页list；先拒绝再读源行，不借候选的任一动作权限。
        if (FixedRoleCheckboxCompilation.ForPage(model,table,page) is not null) result.AppendLine($"        RequireRoleNameAction{suffix}(actor,\"list\");");
        else if (FixedRoleAdminNameCompilation.ForPage(model,table,page) is not null) result.AppendLine($"        _ = RoleAdminNameScope{suffix}(actor,\"list\");");
        var projected = FixedRoleCheckboxCompilation.ForPage(model,table,page) is not null ? FixedRoleCheckboxRenderer.ProjectCall(page,"ordered","list",!ManagerCredentialCompilation.IsOriginal(table,page))
            : FixedRoleAdminNameCompilation.ForPage(model,table,page) is not null ? FixedRoleAdminNameRenderer.ProjectCall(page,"ordered","list") : null;
        return result.AppendLine($"        var query = Query{suffix}(new PageRequest {{ ParentId = request.ParentId, SelectedIds = selected.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList() }}, actor);")
            .AppendLine("        if (selected.Length == 0) return [];")
            .AppendLine(ManagerCredentialCompilation.IsTable(table) ? $"        var entities = await query.Limit(200).ToListAsync({ManagerCredentialCompilation.ReadProjection(table)}, cancellationToken);" : "        var entities = await query.Limit(200).ToListAsync(cancellationToken);")
            .AppendLine("        if (entities.Count != selected.Length) throw new InvalidOperationException(\"已选记录不存在、已删除或不在当前多选页范围，请重新确认选择。\");")
            .AppendLine("        var rows = entities.ToDictionary(row => row.Id);")
            .Append(projected is null ? "" : "        var ordered = selected.Select(id => rows[id]).ToArray();\n")
            .AppendLine(projected is not null ? "        return " + projected + ";" : $"        return selected.Select(id => {page.Name}Vo.FromEntity(rows[id]{(ManagerCredentialCompilation.IsOriginal(table,page) ? FixedRoleCheckboxCompilation.ForPage(model,table,page) is null ? ",false" : ",null,false" : "")})).ToArray();").AppendLine("    }").ToString();
    }

    /// <summary>回读使用目标页list权限与真实Actor，不借用请求项目或来源控件配置。</summary>
    internal static string Controller(PageModel page) => !Enabled(page) ? "" : $"    /// <summary>多选页已选行整体回读，确认前再次验证当前范围。</summary>\n    [HttpPost(\"selection/selected\")]\n    public async Task<R<IReadOnlyList<{page.Name}Vo>>> SelectionSelected([FromBody] SelectionRowsRequest request, CancellationToken cancellationToken) => R<IReadOnlyList<{page.Name}Vo>>.Success(await service.SelectionSelected{page.Source.Id}Async(request, Actor(\"list\"), cancellationToken));\n";
}
