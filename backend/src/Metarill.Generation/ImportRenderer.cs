using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>兼容客户端解析 Excel 后的中文标题 JSON 行，写入复用同一 Service 规则。</summary>
internal static class ImportRenderer
{
    /// <summary>普通导入由页面开关启用，定制导入必须另行实现明确业务入口。</summary>
    public static bool Enabled(PageModel page) => page.Source.IsImport == 1 || !string.IsNullOrWhiteSpace(Mode(page));
    /// <summary>只有私有生成导入调用携带真实动作，客户端不能绕过多选或级联的目标权限。</summary>
    internal static bool RelationOperationAware(GenerationModel model,TableModel table,PageModel page)=>Enabled(page)&&DatabaseCheckboxCompilation.ForService(model,table,page).Count>0||CascadeTreeExcelRenderer.OperationAware(model,table,page)||Enabled(page)&&table.Source.IsRongyu==0&&MultiSelectCompilation.ForService(model,table,page).Count>0;
    /// <summary>读取已声明导入策略，默认追加。</summary>
    public static string Mode(PageModel page) => page.Source.Extra.TryGetValue("importMethod", out var value) ? value.ToString() : "";
    /// <summary>Excel只取原显式编辑列；父键由页面上下文绑定，不接受覆盖。</summary>
    public static IReadOnlyList<ColumnModel> Columns(PageModel page)
    {
        // 普通表单Writable含隐式级联ID，不能因此向用户模板追加未配置字段。
        var declared=page.DeclaredWritable.Select(column=>column.Source.Id).ToHashSet(StringComparer.Ordinal);
        return page.Writable.Where(column=>declared.Contains(column.Source.Id)&&!ManagerCredentialCompilation.IsObsolete(column)&&!page.Scopes.Any(scope=>scope.Source=="parent"&&scope.Column==column)).ToArray();
    }
    /// <summary>生成保留 requestJson 与 parentId 的独立请求契约。</summary>
    public static string Dto(PageModel page) => !Enabled(page) ? "" : $$"""
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed partial class {{page.Name}}ImportRequest
{
    public JsonElement RequestJson { get; set; }
    public string? ParentId { get; set; }
}

""";
    /// <summary>生成原 V8 精确导入路由，权限不能继承普通新增权限。</summary>
    public static string Controller(PageModel page) => !Enabled(page) ? "" : $$"""
    /// <summary>沿用 Excel 前端提交的中文标题 JSON 批次协议。</summary>
    [HttpPost("importDatas{{page.Source.Id}}")]
    public async Task<R<string>> Import([FromBody] {{page.Name}}ImportRequest request, CancellationToken cancellationToken) =>
        R<string>.Success(await service.Import{{page.Source.Id}}Async(request, Actor("import"), cancellationToken));

""";
    /// <summary>所有行共享真实事务，覆盖匹配严格限定当前页面范围。</summary>
    public static string Service(GenerationModel model, TableModel table, PageModel page)
    {
        if (!Enabled(page)) return "";
        if (Mode(page) == "custom") throw new InvalidOperationException("定制导入需要手写 Business 入口，不能自动生成普通导入。");
        var columns = Columns(page);
        var crosses=columns.Where(column=>column.Source.ControlKind=="CascadeTree").Select(column=>CrossTableWriteCompilation.Resolve(model,table,page,column).Definition).OfType<CrossTableWriteModel>().ToArray();
        var hasCross=crosses.Length>0;
        var dependency = ImportDependencyCompilation.ForPage(model, table, page);
        var contextual = dependency.Contextual;
        var fileImport = BackendRenderer.HasPhysicalFileColumns(model, table)||ExactDataManageCompilation.Declared(page)||ImportRenderer.RelationOperationAware(model,table,page);
        if (columns.Select(Header).Distinct(StringComparer.Ordinal).Count() != columns.Count) throw new InvalidOperationException("导入列中文标题重复，无法明确解析。");
        var overwrite = Mode(page) == "OverWriteByFirstColumn";
        // 校验与下方实际选中的表级首列一致，避免页面排序不同漏过数组匹配门禁。
        if (overwrite && columns.Count > 0 && table.Columns.First(column => columns.Contains(column)).IsJsonArray) throw new InvalidOperationException("覆盖匹配字段不能为数组。");
        if (overwrite && columns.Count == 0) throw new InvalidOperationException("覆盖导入至少需要一个可导入字段。");
        var result = new StringBuilder().AppendLine("    /// <summary>任意行失败时撤销整批；单行复用标准新增或明确字段更新。</summary>")
            .AppendLine($"    public Task<string> Import{page.Source.Id}Async({page.Name}ImportRequest request, RuntimeActor actor, CancellationToken cancellationToken) => session.InTransactionAsync(async () =>")
            .AppendLine("    {").AppendLine("        ArgumentNullException.ThrowIfNull(request);")
            .Append(BackendRenderer.PrepareCall(model, table, page, "Import", "null", null, "request.ParentId"))
            .AppendLine("        if (request.RequestJson.ValueKind != JsonValueKind.Array) throw PageImportException.Input(PageImportError.RowsArray);")
            .AppendLine($"        await ValidateParents{page.Source.Id}Async(request.ParentId, actor, cancellationToken{NonRedundantCrossParentCompilation.ParentArgument(model,table,page)});")
            .AppendLine("        var rowNumber = 0;")
            .AppendLine("        foreach (var row in request.RequestJson.EnumerateArray())").AppendLine("        {").AppendLine("            rowNumber++;").AppendLine("            try").AppendLine("            {");
        if (contextual && overwrite) result.AppendLine($"                {table.EntityType}? importExisting = null;");
        // 覆盖先匹配并锁定原对象，再解析其余列；旧父与默认值只作候选上下文，不作为提交字段。
        var parsed = contextual && overwrite ? "keyValues" : "values";
        var initialContext = contextual ? ", null, " + (overwrite ? "true" : "false") : "";
        result.AppendLine($"                var {parsed} = await ParseImport{page.Source.Id}Async(row, request.ParentId, actor{initialContext}, cancellationToken);");
        if (overwrite)
        {
            var first = table.Columns.First(column => columns.Contains(column));
            result.AppendLine($"                if (!{parsed}.TryGetValue({Literal(first.JsonName)}, out var matchValue) || matchValue.ValueKind == JsonValueKind.Null) throw new ArgumentException(\"覆盖匹配字段不能为空。\");")
                .AppendLine($"                var key = JsonSerializer.Deserialize<{first.BoundaryType}>(matchValue.GetRawText(), JsonOptions);")
                .AppendLine($"                var existing = await Scope{page.Source.Id}(actor, request.ParentId{ExactDataManageCompilation.Argument(page,"\"import\"")}{NonRedundantCrossParentCompilation.LockArgument(model,table,page)}).Where(entity => entity.{first.Property} == key).ForUpdate().Limit(2).ToListAsync(cancellationToken);")
                .AppendLine("                if (existing.Count > 1) throw new InvalidOperationException(\"覆盖匹配字段重名，不能确定目标记录。\");");
            if(contextual)result.AppendLine("                if (existing.Count == 1) importExisting = existing[0];")
                .AppendLine($"                var values = await ParseImport{page.Source.Id}Async(row, request.ParentId, actor, importExisting, false, cancellationToken);");
            result.AppendLine("                if (existing.Count == 1)").AppendLine("                {")
                .AppendLine("                    values[\"id\"] = JsonSerializer.SerializeToElement(existing[0].Id);")
                .AppendLine($"                    var update = JsonSerializer.Deserialize<{page.Name}UpdateRequest>(JsonSerializer.Serialize(values), JsonOptions)!;")
                .AppendLine($"                    await Update{page.Source.Id}{(fileImport?"Core":"")}Async(update, request.ParentId, actor{(fileImport?", RecordMutationOperation.Import":"")}, cancellationToken);")
                .AppendLine("                    continue;").AppendLine("                }");
        }
        result.AppendLine($"                var create = JsonSerializer.Deserialize<{page.Name}CreateRequest>(JsonSerializer.Serialize(values), JsonOptions)!;")
            .AppendLine($"                await Create{page.Source.Id}{(fileImport?"Core":"")}Async(create, request.ParentId, actor{(fileImport?", RecordMutationOperation.Import":"")}, cancellationToken);")
            .AppendLine("            }").AppendLine("            catch (PageImportException error) when (error.IsInputError)").AppendLine("            {")
            .AppendLine("                throw error.AtRow(rowNumber);").AppendLine("            }")
            .AppendLine("            catch (Exception error) when (error is not OperationCanceledException and not UnauthorizedAccessException and not System.Security.SecurityException)").AppendLine("            {")
            .AppendLine("                throw PageImportException.Execution(rowNumber, error);").AppendLine("            }").AppendLine("        }")
            .Append(NonRedundantCrossParentCompilation.Enabled(model,table,page)?$"        await ValidateParents{page.Source.Id}Async(request.ParentId,actor,cancellationToken);\n":"")
            .AppendLine("        return $\"导入成功{rowNumber}条数据\";").AppendLine("    }, cancellationToken);")
            .AppendLine("    /// <summary>仅按当前页已开放的中文标题读取单元格，未知、系统和父键列均拒绝。</summary>")
            .AppendLine($"    private async Task<Dictionary<string, JsonElement>> ParseImport{page.Source.Id}Async(JsonElement row, string? parentId, RuntimeActor actor{(contextual?", "+table.EntityType+"? existing, bool keyOnly":"")}, CancellationToken cancellationToken)")
            .AppendLine("    {").AppendLine("        await Task.CompletedTask;")
            .AppendLine("        if (row.ValueKind != JsonValueKind.Object) throw PageImportException.Input(PageImportError.RowObject);")
            .AppendLine("        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);")
            .AppendLine("        var seen = new HashSet<string>(StringComparer.Ordinal);");
        if(hasCross)
        {
            result.AppendLine("        var crossInputs=new Metarill.Core.Cascades.CrossTableExcelRow();");
            var slots=crosses.SelectMany(cross=>cross.Levels).Select(level=>level.Id.Source.Id).ToHashSet(StringComparer.Ordinal);
            foreach(var scope in page.Scopes.Where(scope=>slots.Contains(scope.Column.Source.Id)))
                result.AppendLine($"        values[{Literal(scope.Column.JsonName)}]=JsonSerializer.SerializeToElement({ScopeValue(scope.Column,scope.Source=="parent"?"parentId!":"actor.ProjectId")},JsonOptions);");
        }
        if (contextual)
        {
            result.AppendLine("        var relationContext = new Dictionary<string, JsonElement>(StringComparer.Ordinal);");
            foreach(var parent in dependency.ContextValues)
            {
                var fallback=string.IsNullOrWhiteSpace(parent.Source.DefaultValue)?$"default({parent.Type})":BackendRenderer.DefaultExpression(parent);
                var value="existing is null ? "+fallback+" : existing."+parent.Property;
                if(parent.Source.ColumnName=="yz_project_id")value=ScopeValue(parent,"actor.ProjectId");
                else if(page.Scopes.Any(scope=>scope.Source=="parent"&&scope.Column.Source.Id==parent.Source.Id))value=ScopeValue(parent,"parentId!");
                result.AppendLine($"        relationContext[{Literal(parent.JsonName)}] = JsonSerializer.SerializeToElement({value}, JsonOptions);");
            }
            result.AppendLine("        // 先验证整行标题，再按固定拓扑顺序解析，JSON属性顺序不决定目标父。")
                .AppendLine("        foreach (var header in row.EnumerateObject())")
                .AppendLine("        {")
                .AppendLine("            if (!seen.Add(header.Name)) throw PageImportException.Input(PageImportError.DuplicateHeader);")
                .AppendLine("            if (header.Name is not ("+string.Join(" or ",columns.Select(column=>Literal(Header(column))))+")) throw PageImportException.Input(PageImportError.UnknownHeader);")
                .AppendLine("        }");
            var order=string.Join(", ",dependency.Order.Select((column,index)=>Literal(Header(column))+" => "+index));
            result.AppendLine("        foreach (var cell in row.EnumerateObject().OrderBy(cell => cell.Name switch { "+order+", _ => int.MaxValue }))").AppendLine("        {");
        }
        else result.AppendLine("        foreach (var cell in row.EnumerateObject())").AppendLine("        {")
            .AppendLine("            if (!seen.Add(cell.Name)) throw PageImportException.Input(PageImportError.DuplicateHeader);");
        result.AppendLine("            switch (cell.Name)").AppendLine("            {");
        foreach (var column in columns)
        {
            result.AppendLine($"                case {Literal(Header(column))}:").AppendLine("                {");
            if(contextual&&!dependency.MatchFields.Contains(column.Source.Id))result.AppendLine("                    if (keyOnly) break;");
            var relation = RelationCompilation.ForPage(model, page).SingleOrDefault(candidate => candidate.SourceColumn == column);
            var multi=MultiSelectCompilation.ForPage(model,table,page).SingleOrDefault(multi=>multi.Source.Source.Id==column.Source.Id);
            var checkbox=DatabaseCheckboxCompilation.ForPage(model,table,page).SingleOrDefault(def=>def.Source.Source.Id==column.Source.Id);
            if(checkbox is not null)
            {
                result.AppendLine("                    try{")
                    .AppendLine($"                        values[{Literal(column.JsonName)}]=await ImportCheckbox{page.Source.Id}_{column.Source.Id}Async(cell.Value,parentId,actor{(contextual?",relationContext":"")},cancellationToken);")
                    .AppendLine("                    }catch(PageImportException error)when(error.IsInputError){")
                    .AppendLine($"                        throw PageImportException.Input(error.Code!.Value,error.RowNumber,{Literal(Header(column))},error);")
                    .AppendLine("                    }");
            }
            else if(multi is not null)
            {
                result.AppendLine("                    try").AppendLine("                    {")
                    .AppendLine($"                        values[{Literal(column.JsonName)}]=await ImportMulti{page.Source.Id}_{column.Source.Id}Async(cell.Value,parentId,actor{(contextual?", relationContext":"")},cancellationToken);")
                    .AppendLine("                    }").AppendLine("                    catch(PageImportException error) when(error.IsInputError)").AppendLine("                    {")
                    .AppendLine($"                        throw PageImportException.Input(error.Code!.Value,error.RowNumber,{Literal(Header(column))},error);").AppendLine("                    }");
            }
            else if(column.Source.ControlKind=="CascadeTree")
            {
                var cross=CrossTableWriteCompilation.Resolve(model,table,page,column).Definition;
                result.AppendLine("                    try").AppendLine("                    {")
                    .AppendLine(cross is null?$"                        values[{Literal(column.JsonName)}]=await ImportCascade{page.Source.Id}_{column.Source.Id}Async(cell.Value,actor,cancellationToken);":
                        $"                        crossInputs.Add({Literal(Header(column))},await ImportCascade{page.Source.Id}_{column.Source.Id}Async(cell.Value,actor,cancellationToken),cell.Value.ValueKind==JsonValueKind.Null||cell.Value.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(cell.Value.GetString()));")
                    .AppendLine("                    }").AppendLine("                    catch(PageImportException error) when(error.IsInputError)").AppendLine("                    {")
                    .AppendLine($"                        throw PageImportException.Input(error.Code!.Value,error.RowNumber,{Literal(Header(column))},error);").AppendLine("                    }");
            }
            else if (relation is not null)
            {
                // 数据库和授权异常不能被当成单元格类型错误；只为明确的无匹配分类补上已发布标题。
                result.AppendLine("                    try").AppendLine("                    {")
                    .AppendLine($"                        values[{Literal(column.JsonName)}] = await ImportRelation{page.Source.Id}_{column.Source.Id}Async(cell.Value, parentId, actor{(contextual?", relationContext":"")}, cancellationToken);")
                    .AppendLine("                    }").AppendLine("                    catch (PageImportException error) when (error.IsInputError)").AppendLine("                    {")
                    .AppendLine($"                        throw PageImportException.Input(error.Code!.Value, error.RowNumber, {Literal(Header(column))}, error);")
                    .AppendLine("                    }");
            }
            else if (column.Source.ControlKind is "List" or "SelectPage" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "CascadeTree")
            {
                result.AppendLine("                    throw new NotSupportedException(\"此关系控件尚无安全的导入候选规则。\");").AppendLine("                }");
                continue;
            }
            else
            {
                // 先在本列内用与DTO一致的类型/JSON选项核验，避免整DTO反序列化后只剩属性路径、丢中文列名。
                result.AppendLine("                    try").AppendLine("                    {")
                    .AppendLine($"                        var normalized = {CellExpression(column)};")
                    .AppendLine($"                        _ = JsonSerializer.Deserialize<{column.BoundaryType}>(normalized.GetRawText(), JsonOptions);");
                if (column.IsJsonArray) result.AppendLine("                        if (normalized.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null)) throw PageImportException.Input(PageImportError.Array);");
                result.AppendLine($"                        values[{Literal(column.JsonName)}] = normalized;")
                    .AppendLine("                    }").AppendLine("                    catch (PageImportException error) when (error.IsInputError)").AppendLine("                    {")
                    .AppendLine($"                        throw PageImportException.Input(error.Code!.Value, error.RowNumber, {Literal(Header(column))}, error);")
                    .AppendLine("                    }").AppendLine("                    catch (Exception error) when (error is JsonException or FormatException or OverflowException)").AppendLine("                    {")
                    .AppendLine($"                        throw PageImportException.Input(PageImportError.{ErrorKind(column)}, columnName: {Literal(Header(column))}, inner: error);")
                    .AppendLine("                    }");
            }
            if(contextual&&hasCross&&column.Source.ControlKind=="CascadeTree"&&CrossTableWriteCompilation.Resolve(model,table,page,column).Definition is not null)
            {
                result.AppendLine("                    var crossContext=crossInputs.Merge(values);");
                foreach(var parent in dependency.ContextValues)
                    result.AppendLine($"                    if(crossContext.TryGetValue({Literal(parent.JsonName)},out var crossValue{parent.Source.Id}))relationContext[{Literal(parent.JsonName)}]=crossValue{parent.Source.Id};");
            }
            else if(contextual&&dependency.ContextValues.Any(parent=>parent.Source.Id==column.Source.Id))result.AppendLine($"                    relationContext[{Literal(column.JsonName)}] = values[{Literal(column.JsonName)}];");
            result.AppendLine("                    break;").AppendLine("                }");
        }
        result.AppendLine("                default: throw PageImportException.Input(PageImportError.UnknownHeader);")
            .AppendLine("            }").AppendLine("        }").AppendLine(hasCross?"        return crossInputs.Merge(values);":"        return values;").AppendLine("    }");
        foreach (var relation in RelationCompilation.ForPage(model, page).Where(relation => columns.Contains(relation.SourceColumn))) result.Append(RelationResolver(model,relation, page, dependency));
        foreach(var checkbox in DatabaseCheckboxCompilation.ForPage(model,table,page).Where(def=>columns.Any(column=>column.Source.Id==def.Source.Source.Id)))result.Append(DatabaseCheckboxExcelRenderer.Import(table,page,checkbox,dependency));
        foreach(var source in columns.Where(column=>column.Source.ControlKind=="CascadeTree"))result.Append(CascadeTreeExcelRenderer.Import(model,table,page,source));
        foreach(var multi in MultiSelectCompilation.ForPage(model,table,page).Where(multi=>columns.Any(column=>column.Source.Id==multi.Source.Source.Id)))result.Append(MultiSelectExcelRenderer.Import(model,table,page,multi,dependency));
        return result.ToString();
    }
    /// <summary>候选反查复用目标项目及安全关系范围，重名和不存在均报错。</summary>
    private static string RelationResolver(GenerationModel model,RelationModel relation, PageModel page, ImportDependencyPlan dependency)
    {
        if(DerivedRelationNamesCompilation.Declared(model,relation))return DerivedRelationNamesRenderer.Import(relation,page,dependency);
        var result = new StringBuilder().AppendLine("    /// <summary>Excel 关系名称必须唯一落在已授权候选范围，数字 ID 也核对归属。</summary>")
            .AppendLine($"    private async Task<JsonElement> ImportRelation{page.Source.Id}_{relation.SourceColumn.Source.Id}Async(JsonElement cell, string? parentId, RuntimeActor actor{(dependency.Contextual?", IReadOnlyDictionary<string, JsonElement> relationContext":"")}, CancellationToken cancellationToken)")
            .AppendLine("    {").AppendLine("        if (cell.ValueKind == JsonValueKind.Null || string.IsNullOrWhiteSpace(cell.ToString())) return JsonSerializer.SerializeToElement<object?>(null);");
        var bound = relation.ParentBinding?.Declared == true;
        var mapped = dependency.Parents.GetValueOrDefault(relation.SourceColumn.Source.Id);
        if (bound || relation.Conditions is not null || relation.TargetPage.Condition is not null || relation.SourceColumn.Source.ControlKind=="SelectPage" || mapped is not null)
        {
            // 来源已按import授权；固定源父/目标父不能混用，名称匹配前先复用目标授权和服务器推导范围。
            // 无效声明同样走固定RejectBound，绝不回退宽查询；普通未绑定关系保持原合同。
            if(mapped is not null)result.AppendLine($"        if (!relationContext.TryGetValue({Literal(mapped.JsonName)}, out var targetParent) || targetParent.ValueKind == JsonValueKind.Null || string.IsNullOrWhiteSpace(targetParent.ToString())) throw PageImportException.Input(PageImportError.Relation);");
            result.AppendLine($"        var query = await OptionsScope{page.Source.Id}_{relation.SourceColumn.Source.Id}Async(parentId, {(mapped is null?"null":"targetParent.ToString()")}, actor, cancellationToken, false, false{RelationConditionsCompilation.ImportArgument(relation)});");
        }
        else
        {
            result.AppendLine($"        var query = session.Select<{relation.Target.EntityType}>();");
            if (relation.Target.Columns.Any(column => column.Source.ColumnName == "delete_no")) result.AppendLine("        query = query.Where(entity => entity.DeleteNo == null);");
            var project = relation.Target.Columns.SingleOrDefault(column => column.Source.ColumnName == "yz_project_id");
            if (project is not null && (!relation.Target.IsGlobal || relation.TargetPage.Scopes.Concat(relation.Scopes).Any(scope => scope.Source == "project")))
                result.AppendLine($"        query = query.Where(entity => entity.{project.Property} == {ScopeValue(project, "actor.ProjectId")});");
            foreach (var (column, source) in relation.TargetPage.Scopes.Concat(relation.Scopes).Where(scope => scope.Source == "parent"))
                result.AppendLine("        if (string.IsNullOrWhiteSpace(parentId)) throw new ArgumentException(\"关系候选要求父记录上下文。\");")
                    .AppendLine($"        query = query.Where(entity => entity.{column.Property} == {ScopeValue(column, "parentId")});");
        }
        result.AppendLine("        if (cell.ValueKind == JsonValueKind.Number)").AppendLine("        {")
            .AppendLine("            long id; try { id = cell.GetInt64(); } catch (Exception error) when (error is FormatException or OverflowException) { throw PageImportException.Input(PageImportError.Number, inner: error); }")
            .AppendLine("            query = query.Where(entity => entity.Id == id);").AppendLine("        }")
            .AppendLine("        else").AppendLine("        {").AppendLine("            var name = cell.ToString().Trim();")
            .AppendLine($"            query = query.Where(entity => entity.{relation.NameColumn.Property} == name);").AppendLine("        }")
            .AppendLine(bound ? "        var candidates = await query.Limit(2).ToListAsync(entity => new { entity.Id }, cancellationToken);" : "        var candidates = await query.Limit(2).ToListAsync(cancellationToken);")
            .AppendLine("        if (candidates.Count != 1) throw PageImportException.Input(PageImportError.Relation);")
            .AppendLine("        return JsonSerializer.SerializeToElement(candidates[0].Id.ToString(CultureInfo.InvariantCulture));").AppendLine("    }");
        return result.ToString();
    }
    /// <summary>规范化空值、日期、常量标签和数组单元格，再交给生成 DTO 校验。</summary>
    private static string CellExpression(ColumnModel column)
    {
        if (column.Source.ControlKind is "ConstList" or "ConstDownList")
        {
            var choices = column.Source.ControlDetail.Split(',').Select(token => token.Split('|', 2)).Where(pair => pair.Length == 2).ToArray();
            if (choices.Length == 0) throw new InvalidOperationException("常量导入选项未配置：" + column.Source.Name);
            var cases = choices.SelectMany(pair => new[] { pair[0], pair[1] }.Distinct().Select(label => (label, value: pair[1]))).GroupBy(pair => pair.label, StringComparer.Ordinal).Select(group => group.First());
            return "cell.Value.ValueKind == JsonValueKind.Null || string.IsNullOrWhiteSpace(cell.Value.ToString()) ? JsonSerializer.SerializeToElement<object?>(null) : cell.Value.ToString() switch { "
                + string.Join(", ", cases.Select(pair => Literal(pair.label) + " => " + (column.Type == "string?" ? "JsonSerializer.SerializeToElement(" + Literal(pair.value) + ")" : "JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<" + column.Type + ">(" + Literal(JsonSerializer.Serialize(pair.value)) + ", JsonOptions))")))
                + ", _ => throw PageImportException.Input(PageImportError.Constant) }";
        }
        if(column.Type=="byte[]?")return "cell.Value.Clone()";
        if (column.IsJsonArray) return "cell.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(cell.Value.GetString()) ? JsonSerializer.Deserialize<JsonElement>(cell.Value.GetString()!) : cell.Value.Clone()";
        if (column.Type == "string?") return "cell.Value.ValueKind == JsonValueKind.Null ? JsonSerializer.SerializeToElement<object?>(null) : JsonSerializer.SerializeToElement(cell.Value.ToString())";
        if (column.Type == "bool?") return "cell.Value.ValueKind == JsonValueKind.Null || string.IsNullOrWhiteSpace(cell.Value.ToString()) ? JsonSerializer.SerializeToElement<object?>(null) : cell.Value.ToString().ToLowerInvariant() switch { \"1\" or \"true\" or \"是\" or \"男\" => JsonSerializer.SerializeToElement(true), \"0\" or \"false\" or \"否\" or \"女\" => JsonSerializer.SerializeToElement(false), _ => throw PageImportException.Input(PageImportError.Boolean) }";
        if(column.Type=="TimeSpan?")return $"JsonSerializer.SerializeToElement({TimeFieldCompilation.Value(column)}.Parse(cell.Value),JsonOptions)";
        if (column.Type == "DateTime?") return "cell.Value.ValueKind == JsonValueKind.Null || string.IsNullOrWhiteSpace(cell.Value.ToString()) ? JsonSerializer.SerializeToElement<object?>(null) : JsonSerializer.SerializeToElement(DateTime.Parse(cell.Value.ToString(), CultureInfo.InvariantCulture))";
        return "cell.Value.ValueKind == JsonValueKind.Null || string.IsNullOrWhiteSpace(cell.Value.ToString()) ? JsonSerializer.SerializeToElement<object?>(null) : cell.Value.Clone()";
    }
    /// <summary>类型错误只映射固定公开分类，原异常消息不进入返回值。</summary>
    private static string ErrorKind(ColumnModel column) => column.Source.ControlKind is "ConstList" or "ConstDownList" ? "Constant" : column.IsJsonArray ? "Array" : column.Type switch { "byte[]?"=>"Binary", "bool?" => "Boolean", "DateTime?" or "TimeSpan?" => "DateTime", _ => "Number" };
    /// <summary>Excel 使用中文标题，空标题回退为稳定字段名。</summary>
    internal static string Header(ColumnModel column) => string.IsNullOrWhiteSpace(column.Source.Name) ? column.JsonName : column.Source.Name;
    /// <summary>安全生成 C# 字符串字面量。</summary>
    private static string Literal(string value) => JsonSerializer.Serialize(value);
    /// <summary>导入候选与标准写入共用规范整数身份，字符串范围不保留前导零别名。</summary>
    private static string ScopeValue(ColumnModel column, string value) => column.Type switch { "string?" => $"long.Parse({value}, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)", "long?" or "long" => $"long.Parse({value}, CultureInfo.InvariantCulture)", "int?" => $"int.Parse({value}, CultureInfo.InvariantCulture)", _ => throw new InvalidOperationException("关系范围字段类型无效。") };
}
