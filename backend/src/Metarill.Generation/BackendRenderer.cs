using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

/// <summary>生成固定类型访问与页级白名单，运行时不解释元数据。</summary>
internal static partial class BackendRenderer
{
    /// <summary>生成安全的 C# 字面量。</summary>
    private static string Literal(string value) => JsonSerializer.Serialize(value);
    /// <summary>汇总整个生成批次的强类型后端文件。</summary>
    public static IEnumerable<PlannedFile> Render(GenerationModel model)
    {
        foreach (var table in model.Tables)
        {
            yield return new($"Entities/{table.Name}.g.cs", Entity(model, table));
            yield return new($"Mappers/{table.Name}Mapper.g.cs", Mapper(model, table));
            yield return new($"Services/{table.Name}Service.g.cs", Service(model, table));
            yield return new($"Services/{table.Name}Service.cs", HandwrittenService(model, table), true);
            foreach (var page in table.Pages)
            {
                yield return new($"DTOs/{page.Name}Requests.g.cs", Dto(model, table, page));
                yield return new($"VOs/{page.Name}Vo.g.cs", Vo(model, table, page));
                yield return new($"Controllers/{page.Name}Controller.g.cs", Controller(model, table, page));
            }
        }
        yield return new("GeneratedRegistration.g.cs", Registration(model)); yield return new("VOs/GeneratedOption.g.cs", CSharpGenerator.Header + $"namespace {model.Namespace}.Generated.VOs;\npublic sealed record GeneratedOption(long Id, string? Name, long Value, string? Text);\n");
    }
    /// <summary>根据物理字段生成持久化实体。</summary>
    private static string Entity(GenerationModel model, TableModel table)
    {
        var result = new StringBuilder(CSharpGenerator.Header).AppendLine("using FreeSql.DataAnnotations;").AppendLine("using System.Text.Json.Serialization;")
            .AppendLine($"namespace {model.Namespace}.Generated.Entities;").AppendLine($"[Table(Name = {Literal(table.Source.TableName)})]")
            .AppendLine($"public partial class {table.Name}").AppendLine("{");
        foreach (var column in table.Columns)
        {
            result.AppendLine($"    [Column(Name = {Literal(column.Source.ColumnName)}, IsPrimary = {column.IsId.ToString().ToLowerInvariant()}, IsIdentity = false, IsNullable = {(column.Source.IsDbNotNull == 0 && !column.IsId).ToString().ToLowerInvariant()})]")
                .AppendLine($"    [JsonPropertyName({Literal(column.JsonName)})]")
                .AppendLine($"    public {column.Type} {column.Property} {{ get; set; }}");
        }
        return result.AppendLine("}").ToString();
    }
    /// <summary>生成只接受页面可写字段的请求。</summary>
    private static string Dto(GenerationModel model, TableModel table, PageModel page)
    {
        var result = new StringBuilder(CSharpGenerator.Header).AppendLine("using System.Text.Json;").AppendLine("using System.Text.Json.Serialization;").AppendLine("using Metarill.Core.Contracts;")
            .AppendLine($"namespace {model.Namespace}.Generated.DTOs;");
        foreach (var kind in new[] { "Create", "Update" })
        {
            result.AppendLine("[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]").AppendLine($"public sealed class {page.Name}{kind}Request").AppendLine("{");
            if (kind == "Update") result.AppendLine("    public long Id { get; set; }");
            foreach (var column in ManagerCredentialCompilation.Writable(table,page))
            {
                result.AppendLine($"    [JsonPropertyName({Literal(column.JsonName)})]");
                if (Checkbox(column)) result.AppendLine("    [JsonConverter(typeof(CheckboxBooleanJsonConverter))]");
                result.AppendLine($"    public Optional<{column.BoundaryType}> {column.Property} {{ get; set; }}");
            }
            result.AppendLine("}");
        }
        result.AppendLine("[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]").AppendLine($"public sealed class {page.Name}RecordRequest {{ public long Id {{ get; set; }} }}");
        return result.Append(ImportRenderer.Dto(page)).ToString();
    }
    /// <summary>生成页面可读投影、数组控件及原前端使用的常量展示字段。</summary>
    private static string Vo(GenerationModel model, TableModel table, PageModel page)
    {
        var role = FixedRoleCheckboxCompilation.ForPage(model,table,page);
        var roleAdmin = FixedRoleAdminNameCompilation.ForPage(model,table,page);
        var credentials = ManagerCredentialCompilation.IsOriginal(table,page);
        var catalogs = FixedOptionCatalogCompilation.ForPage(model, page).Where(item => page.Readable.Contains(item.Source)).ToArray();
        var constants = page.Readable.Where(column => column.Source.ControlKind is "ConstList" or "ConstDownList").ToArray();
        // 展示别名不能覆盖真实业务字段；在写文件前拒绝整个生成批次。
        foreach (var column in constants)
            if (page.Readable.Any(candidate => candidate.Property.Equals(column.Property + "Show", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("常量展示字段与页面字段重名：" + column.JsonName + "Show");
        var result = new StringBuilder(CSharpGenerator.Header).Append(constants.Length == 0 ? "" : "using System.Globalization;\n").AppendLine("using System.Text.Json;").AppendLine("using System.Text.Json.Serialization;").AppendLine($"using {model.Namespace}.Generated.Entities;")
            .AppendLine($"namespace {model.Namespace}.Generated.VOs;").AppendLine($"public sealed class {page.Name}Vo").AppendLine("{");
        foreach (var column in page.Readable)
        {
            if (ManagerCredentialCompilation.IsCredential(table,column)) result.AppendLine("    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]");
            result.AppendLine($"    [JsonPropertyName({Literal(column.JsonName)})]");
            if (Checkbox(column)) result.AppendLine("    [JsonConverter(typeof(Metarill.Core.Contracts.CheckboxBooleanJsonConverter))]");
            result.AppendLine($"    public {column.BoundaryType} {column.Property} {{ get; init; }}");
        }
        foreach (var catalog in catalogs)
            result.AppendLine($"    [JsonPropertyName({Literal(catalog.JsonName)})]").AppendLine($"    public string? {catalog.Property} {{ get; init; }}");
        if (role is not null) result.AppendLine("    [JsonPropertyName(\"yzRoleNames\")] public JsonElement? YzRoleNames { get; init; }")
            .AppendLine($"    public static {page.Name}Vo FromEntity({table.Name} entity) => FromEntity(entity,null);");
        if (roleAdmin is not null) result.AppendLine("    [JsonPropertyName(\"yzAdminTypeName\")] public string? YzAdminTypeName { get; init; }")
            .AppendLine($"    public static {page.Name}Vo FromEntity({table.Name} entity) => FromEntity(entity,null);");
        if (credentials && role is not null) result.AppendLine($"    public static {page.Name}Vo FromEntity({table.Name} entity,JsonElement? roleNames) => FromEntity(entity,roleNames,true);");
        foreach (var column in constants)
            result.AppendLine($"    [JsonPropertyName({Literal(column.JsonName + "Show")})]").AppendLine($"    public string? {column.Property}Show {{ get; init; }}");
        result.AppendLine("    /// <summary>只投影本页声明可读字段；展示别名不参与写入。</summary>").AppendLine($"    public static {page.Name}Vo FromEntity({table.Name} entity{(role is not null ? ",JsonElement? roleNames" : roleAdmin is not null ? ",string? roleAdminName" : "")}{(credentials ? ",bool includeCredentials = true" : "")}) => new()").AppendLine("    {");
        foreach (var column in page.Readable) result.AppendLine(ManagerCredentialCompilation.Output(table,page,column) is {} credential ? $"        {column.Property} = {credential}," : role?.Source.Source.Id==column.Source.Id ? $"        {column.Property} = Metarill.Core.Contracts.RoleCheckboxValue.ProjectIds(entity.{column.Property})," : column.IsJsonArray ? $"        {column.Property} = string.IsNullOrWhiteSpace(entity.{column.Property}) ? null : JsonSerializer.Deserialize<JsonElement>(entity.{column.Property})," : $"        {column.Property} = entity.{column.Property},");
        if (role is not null) result.AppendLine("        YzRoleNames = roleNames,");
        if (roleAdmin is not null) result.AppendLine("        YzAdminTypeName = roleAdminName,");
        foreach (var catalog in catalogs) result.AppendLine($"        {catalog.Property} = Metarill.Core.Metadata.TemplateVersionCatalog.DisplayName(entity.{catalog.Source.Property}),");
        foreach (var column in constants) result.AppendLine($"        {column.Property}Show = {ConstantDisplayExpression(column)},");
        return result.AppendLine("    };").AppendLine("}").ToString();
    }
    /// <summary>只对已发布CheckBox布尔字段使用原0/1协议，其他bool API不改变。</summary>
    private static bool Checkbox(ColumnModel column) => column.Type == "bool?" && column.Source.ControlKind == "CheckBox";
    /// <summary>把常量标签预编译为类型匹配的 switch，未知值保留原文、空值保持为空。</summary>
    private static string ConstantDisplayExpression(ColumnModel column)
    {
        var choices = new List<(string Pattern, string Text)>();
        foreach (var token in column.Source.ControlDetail.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = token.Split('|', 2);
            var raw = parts.Length == 2 ? parts[1] : parts[0];
            // 数字控件的空选项表示未选中，不用提示文字替代空数据。
            if (raw.Length == 0 && column.Type != "string?") continue;
            var pattern = column.Type switch
            {
                "string?" => Literal(raw),
                "int?" => int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "long" or "long?" => long.Parse(raw, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture) + "L",
                "decimal?" => decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture).ToString("G29", System.Globalization.CultureInfo.InvariantCulture) + "M",
                _ => throw new InvalidOperationException("常量控件不支持该字段类型：" + column.Source.ColumnName)
            };
            if (!choices.Any(choice => choice.Pattern == pattern)) choices.Add((pattern, parts[0]));
        }
        var arms = choices.Select(choice => choice.Pattern + " => " + Literal(choice.Text)).ToList();
        if (column.Type != "long") arms.Insert(0, "null => null");
        arms.Add("var raw => Convert.ToString(raw, CultureInfo.InvariantCulture)");
        return "entity." + column.Property + " switch { " + string.Join(", ", arms) + " }";
    }
    /// <summary>生成加入当前事务的强类型数据库操作。</summary>
    private static string Mapper(GenerationModel model, TableModel table)
    {
        var deleted = FixedPageQueryCompilation.SoftDeleteColumn(table);
        return CSharpGenerator.Header + $$"""
using FreeSql;
using Metarill.Data;
using {{model.Namespace}}.Generated.Entities;
namespace {{model.Namespace}}.Generated.Mappers;

/// <summary>固定类型持久化，当前事务由 DatabaseSession 自动附加。</summary>
public sealed class {{table.Name}}Mapper(DatabaseSession session)
{
    /// <summary>创建仅包含未删除记录的查询起点。</summary>
    public ISelect<{{table.Name}}> Query() => session.Select<{{table.Name}}>(){{(deleted is not null ? $".Where(entity => entity.{deleted.Property} == null)" : "")}};
    /// <summary>插入当前服务已验证的记录。</summary>
    public Task<int> InsertAsync({{table.Name}} entity, CancellationToken cancellationToken) => session.Insert<{{table.Name}}>().AppendData(entity).ExecuteAffrowsAsync(cancellationToken);
    /// <summary>仅更新服务列出的提交字段，禁止实体全量覆盖。</summary>
    public Task<int> UpdateAsync({{table.Name}} entity, string[] columns, CancellationToken cancellationToken) =>
        session.Update<{{table.Name}}>().SetSource(entity).UpdateColumns(columns).Where(row => row.Id == entity.Id).ExecuteAffrowsAsync(cancellationToken);
    /// <summary>未声明逻辑删除字段的表执行物理删除。</summary>
    public Task<int> DeleteAsync(long id, CancellationToken cancellationToken) => session.Delete<{{table.Name}}>().Where(entity => entity.Id == id).ExecuteAffrowsAsync(cancellationToken);
}

""";
    }
    /// <summary>生成单对象生命周期与按页面隔离的规则。</summary>
    private static string Service(GenerationModel model, TableModel table)
    {
        var hasMulti = RequiresRelationAuthorization(model, table);
        var trustedSecurity=TrustedSecurityServiceCompilation.Applies(model,table);
        var result = new StringBuilder(CSharpGenerator.Header).AppendLine("using System.Globalization;").AppendLine("using System.Text.Json;").AppendLine("using FreeSql;")
            .AppendLine("using Metarill.Core.Contracts;").AppendLine("using Metarill.Core.Security;").AppendLine("using Metarill.Core.Persistence;").AppendLine("using Metarill.Data;").Append(trustedSecurity ? "using Metarill.Runtime.Security;\n" : "")
            .AppendLine($"using {model.Namespace}.Generated.Entities;").AppendLine(model.Tables.Any(candidate => candidate.Pages.Count != 0) ? $"using {model.Namespace}.Generated.DTOs;" : "")
            .AppendLine($"using {model.Namespace}.Generated.VOs;").AppendLine($"using {model.Namespace}.Generated.Mappers;")
            .AppendLine($"namespace {model.Namespace}.Generated.Services;").AppendLine($"public partial class {table.Name}Service").AppendLine("{").AppendLine("    private readonly DatabaseSession session;").AppendLine($"    private readonly {table.Name}Mapper mapper;").AppendLine("    private readonly IIdGenerator ids;").AppendLine("    private readonly IEnumerable<IRecordMutationObserver> observers;").AppendLine("    private readonly IEnumerable<IRecordMutationGuard> guards;").AppendLine(hasMulti ? "    private readonly IRuntimeAuthorization? relationAuthorization;" : "").AppendLine("    /// <summary>注入当前事务、类型持久化及可选的写入准备守卫。</summary>").AppendLine($"    public {table.Name}Service(DatabaseSession session, {table.Name}Mapper mapper, IIdGenerator ids, IEnumerable<IRecordMutationObserver> observers, IEnumerable<IRecordMutationGuard>? guards = null{(hasMulti ? ", IRuntimeAuthorization? relationAuthorization = null" : "")}) {{ this.session = session; this.mapper = mapper; this.ids = ids; this.observers = observers; {TrustedSecurityServiceCompilation.GuardsAssignment(model,table)} {(hasMulti ? "this.relationAuthorization = relationAuthorization;" : "")} }}")
            .AppendLine("    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();")
            .AppendLine("    /// <summary>查询筛选值沿用字符串 ID 协议。</summary>")
            .AppendLine("    private static JsonSerializerOptions CreateJsonOptions() { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); options.Converters.Add(new StringLongJsonConverter()); return options; }")
            .AppendLine("    /// <summary>服务层再次检查身份来源，不能用客户端项目选择代替真实身份。</summary>")
            .AppendLine("    private static void ValidateActor(RuntimeActor actor) { if (actor.AccountId <= 0 || string.IsNullOrWhiteSpace(actor.ProjectId)) throw new UnauthorizedAccessException(\"登录身份无效。\"); }")
            .AppendLine("    /// <summary>所有单对象写入共用手写校验和归一化钩子。</summary>")
            .AppendLine($"    {(trustedSecurity ? "private " : "")}partial void BeforeSave({table.Name} entity, RuntimeActor actor, bool creating, ISet<string> submittedFields);")
            .AppendLine("    /// <summary>删除前规则在当前事务内执行。</summary>")
            .AppendLine($"    {(trustedSecurity ? "private " : "")}partial void BeforeDelete({table.Name} entity, RuntimeActor actor);");
        foreach (var page in table.Pages)
        {
            result.Append(ForeignValidationRenderer.Render(model, table, page)); result.Append(ParentValidation(model, table, page)); result.Append(Query(model, table, page)); result.Append(Relations(model, page)); result.Append(SelectionPageRenderer.Service(model, page)); result.Append(MultiSelectFieldRenderer.Service(model, table, page));
            result.Append(FixedRoleCheckboxRenderer.Service(model,table,page));
            result.Append(FixedRoleAdminNameRenderer.Service(model,table,page));
            result.Append(BoundRelationValidationRenderer.Service(model, table, page)); result.Append(ExportRenderer.Service(model, table, page)); result.Append(ImportRenderer.Service(model, table, page)); result.Append(Mutations(model, table, page)); result.Append(Copy(model, table, page));
        }
        result.Append(Preparation(model, table)); result.Append(Observer(model, table)); return result.AppendLine("}").ToString();
    }
    /// <summary>新增父键必须对应真实存在且属于当前项目的父记录。</summary>
    private static string ParentValidation(GenerationModel model, TableModel table, PageModel page)
    {
        var result = new StringBuilder().AppendLine("    /// <summary>绑定父记录前验证真实归属，不能把客户端 parentId 当作授权。</summary>")
            .AppendLine($"    private async Task ValidateParents{page.Source.Id}Async(string? parentId, RuntimeActor actor, CancellationToken cancellationToken)")
            .AppendLine("    {");
        var number = 0;
        foreach (var (column, source) in page.Scopes.Where(scope => scope.Source == "parent"))
        {
            var parent = model.Tables.SingleOrDefault(candidate => !string.IsNullOrWhiteSpace(column.Source.LinkConfigTableId) ? candidate.Source.Id == column.Source.LinkConfigTableId : candidate.Source.TableName + "_id" == column.Source.ColumnName)
                ?? throw new InvalidOperationException("父范围字段缺少明确的目标表：" + column.Source.ColumnName);
            var project = parent.Columns.SingleOrDefault(candidate => candidate.Source.ColumnName == "yz_project_id");
            result.AppendLine("        if (string.IsNullOrWhiteSpace(parentId)) throw new ArgumentException(\"当前页面要求 parentId。\");");
            if (project is null) result.AppendLine("        throw new InvalidOperationException(\"父表未声明项目归属，当前页面需要明确的父记录访问规则。\");");
            else
            {
                number++;
                result.AppendLine($"        var parent{number} = session.Select<{parent.Name}>().Where(entity => entity.Id == long.Parse(parentId, CultureInfo.InvariantCulture)).Where(entity => entity.{project.Property} == {ScopeValue(project, "actor.ProjectId")});");
                if (parent.Columns.Any(candidate => candidate.Source.ColumnName == "delete_no")) result.AppendLine($"        parent{number} = parent{number}.Where(entity => entity.DeleteNo == null);");
                result.AppendLine($"        if (await parent{number}.ForUpdate().FirstAsync(cancellationToken) is null) throw new InvalidOperationException(\"父记录不存在或不属于当前项目。\");");
            }
        }
        if (!page.Scopes.Any(scope => scope.Source == "parent")) result.AppendLine("        await Task.CompletedTask;");
        return result.AppendLine("    }").ToString();
    }
    private static string Query(GenerationModel model, TableModel table, PageModel page)
    {
        var suffix = page.Source.Id;
        var fixedQuery = FixedPageQueryCompilation.For(table, page);
        var names = SelectionNameFilterCompilation.ForPage(model, table, page);
        var nameKeys = names.ConsumedKeys.Concat(names.Gaps.Select(gap => gap.FilterKey)).ToHashSet(StringComparer.Ordinal);
        var result = new StringBuilder().AppendLine($"    /// <summary>固定页面范围先进入 SQL，再执行明细、数量或分页查询。</summary>")
            .AppendLine($"    private ISelect<{table.Name}> Scope{suffix}(RuntimeActor actor, string? parentId)").AppendLine("    {")
            .AppendLine("        ValidateActor(actor);").AppendLine("        var query = mapper.Query();");
        foreach (var (column, source) in fixedQuery.IdentityScopes)
        {
            if (source == "project") result.AppendLine($"        query = query.Where(entity => entity.{column.Property} == {ScopeValue(column, "actor.ProjectId")});");
            else result.AppendLine("        if (string.IsNullOrWhiteSpace(parentId)) throw new ArgumentException(\"当前页面要求 parentId。\");")
                .AppendLine($"        query = query.Where(entity => entity.{column.Property} == {ScopeValue(column, "parentId")});");
        }
        if (ManagerCredentialCompilation.Unsupported(table,page)) result.AppendLine("        ManagerCredentialValue.RequirePublishedPage(false);");
        result.AppendLine("        return query;").AppendLine("    }")
            .AppendLine("    /// <summary>请求筛选只接受本页开放键；所有值通过表达式参数绑定。</summary>")
            .AppendLine($"    private ISelect<{table.Name}> Query{suffix}(PageRequest request, RuntimeActor actor)").AppendLine("    {")
            .AppendLine("        ArgumentNullException.ThrowIfNull(request);")
            .AppendLine($"        var query = Scope{suffix}(actor, request.ParentId);")
            .AppendLine("        foreach (var filter in request.Filters ?? throw new ArgumentException(\"filters 不能为空。\"))")
            .AppendLine("        {").AppendLine("            switch (filter.Key)").AppendLine("            {");
        // 成功名称谓词与缺口拒绝共用固定键集合，不能落入普通JSON文本LIKE，也不能重复生成switch case。
        result.Append(SelectionNameFilterCompilation.RenderCases(new(names.Bindings, []), binding =>
            $"query = Metarill.Data.Queries.JsonArrayNameQuery.ApplyJsonArrayAnyLike(session, query, entity => entity.{binding.StorageColumn.Property}, value);"));
        foreach (var gap in names.Gaps.GroupBy(gap => gap.FilterKey, StringComparer.Ordinal))
            result.AppendLine($"                case {Literal(gap.Key)}:")
                .AppendLine("                    if (filter.Value.ValueKind == JsonValueKind.Null || filter.Value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(filter.Value.GetString())) break;")
                .AppendLine($"                    throw new ArgumentException({Literal(string.Join("；", gap.Select(item => item.Reason).Distinct(StringComparer.Ordinal)))});");
        foreach (var column in page.Searchable.Where(column => !nameKeys.Contains(column.JsonName)))
        {
            result.AppendLine($"                case {Literal(column.JsonName)}:").AppendLine("                {")
                .AppendLine("                    if (filter.Value.ValueKind == JsonValueKind.Null || (filter.Value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(filter.Value.GetString())) || (filter.Value.ValueKind == JsonValueKind.Array && filter.Value.GetArrayLength() == 0)) break;");
            if (ManagerCredentialCompilation.IsCredential(table,column))
            { result.AppendLine("                    ManagerCredentialValue.RequirePublishedPage(false);"); }
            else if (ManagerCredentialCompilation.IsEnabled(table,page,column))
            {
                result.AppendLine("                    if (filter.Value.ValueKind == JsonValueKind.Array) { var values = filter.Value.EnumerateArray().Select(ManagerCredentialValue.Filter).ToArray(); query = query.Where(entity => values.Contains(entity.IsDisabled)); break; }")
                    .AppendLine("                    var value = ManagerCredentialValue.Filter(filter.Value); query = query.Where(entity => entity.IsDisabled == value);");
            }
            else if (column.Type == "DateTime?")
            {
                result.AppendLine("                    if (filter.Value.ValueKind == JsonValueKind.Array)").AppendLine("                    {")
                    .AppendLine("                        var range = PageDateRange.Parse(filter.Value); var start = range.Start; var end = range.End;")
                    .AppendLine("                        // 原Date/月范围的日期终点覆盖整天；带时分秒的午夜仍是精确时刻。")
                    .AppendLine($"                        query = range.EndExclusive ? query.Where(entity => entity.{column.Property} >= start && entity.{column.Property} < end) : query.Where(entity => entity.{column.Property} >= start && entity.{column.Property} <= end);")
                    .AppendLine("                        break;").AppendLine("                    }")
                    .AppendLine("                    var value = DateTime.Parse(filter.Value.GetString()!, CultureInfo.InvariantCulture);")
                    .AppendLine($"                    query = query.Where(entity => entity.{column.Property} == value);");
            }
            else
            {
                result.AppendLine("                    if (filter.Value.ValueKind == JsonValueKind.Array)").AppendLine("                    {")
                    .AppendLine(Checkbox(column) ? "                        var values = filter.Value.EnumerateArray().Select(CheckboxBooleanJsonConverter.Parse).ToArray();" : $"                        var values = JsonSerializer.Deserialize<{column.Type}[]>(filter.Value.GetRawText(), JsonOptions)!;")
                    .AppendLine($"                        query = query.Where(entity => values.Contains(entity.{column.Property}));")
                    .AppendLine("                        break;").AppendLine("                    }")
                    .AppendLine(Checkbox(column) ? "                    var value = CheckboxBooleanJsonConverter.Parse(filter.Value);" : $"                    var value = JsonSerializer.Deserialize<{column.Type}>(filter.Value.GetRawText(), JsonOptions);");
                var field = model.Catalog.ConfigPageColumns.Single(x => x.ConfigPageId == page.Source.Id && x.ConfigColumnId == column.Source.Id);
                var matchName = field.Extra.TryGetValue("searchType", out var match) ? match.ToString() : field.Extra.TryGetValue("matchType", out match) ? match.ToString() : "";
                var like = column.Type == "string?" && (matchName == "like" || (string.IsNullOrEmpty(matchName) && column.Source.ControlKind is not "List" and not "ConstList" and not "ConstDownList" and not "SelectPage"));
                result.AppendLine(like ? $"                    query = query.Where(entity => entity.{column.Property} != null && value != null && entity.{column.Property}.Contains(value));" : $"                    query = query.Where(entity => entity.{column.Property} == value);");
            }
            result.AppendLine("                    break;").AppendLine("                }");
        }        result.AppendLine("                default: throw new ArgumentException(\"页面未开放该筛选字段：\" + filter.Key);").AppendLine("            }").AppendLine("        }")
            .AppendLine("        if (request.SelectedIds is null) throw new ArgumentException(\"selectedIds 不能为空。\");")
            .AppendLine(SelectionPageRenderer.Enabled(page) ? "        if (request.SelectedIds.Count > 200 || request.PageNo > 1000000) throw new ArgumentException(\"多选页最多200个已选项，页码最多1000000。\");" : "")
            .AppendLine("        if (request.SelectedIds.Count > 0)").AppendLine("        {").AppendLine("            var selected = request.SelectedIds.Select(id => long.Parse(id, CultureInfo.InvariantCulture)).ToArray();")
            .AppendLine("            query = query.Where(entity => selected.Contains(entity.Id));").AppendLine("        }").AppendLine("        return query;").AppendLine("    }")
            .AppendLine("    /// <summary>排序只能引用已生成的开放列，方向不能成为 SQL 片段。</summary>")
            .AppendLine($"    private static ISelect<{table.Name}> Order{suffix}(ISelect<{table.Name}> query, PageRequest request)").AppendLine("    {")
            .AppendLine("        if (request.OrderBy is not null && !string.IsNullOrWhiteSpace(request.OrderBy.Order))").AppendLine("        {")
            .AppendLine("            var descending = request.OrderBy.Order switch { \"descending\" => true, \"ascending\" => false, _ => throw new ArgumentException(\"排序方向无效。\") };")
            .AppendLine("            return (request.OrderBy.Column ?? request.OrderBy.Field) switch").AppendLine("            {");
        foreach (var column in page.Sortable.Where(column=>!ManagerCredentialCompilation.IsCredential(table,column)))
            result.AppendLine($"                {Literal(column.JsonName)} => descending ? query.OrderByDescending(entity => {ManagerCredentialCompilation.Order(table,page,column)}).OrderByDescending(entity => entity.Id) : query.OrderBy(entity => {ManagerCredentialCompilation.Order(table,page,column)}).OrderByDescending(entity => entity.Id),");
        result.AppendLine("                _ => throw new ArgumentException(\"页面未开放该排序字段。\")").AppendLine("            };").AppendLine("        }");
        result.AppendLine("        return query" + string.Concat(fixedQuery.DefaultOrder.Select(item => $".{(item.Descending ? "OrderByDescending" : "OrderBy")}(entity => {ManagerCredentialCompilation.Order(table,page,item.Column)})")) + ";")
            .AppendLine("    }").AppendLine("    /// <summary>分页与统计使用同一条已受限查询。</summary>")
            .AppendLine($"    public async Task<PageResult<{page.Name}Vo>> Page{suffix}Async(PageRequest request, RuntimeActor actor, CancellationToken cancellationToken)").AppendLine("    {")
            .AppendLine("        if (request.PageNo < 1 || request.PageSize < 0 || request.PageSize > 1000) throw new ArgumentException(\"分页参数无效，每页最多 1000 条。\");")
            .AppendLine($"        var unpaged = request.PageSize == 0 && {(page.Source.PageSize == 0).ToString().ToLowerInvariant()};")
            .AppendLine("        if (request.PageSize == 0 && (!unpaged || request.PageNo != 1)) throw new ArgumentException(\"仅已配置不分页的页面允许 pageSize=0，且页码必须为1。\");")
            .AppendLine(SelectionPageRenderer.Enabled(page) ? $"        await ValidateSelectionParents{suffix}Async(request.ParentId, actor, cancellationToken);" : "")
            .AppendLine($"        var query = Query{suffix}(request, actor);").AppendLine("        var total = await query.CountAsync(cancellationToken);")
            .AppendLine($"        var ordered = Order{suffix}(query, request);")
            .AppendLine(ManagerCredentialCompilation.IsTable(table) ? $"        var entities = await (unpaged ? ordered : ordered.Page(request.PageNo, request.PageSize)).ToListAsync({ManagerCredentialCompilation.ReadProjection(table)}, cancellationToken);" : "        var entities = await (unpaged ? ordered : ordered.Page(request.PageNo, request.PageSize)).ToListAsync(cancellationToken);")
            .AppendLine($"        return new({ProjectResults(model,table,page,"entities","list")}, total, unpaged ? 0 : (total + request.PageSize - 1) / request.PageSize, request.PageNo, request.PageSize);")
            .AppendLine("    }").AppendLine("    /// <summary>数量查询复用相同的数据范围和筛选白名单。</summary>")
            .AppendLine(SelectionPageRenderer.Enabled(page)
                ? $"    public async Task<long> Count{suffix}Async(PageRequest request, RuntimeActor actor, CancellationToken cancellationToken) {{ await ValidateSelectionParents{suffix}Async(request.ParentId, actor, cancellationToken); return await Query{suffix}(request, actor).CountAsync(cancellationToken); }}"
                : $"    public Task<long> Count{suffix}Async(PageRequest request, RuntimeActor actor, CancellationToken cancellationToken) => Query{suffix}(request, actor).CountAsync(cancellationToken);")
            .AppendLine("    /// <summary>明细读不到时保持数据不存在或无权限的统一语义。</summary>")
            .AppendLine($"    public async Task<{page.Name}Vo?> Detail{suffix}Async(long id, string? parentId, RuntimeActor actor, CancellationToken cancellationToken)").AppendLine("    {")
            .AppendLine($"        var entity = await Scope{suffix}(actor, parentId).Where(row => row.Id == id).{(ManagerCredentialCompilation.IsTable(table) ? "ToOneAsync("+ManagerCredentialCompilation.ReadProjection(table)+", cancellationToken)" : "FirstAsync(cancellationToken)")};")
            .AppendLine($"        return entity is null ? null : {RoleResult(model,table,page,"detail")};").AppendLine("    }");
        return result.ToString();
    }
    /// <summary>生成新增、部分更新和逻辑删除事务。</summary>
    private static string Mutations(GenerationModel model, TableModel table, PageModel page)
    {
        var suffix = page.Source.Id;
        var result = new StringBuilder();
        if (page.Source.IsNew == 1 || ImportRenderer.Enabled(page))
        {
            result.AppendLine("    /// <summary>新增使用页级白名单及真实身份补齐系统字段。</summary>")
                .AppendLine($"    public Task<{page.Name}Vo> Create{suffix}Async({page.Name}CreateRequest request, string? parentId, RuntimeActor actor, CancellationToken cancellationToken) => session.InTransactionAsync(async () =>")
                .AppendLine("    {").AppendLine("        ArgumentNullException.ThrowIfNull(request);")
                .Append(PrepareCall(model, table, page, "Create", "null", "request"))
                .AppendLine($"        await ValidateParents{suffix}Async(parentId, actor, cancellationToken);").AppendLine($"        var entity = new {table.Name} {{ Id = ids.NextId() }};").AppendLine("        var submitted = new HashSet<string>(StringComparer.Ordinal);");
            foreach (var column in table.Columns.Where(column => !column.IsSystem && !page.Writable.Contains(column) && !string.IsNullOrWhiteSpace(column.Source.DefaultValue))) result.AppendLine($"        entity.{column.Property} = {DefaultExpression(column)};");
            foreach (var column in ManagerCredentialCompilation.Writable(table,page))
            {
                var multi = column.Source.ControlKind == "MultiSelectPage" && MultiSelectCompilation.Resolve(model, table, page, column).Definition is not null;
                var role = column.Source.ControlKind=="MultiCheckBoxDatabase" && FixedRoleCheckboxCompilation.Resolve(model,table,page,column).Definition is not null;
                if (column.IsJsonArray && !multi && !role) result.AppendLine($"        if (request.{column.Property}.IsSpecified && request.{column.Property}.Value is {{ }} array{column.Property} && array{column.Property}.ValueKind != JsonValueKind.Array) throw new ArgumentException(\"数组控件只接受数组或 null。\");");
                result.AppendLine($"        if (request.{column.Property}.IsSpecified) {{ entity.{column.Property} = {(role ? $"RoleCheckboxValue.ReadWrite(request.{column.Property}.Value)" : multi ? MultiSelectFieldRenderer.Assignment(page, column) : column.IsJsonArray ? $"request.{column.Property}.Value?.GetRawText()" : ManagerCredentialCompilation.Assignment(table,page,column,$"request.{column.Property}.Value"))}; submitted.Add({Literal(column.Source.ColumnName)}); }}");
                if (!string.IsNullOrWhiteSpace(column.Source.DefaultValue))
                    result.AppendLine($"        else entity.{column.Property} = {DefaultExpression(column)};");
            }
            result.Append(Audit(table, true));
            foreach (var (column, source) in page.Scopes.Where(x => x.Source == "parent"))
                result.AppendLine("        if (string.IsNullOrWhiteSpace(parentId)) throw new ArgumentException(\"当前页面要求 parentId。\");")
                    .AppendLine($"        entity.{column.Property} = {ScopeValue(column, "parentId")};");
            result.Append(TrustedSecurityServiceCompilation.RequirePrepared(model,table,"true","false")).AppendLine("        BeforeSave(entity, actor, true, submitted);").Append(ManagerCredentialCompilation.DigestCall(table,true)).Append(BoundRelationValidationRenderer.Call(model, table, page, true, true)).AppendLine($"        await ValidateRelations{page.Source.Id}Async(entity, actor, true, submitted, cancellationToken);").Append(BoundRelationValidationRenderer.Call(model, table, page, true, false)).Append(RequiredValidation(model, table, page, false))
                .Append(WriteCall(page, "Create", "null"))
                .AppendLine("        if (await mapper.InsertAsync(entity, cancellationToken) != 1) throw new InvalidOperationException(\"新增失败。\");")
                .AppendLine($"        await Notify{table.Name}Async({Literal(page.Source.Id)}, entity, actor, submitted, false, true, cancellationToken);").AppendLine($"        return {RoleResult(model,table,page,"create")};").AppendLine("    }, cancellationToken);");
        }
        if (Metarill.Core.Metadata.PageUpdatePolicy.CanUpdate(page.Source) || (ImportRenderer.Enabled(page) && ImportRenderer.Mode(page) == "OverWriteByFirstColumn"))
        {
            result.AppendLine("    /// <summary>按存在性更新指定字段，省略不修改，显式 null 清空。</summary>")
                .AppendLine($"    public Task<{page.Name}Vo> Update{suffix}Async({page.Name}UpdateRequest request, string? parentId, RuntimeActor actor, CancellationToken cancellationToken) => session.InTransactionAsync(async () =>")
                .AppendLine("    {").AppendLine("        ArgumentNullException.ThrowIfNull(request);")
                .Append(PrepareCall(model, table, page, "Update", "request.Id", "request"))
                .AppendLine($"        var entity = await Scope{suffix}(actor, parentId).Where(row => row.Id == request.Id).ForUpdate().FirstAsync(cancellationToken) ?? throw new InvalidOperationException(\"数据不存在或无权限。\");")
                .AppendLine("        var originalScopeValues = MutationScopeValues(entity);")
                .Append(PlatformColumnTypeCompatibilityCompilation.Capture(model, table, page))
                .AppendLine("        var submitted = new HashSet<string>(StringComparer.Ordinal);");
            foreach (var column in ManagerCredentialCompilation.Writable(table,page))
            {
                var multi = column.Source.ControlKind == "MultiSelectPage" && MultiSelectCompilation.Resolve(model, table, page, column).Definition is not null;
                var role = column.Source.ControlKind=="MultiCheckBoxDatabase" && FixedRoleCheckboxCompilation.Resolve(model,table,page,column).Definition is not null;
                if (column.IsJsonArray && !multi && !role) result.AppendLine($"        if (request.{column.Property}.IsSpecified && request.{column.Property}.Value is {{ }} array{column.Property} && array{column.Property}.ValueKind != JsonValueKind.Array) throw new ArgumentException(\"数组控件只接受数组或 null。\");");
                result.AppendLine($"        if (request.{column.Property}.IsSpecified) {{ entity.{column.Property} = {(role ? $"RoleCheckboxValue.ReadWrite(request.{column.Property}.Value)" : multi ? MultiSelectFieldRenderer.Assignment(page, column) : column.IsJsonArray ? $"request.{column.Property}.Value?.GetRawText()" : ManagerCredentialCompilation.Assignment(table,page,column,$"request.{column.Property}.Value"))}; submitted.Add({Literal(column.Source.ColumnName)}); }}");
            }
            result.Append(Audit(table, false)).Append(TrustedSecurityServiceCompilation.RequirePrepared(model,table,"false","false")).AppendLine("        BeforeSave(entity, actor, false, submitted);").Append(ManagerCredentialCompilation.DigestCall(table,false)).Append(BoundRelationValidationRenderer.Call(model, table, page, false, true)).AppendLine($"        await ValidateRelations{page.Source.Id}Async(entity, actor, false, submitted, cancellationToken);").Append(BoundRelationValidationRenderer.Call(model, table, page, false, false)).Append(RequiredValidation(model, table, page, true));
            foreach (var (column, source) in page.Scopes.Where(x => x.Source == "parent"))
                result.AppendLine($"        if (entity.{column.Property} != {ScopeValue(column, "parentId!")}) throw new InvalidOperationException(\"不能修改页面归属字段。\");");
            result.Append(WriteCall(page, "Update", "originalScopeValues")).AppendLine("        if (submitted.Count > 0 && await mapper.UpdateAsync(entity, submitted.ToArray(), cancellationToken) != 1) throw new InvalidOperationException(\"修改失败。\");")
                .AppendLine($"        await Notify{table.Name}Async({Literal(page.Source.Id)}, entity, actor, submitted, false, false, cancellationToken);").AppendLine($"        return {RoleResult(model,table,page,"update")};").AppendLine("    }, cancellationToken);");
        }
        if (page.Source.IsDelete == 1)
        {
            result.AppendLine("    /// <summary>先校验数据范围，再在同一事务内删除。</summary>")
                .AppendLine($"    public Task<bool> Delete{suffix}Async(long id, string? parentId, RuntimeActor actor, CancellationToken cancellationToken) => session.InTransactionAsync(async () =>")
                .AppendLine("    {").Append(PrepareCall(model, table, page, "Delete", "id", null))
                .AppendLine($"        var entity = await Scope{suffix}(actor, parentId).Where(row => row.Id == id).ForUpdate().FirstAsync(cancellationToken) ?? throw new InvalidOperationException(\"数据不存在或无权限。\");")
                .AppendLine("        var originalScopeValues = MutationScopeValues(entity);")
                .Append(TrustedSecurityServiceCompilation.RequirePrepared(model,table,"false","true")).AppendLine("        BeforeDelete(entity, actor);")
                .Append(BoundRelationValidationRenderer.Call(model, table, page, true, true))
                .Append(BoundRelationValidationRenderer.Call(model, table, page, true, false))
                .Append(WriteCall(page, "Delete", "originalScopeValues", "new HashSet<string>(StringComparer.Ordinal)"));
            if (table.Columns.Any(x => x.Source.ColumnName == "delete_no"))
                result.AppendLine("        entity.DeleteNo = DateTime.UtcNow;").AppendLine("        var deleted = await mapper.UpdateAsync(entity, [\"delete_no\"], cancellationToken) == 1;");
            else result.AppendLine("        var deleted = await mapper.DeleteAsync(id, cancellationToken) == 1;"); result.AppendLine($"        if (deleted) await Notify{table.Name}Async({Literal(page.Source.Id)}, entity, actor, new HashSet<string>(), true, true, cancellationToken);").AppendLine("        return deleted;");
            result.AppendLine("    }, cancellationToken);");
        }
        return result.ToString();
    }
    /// <summary>复制完整业务值，重建主键和审计，不复制子表。</summary>
    private static string Copy(GenerationModel model, TableModel table, PageModel page)
    {
        if (page.Source.IsCopy != 1) return "";
        var result = new StringBuilder().AppendLine("    /// <summary>复制受限记录，创建规则和文件引用仍处于同一事务。</summary>")
            .AppendLine($"    public Task<{page.Name}Vo> Copy{page.Source.Id}Async(long id, string? parentId, RuntimeActor actor, CancellationToken cancellationToken) => session.InTransactionAsync(async () =>")
            .AppendLine("    {")
            .Append(PrepareCall(model, table, page, "Copy", "id", null))
            .AppendLine($"        await ValidateParents{page.Source.Id}Async(parentId, actor, cancellationToken);")
            .AppendLine($"        var source = await Scope{page.Source.Id}(actor, parentId).Where(row => row.Id == id).ForUpdate().FirstAsync(cancellationToken) ?? throw new InvalidOperationException(\"数据不存在或无权限。\");")
            .AppendLine("        var originalScopeValues = MutationScopeValues(source);")
            .AppendLine($"        var entity = new {table.Name} {{ Id = ids.NextId() }};")
            .AppendLine("        var submitted = new HashSet<string>(StringComparer.Ordinal);");
        foreach (var column in table.Columns.Where(column => !column.IsSystem))
            result.AppendLine($"        entity.{column.Property} = source.{column.Property}; submitted.Add({Literal(column.Source.ColumnName)});");
        result.Append(Audit(table, true)).AppendLine("        BeforeSave(entity, actor, true, submitted);").Append(BoundRelationValidationRenderer.Call(model, table, page, true, true)).AppendLine($"        await ValidateRelations{page.Source.Id}Async(entity, actor, true, submitted, cancellationToken);").Append(BoundRelationValidationRenderer.Call(model, table, page, true, false))
            .Append(RequiredValidation(model, table, page, false))
            .Append(WriteCall(page, "Copy", "originalScopeValues"))
            .AppendLine("        if (await mapper.InsertAsync(entity, cancellationToken) != 1) throw new InvalidOperationException(\"复制失败。\");")
            .AppendLine($"        await Notify{table.Name}Async({Literal(page.Source.Id)}, entity, actor, submitted, false, true, cancellationToken);")
            .AppendLine($"        return {RoleResult(model,table,page,"copy")};").AppendLine("    }, cancellationToken);");
        return result.ToString();
    }
    /// <summary>观察者在数据库事务内执行，异常会让本次对象写入一同回滚。</summary>
    private static string Observer(GenerationModel model, TableModel table)
    {
        var result = new StringBuilder().AppendLine("    /// <summary>文件引用和平台元数据联动加入本次实际事务。</summary>")
            .AppendLine($"    private async Task Notify{table.Name}Async(string pageId, {table.Name} entity, RuntimeActor actor, ISet<string> submitted, bool deleting, bool allFields, CancellationToken cancellationToken)")
            .AppendLine("    {").AppendLine("        var values = new Dictionary<string, object?>").AppendLine("        {");
        foreach (var column in table.Columns) result.AppendLine($"            [{Literal(column.Source.ColumnName)}] = entity.{column.Property},");
        result.AppendLine("        };")
            .AppendLine("        if (!allFields) values = values.Where(item => item.Key == \"id\" || submitted.Contains(item.Key)).ToDictionary(item => item.Key, item => item.Value);")
            .AppendLine("        var transaction = session.CurrentTransaction ?? throw new InvalidOperationException(\"写入观察者要求当前事务。\");")
            .AppendLine($"        var context = new RecordMutationContext(pageId, {Literal(table.Source.TableName)}, entity.Id, {ProjectExpression(table)}, values, deleting, transaction, IsCreate: allFields && !deleting);")
            .AppendLine("        foreach (var observer in observers) await observer.BeforeCommitAsync(context, cancellationToken);")
            .AppendLine("    }");
        return result.ToString();
    }
    /// <summary>读取实体真实项目归属，缺省时使用当前身份。</summary>
    private static string ProjectExpression(TableModel table) => table.Columns.Any(c => c.Source.ColumnName == "yz_project_id") ? "Convert.ToString(entity.YzProjectId, CultureInfo.InvariantCulture) ?? string.Empty" : "actor.ProjectId";
    /// <summary>由真实身份补齐审计字段。</summary>
    private static string Audit(TableModel table, bool creating)
    {
        var result = new StringBuilder();
        var assignments = new Dictionary<string, string>
        {
            ["update_time"] = "DateTime.UtcNow", ["update_manager_id"] = "actor.AccountId", ["update_manager_name"] = "actor.AccountName", ["update_manager_real_name"] = "actor.AccountName", ["update_user_name"] = "actor.AccountName"
        };
        if (creating)
        {
            assignments["insert_time"] = "DateTime.UtcNow"; assignments["insert_manager_id"] = "actor.AccountId"; assignments["insert_manager_name"] = "actor.AccountName"; assignments["insert_manager_real_name"] = "actor.AccountName"; assignments["insert_user_name"] = "actor.AccountName";
            var project = table.Columns.SingleOrDefault(c => c.Source.ColumnName == "yz_project_id");
            if (project is not null) assignments["yz_project_id"] = ScopeValue(project, "actor.ProjectId");
        }
        foreach (var column in table.Columns.Where(c => assignments.ContainsKey(c.Source.ColumnName)))
        {
            var expression = assignments[column.Source.ColumnName];
            if (column.Type == "string?" && column.Source.ColumnName.EndsWith("_manager_id", StringComparison.Ordinal)) expression += ".ToString(CultureInfo.InvariantCulture)";
            result.AppendLine($"        entity.{column.Property} = {expression};");
            if (!creating) result.AppendLine($"        submitted.Add({Literal(column.Source.ColumnName)});");
        }
        return result.ToString();
    }
    /// <summary>生成数据库和页面必填校验。</summary>
    private static string RequiredValidation(GenerationModel model, TableModel table, PageModel page, bool updating)
    {
        var result = new StringBuilder();
        foreach (var column in page.Writable.Where(c => c.Source.IsDbNotNull == 1 || model.Catalog.ConfigPageColumns.Any(f => f.ConfigPageId == page.Source.Id && f.ConfigColumnId == c.Source.Id && (f.IsRequired == 1 || GenerationModel.Enabled(f.Extra, "isMust")))))
        {
            var condition = column.Type == "string?" ? $"string.IsNullOrWhiteSpace(entity.{column.Property})" : $"entity.{column.Property} is null";
            var compatibility = updating ? PlatformColumnTypeCompatibilityCompilation.Exception(model, table, page, column) : "";
            if (compatibility.Length != 0) condition = $"({condition}) && !({compatibility})";
            result.AppendLine($"        if ({condition}) throw new ArgumentException({Literal(column.Source.Name + "不能为空。")});");
        }
        return result.ToString();
    }
    /// <summary>把固定默认值转为安全的类型表达式。</summary>
    private static string DefaultExpression(ColumnModel column)
    {
        if (column.Type == "string?") return Literal(column.Source.DefaultValue);
        try
        {
            var raw = column.Source.DefaultValue;
            if (column.Type is "DateTime?") raw = Literal(raw);
            using var parsed = JsonDocument.Parse(raw);
            return $"JsonSerializer.Deserialize<{column.Type}>({Literal(raw)}, JsonOptions)";
        }
        catch (JsonException) { throw new InvalidOperationException("字段默认值不能转换为逻辑类型：" + column.Source.ColumnName); }
    }
    /// <summary>把可信身份或已校验父记录转换为字段类型。</summary>
    private static string ScopeValue(ColumnModel column, string value) => column.Type switch
    {
        "string?" => value,
        "long?" or "long" => $"long.Parse({value}, CultureInfo.InvariantCulture)",
        "int?" => $"int.Parse({value}, CultureInfo.InvariantCulture)",
        _ => throw new InvalidOperationException("查询范围字段必须为整型或字符串。")
    };
    /// <summary>生成兼容原路由的 HTTP 权限边界。</summary>
    private static string Controller(GenerationModel model, TableModel table, PageModel page)
    {
        var suffix = page.Source.Id;
        var result = new StringBuilder(CSharpGenerator.Header).AppendLine("using Microsoft.AspNetCore.Mvc;").AppendLine("using Metarill.Core.Contracts;").AppendLine("using Metarill.Core.Security;")
            .AppendLine(model.Tables.Any(candidate => candidate.Pages.Count != 0) ? $"using {model.Namespace}.Generated.DTOs;" : "").AppendLine($"using {model.Namespace}.Generated.VOs;").AppendLine($"using {model.Namespace}.Generated.Services;")
            .AppendLine($"namespace {model.Namespace}.Generated.Controllers;").AppendLine("[ApiController]")
            .AppendLine($"[Route({Literal("api/admin/auto/" + table.Source.TableName + "/" + suffix)})]")
            .AppendLine($"public sealed class {page.Name}Controller({table.Name}Service service, IRuntimeAuthorization authorization) : ControllerBase").AppendLine("{")
            .AppendLine("    /// <summary>HTTP 边界先检查页面操作权限，再传递真实身份。</summary>")
            .AppendLine($"    private RuntimeActor Actor(string operation) {{ authorization.RequirePagePermission({Literal(suffix)}, operation); return authorization.RequireActor(); }}")
            .Append(LegacyPageRequestRenderer.Controller(model, table, page))
            .AppendLine("    /// <summary>兼容现有页面明细路由。</summary>").AppendLine("    [HttpGet(\"detail/{id}\")]")
            .AppendLine($"    public async Task<R<{page.Name}Vo>> Detail(long id, [FromQuery] string? parentId, CancellationToken cancellationToken)").AppendLine("    {")
            .AppendLine($"        var record = await service.Detail{suffix}Async(id, parentId, Actor(\"detail\"), cancellationToken);")
            .AppendLine($"        return record is null ? R<{page.Name}Vo>.Error(\"数据不存在或无权限。\") : R<{page.Name}Vo>.Success(record);").AppendLine("    }")
            .AppendLine("    /// <summary>兼容现有分页响应与分页路由。</summary>").AppendLine("    [HttpPost(\"page\")]")
            .AppendLine($"    public async Task<R<PageResult<{page.Name}Vo>>> Page([FromBody] PageRequest request, CancellationToken cancellationToken) => R<PageResult<{page.Name}Vo>>.Success(await service.Page{suffix}Async(request, Actor(\"list\"), cancellationToken));")
            .AppendLine("    /// <summary>数量接口沿用相同页面范围。</summary>").AppendLine("    [HttpPost(\"count\")]")
            .AppendLine($"    public async Task<R<long>> Count([FromBody] PageRequest request, CancellationToken cancellationToken) => R<long>.Success(await service.Count{suffix}Async(request, Actor(\"count\"), cancellationToken));");
        if (page.Source.IsNew == 1)
            result.AppendLine("    /// <summary>新增仅接受本页生成 DTO。</summary>").AppendLine("    [HttpPost(\"add\")]")
                .Append(LegacyPageRequestRenderer.Mutation(page, "Add"));
        if (Metarill.Core.Metadata.PageUpdatePolicy.CanUpdate(page.Source))
            result.AppendLine("    /// <summary>更新保留省略字段与显式 null 的区别。</summary>").AppendLine("    [HttpPost(\"update\")]")
                .Append(LegacyPageRequestRenderer.Mutation(page, "Update"));
        if (page.Source.IsDelete == 1)
            result.AppendLine("    /// <summary>兼容原 DELETE 请求体及逻辑删除流程。</summary>").AppendLine("    [HttpDelete]")
                .Append(LegacyPageRequestRenderer.Mutation(page, "Delete"));
        if (page.Source.IsCopy == 1)
            result.AppendLine("    /// <summary>兼容 V8 POST 复制路由，并单独检查复制权限。</summary>").AppendLine("    [HttpPost(\"copy/{id}\")]")
                .AppendLine($"    public async Task<R<{page.Name}Vo>> Copy(long id, [FromQuery] string? parentId, CancellationToken cancellationToken) => R<{page.Name}Vo>.Success(await service.Copy{suffix}Async(id, parentId, Actor(\"copy\"), cancellationToken));");
        result.Append(SelectionPageRenderer.Controller(page));
        result.Append(FixedRoleCheckboxRenderer.Controller(model,table,page));
        foreach (var catalog in FixedOptionCatalogCompilation.ForPage(model, page))
            result.AppendLine("    /// <summary>原下拉只读取固定C#模板目录，表单上下文不参与授权或查询。</summary>")
                .AppendLine($"    [HttpPost({Literal(catalog.Endpoint)})] [RequestSizeLimit(16384)]")
                .AppendLine($"    public R<IReadOnlyList<Metarill.Core.Metadata.TemplateVersionOption>> {catalog.Endpoint}([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] System.Text.Json.JsonElement? request) {{ Actor(\"list\"); if (Request.Query.Count != 0) throw new ArgumentException(\"版本候选不接受query参数。\"); return R<IReadOnlyList<Metarill.Core.Metadata.TemplateVersionOption>>.Success(Metarill.Core.Metadata.TemplateVersionCatalog.ReadOptions(request)); }}");
        foreach (var relation in RelationCompilation.ForPage(model, page))
            result.AppendLine("    /// <summary>关系选项同时验证来源页和目标页读取权限。</summary>")
                .AppendLine($"    [HttpPost({Literal(relation.Endpoint)})]" + (relation.LegacyEndpoint == relation.Endpoint ? "" : $" [HttpPost({Literal(relation.LegacyEndpoint)})]"))
                .AppendLine($"    public async Task<R<IReadOnlyList<GeneratedOption>>> {relation.Endpoint}([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RelationOptionsLegacyRequest? request, CancellationToken cancellationToken) {{ var actor = Actor(\"list\"); authorization.RequirePagePermission({Literal(relation.TargetPage.Source.Id)}, \"list\"); request ??= new(); var selected = request.ToSelectedRequest(); return R<IReadOnlyList<GeneratedOption>>.Success(selected is null ? await service.Options{page.Source.Id}_{relation.SourceColumn.Source.Id}Async(request.ToPageRequest(), actor, cancellationToken, request.TargetParentId) : await service.OptionsSelected{page.Source.Id}_{relation.SourceColumn.Source.Id}Async(selected, actor, cancellationToken)); }}")
                .AppendLine("    /// <summary>关系关键词分页始终采用已编译目标、固定排序和双方页面权限。</summary>")
                .AppendLine($"    [HttpPost({Literal(relation.Endpoint + "/page")})]" + (relation.LegacyEndpoint == relation.Endpoint ? "" : $" [HttpPost({Literal(relation.LegacyEndpoint + "/page")})]"))
                .AppendLine($"    public async Task<R<PageResult<GeneratedOption>>> {relation.Endpoint}Page([FromBody] RelationOptionsPageRequest request, CancellationToken cancellationToken) {{ var actor = Actor(\"list\"); authorization.RequirePagePermission({Literal(relation.TargetPage.Source.Id)}, \"list\"); return R<PageResult<GeneratedOption>>.Success(await service.OptionsPage{page.Source.Id}_{relation.SourceColumn.Source.Id}Async(request, actor, cancellationToken)); }}")
                .AppendLine("    /// <summary>已选项回读复用候选完整范围，任何非法项整体拒绝。</summary>")
                .AppendLine($"    [HttpPost({Literal(relation.Endpoint + "/selected")})]" + (relation.LegacyEndpoint == relation.Endpoint ? "" : $" [HttpPost({Literal(relation.LegacyEndpoint + "/selected")})]"))
                .AppendLine($"    public async Task<R<IReadOnlyList<GeneratedOption>>> {relation.Endpoint}Selected([FromBody] RelationOptionsSelectedRequest request, CancellationToken cancellationToken) {{ var actor = Actor(\"list\"); authorization.RequirePagePermission({Literal(relation.TargetPage.Source.Id)}, \"list\"); return R<IReadOnlyList<GeneratedOption>>.Success(await service.OptionsSelected{page.Source.Id}_{relation.SourceColumn.Source.Id}Async(request, actor, cancellationToken)); }}");
        result.Append(ImportRenderer.Controller(page)); result.Append(ExportRenderer.Controller(model, page));
        return result.AppendLine("}").ToString();
    }
    /// <summary>数组、分页和已选回读共用目标、真实父记录和固定范围，不生成通用查询网关。</summary>
    private static string Relations(GenerationModel model, PageModel page)
    {
        var visibleRelations = RelationCompilation.ForPage(model, page);
        var result = new StringBuilder(); var relations = visibleRelations.Concat(RelationCompilation.ForPage(model, page, true)).DistinctBy(relation => relation.SourceColumn.Source.Id).ToArray();
        if (relations.Length == 0) return "";
        // 来源父验证单独复用，使空导出也不会跳过真实父记录校验。
        var sourceParents = relations[0].SourceParents;
        result.AppendLine("    /// <summary>候选及导出共同核对来源父真实归属，空结果也不能绕过父范围。</summary>")
            .AppendLine($"    private async Task ValidateOptionsSourceParents{page.Source.Id}Async(string? parentId, RuntimeActor actor, CancellationToken cancellationToken)")
            .AppendLine("    {").AppendLine("        ValidateActor(actor);");
        if (sourceParents.Count > 0) result.AppendLine("        if (!long.TryParse(parentId, NumberStyles.None, CultureInfo.InvariantCulture, out var sourceParentKey) || sourceParentKey <= 0) throw new ArgumentException(\"关联来源页要求有效 parentId。\");");
        for (var index = 0; index < sourceParents.Count; index++) Parent(sourceParents[index], "sourceParentKey", "sourceParent" + index);
        if (sourceParents.Count == 0) result.AppendLine("        await Task.CompletedTask;");
        result.AppendLine("    }");
        /// <summary>父定义来自固定元数据，来源与目标分别验证同项目及软删，不取写锁。</summary>
        void Parent(TableModel parent, string key, string variable)
        {
            var project = parent.Columns.Single(column => column.Source.ColumnName == "yz_project_id");
            result.AppendLine($"        var {variable} = session.Select<{parent.Name}>().Where(entity => entity.Id == {key} && entity.{project.Property} == {ScopeValue(project, "actor.ProjectId")});");
            if (parent.Columns.Any(column => column.Source.ColumnName == "delete_no")) result.AppendLine($"        {variable} = {variable}.Where(entity => entity.DeleteNo == null);");
            result.AppendLine($"        if (!await {variable}.AnyAsync(cancellationToken)) throw new InvalidOperationException(\"关联父记录不存在、已删除或不属于当前项目。\");");
        }
        foreach (var relation in relations)
        {
            var suffix = page.Source.Id + "_" + relation.SourceColumn.Source.Id;
            result.AppendLine("    /// <summary>真实父记录校验为只读，不在候选查询中获取写锁。</summary>")
                .AppendLine($"    private async Task<ISelect<{relation.Target.Name}>> OptionsScope{suffix}Async(string? parentId, string? targetParentId, RuntimeActor actor, CancellationToken cancellationToken, bool locking = false, bool requireSourceList = true)")
                .AppendLine("    {");
            if (relation.ParentBinding?.Declared == true)
            {
                if (relation.ParentBinding.Binding is null) result.AppendLine($"        RejectBound{page.Source.Id}({Literal(relation.ParentBinding.Reason!)});");
                else result.AppendLine($"        targetParentId = (await ResolveBound{suffix}Async(parentId, targetParentId, actor, locking, requireSourceList, cancellationToken)).ToString(CultureInfo.InvariantCulture);");
            }
            else result.AppendLine($"        await ValidateOptionsSourceParents{page.Source.Id}Async(parentId, actor, cancellationToken);");
            var sameParent = relation.SourceParents.Count > 0 && relation.SourceParents.Select(parent => parent.Source.Id).Order().SequenceEqual(relation.TargetParents.Select(parent => parent.Source.Id).Order());
            if (relation.TargetParents.Count > 0)
            {
                if (sameParent) result.AppendLine("        targetParentId ??= parentId; // 元数据证明同父时兼容原 parentId。");
                result.AppendLine($"        if (!long.TryParse(targetParentId, NumberStyles.None, CultureInfo.InvariantCulture, out var targetParentKey) || targetParentKey <= 0) throw new ArgumentException({Literal("关联目标范围要求有效 targetParentId，请使用 " + relation.Endpoint + "/page 分页合同。")});");
                if (relation.ParentBinding?.Binding is null)
                    for (var index = 0; index < relation.TargetParents.Count; index++) Parent(relation.TargetParents[index], "targetParentKey", "targetParent" + index);
            }
            else result.AppendLine("        if (targetParentId is not null) throw new ArgumentException(\"当前关联不使用 targetParentId。\");");
            if (relation.SourceParents.Count == 0 && relation.TargetParents.Count == 0) result.AppendLine("        await Task.CompletedTask;");
            result.AppendLine($"        var query = session.Select<{relation.Target.Name}>();");
            if (relation.Target.Columns.Any(c => c.Source.ColumnName == "delete_no")) result.AppendLine("        query = query.Where(entity => entity.DeleteNo == null);");
            var project = relation.Target.Columns.SingleOrDefault(c => c.Source.ColumnName == "yz_project_id");
            if (project is not null && (!relation.Target.IsGlobal || relation.TargetPage.Scopes.Concat(relation.Scopes).Any(scope => scope.Source == "project"))) result.AppendLine($"        query = query.Where(entity => entity.{project.Property} == {ScopeValue(project, "actor.ProjectId")});");
            foreach (var (column, source) in relation.TargetPage.Scopes.Concat(relation.Scopes).Where(s => s.Source == "parent").DistinctBy(scope => scope.Column.Source.Id))
                result.AppendLine($"        query = query.Where(entity => entity.{column.Property} == {ScopeValue(column, "targetParentId!")});");
            if (relation.ParentBinding?.Binding?.SourceParent.Source.Id == "2076977530983813120")
            {
                foreach (var flag in relation.Target.Columns.Where(column => column.Source.ColumnName is "is_virtual_column" or "is_join_column"))
                    result.AppendLine($"        query = query.Where(entity => entity.{flag.Property} == null || entity.{flag.Property} != {(flag.Type == "bool?" ? "true" : "1")});");
                var kind = relation.Target.Columns.Single(column => column.Source.ColumnName == "control_kind");
                result.AppendLine($"        query = query.Where(entity => entity.{kind.Property} == null || entity.{kind.Property} != \"Title\" && entity.{kind.Property} != \"Tab\");");
            }
            if (relation.ParentBinding?.Binding is not null)
                result.AppendLine("        // 读取以同句相关范围防换表；写入已持有源父和归属当前读锁，不混入旧一致性快照。")
                    .AppendLine($"        if (!locking) query = ApplyBoundScope{suffix}(query, long.Parse(parentId!, NumberStyles.None, CultureInfo.InvariantCulture), actor);");
            var optionOrder = relation.OptionsOrder is {} ordering
                ? string.Concat(ordering.Select(item => $".{(item.Descending ? "OrderByDescending" : "OrderBy")}(entity => entity.{item.Column.Property})"))
                : $".OrderBy(entity => entity.{relation.NameColumn.Property}).OrderBy(entity => entity.Id)";
            result.AppendLine("        return query;").AppendLine("    }");
            // 隐藏字段只生成最终值校验所需的私有范围，不新增公开候选方法或Controller路由。
            if (!visibleRelations.Any(candidate => candidate.SourceColumn.Source.Id == relation.SourceColumn.Source.Id)) continue;
            result.AppendLine("    /// <summary>旧数组端点最多 5000 项，超限明确要求使用分页，不返回截断结果。</summary>")
                .AppendLine($"    public async Task<IReadOnlyList<GeneratedOption>> Options{suffix}Async(PageRequest request, RuntimeActor actor, CancellationToken cancellationToken, string? targetParentId = null)")
                .AppendLine("    {").AppendLine("        ArgumentNullException.ThrowIfNull(request);")
                .AppendLine($"        var query = await OptionsScope{suffix}Async(request.ParentId, targetParentId, actor, cancellationToken);")
                .AppendLine($"        var entities = await query{optionOrder}.Limit(5001).ToListAsync(entity => new {{ entity.Id, Name = entity.{relation.NameColumn.Property} }}, cancellationToken);")
                .AppendLine($"        if (entities.Count > 5000) throw new InvalidOperationException({Literal("关联候选超过 5000 条，请使用 " + relation.Endpoint + "/page 分页查询。")});")
                .AppendLine($"        return entities.Select(entity => new GeneratedOption(entity.Id, entity.Name, entity.Id, entity.Name)).ToArray();")
                .AppendLine("    }")
                .AppendLine("    /// <summary>关键词参数化，分页和数量基于同一范围；固定声明排序追加主键，其余沿用名称和ID。</summary>")
                .AppendLine($"    public async Task<PageResult<GeneratedOption>> OptionsPage{suffix}Async(RelationOptionsPageRequest request, RuntimeActor actor, CancellationToken cancellationToken)")
                .AppendLine("    {").AppendLine("        ArgumentNullException.ThrowIfNull(request); request.Validate();")
                .AppendLine($"        var query = await OptionsScope{suffix}Async(request.ParentId, request.TargetParentId, actor, cancellationToken);")
                .AppendLine("        if (!string.IsNullOrEmpty(request.Keyword))").AppendLine("        {").AppendLine("            var keyword = request.Keyword;")
                .AppendLine($"            query = query.Where(entity => entity.{relation.NameColumn.Property} != null && entity.{relation.NameColumn.Property}.Contains(keyword));")
                .AppendLine("        }").AppendLine("        var total = await query.CountAsync(cancellationToken);")
                .AppendLine($"        var entities = await query{optionOrder}.Page(request.PageNo, request.PageSize).ToListAsync(entity => new {{ entity.Id, Name = entity.{relation.NameColumn.Property} }}, cancellationToken);")
                .AppendLine($"        return new(entities.Select(entity => new GeneratedOption(entity.Id, entity.Name, entity.Id, entity.Name)).ToArray(), total, (total + request.PageSize - 1) / request.PageSize, request.PageNo, request.PageSize);")
                .AppendLine("    }")
                .AppendLine("    /// <summary>已选回读忽略当前关键词，但不得扩大项目、页面或控件范围。</summary>")
                .AppendLine($"    public async Task<IReadOnlyList<GeneratedOption>> OptionsSelected{suffix}Async(RelationOptionsSelectedRequest request, RuntimeActor actor, CancellationToken cancellationToken)")
                .AppendLine("    {").AppendLine("        ArgumentNullException.ThrowIfNull(request); var selected = request.ParseIds();")
                .AppendLine($"        var query = await OptionsScope{suffix}Async(request.ParentId, request.TargetParentId, actor, cancellationToken);")
                .AppendLine("        if (selected.Length == 0) return [];")
                .AppendLine($"        var entities = await query.Where(entity => selected.Contains(entity.Id)){optionOrder}.Limit(200).ToListAsync(entity => new {{ entity.Id, Name = entity.{relation.NameColumn.Property} }}, cancellationToken);")
                .AppendLine("        if (entities.Count != selected.Length) throw new InvalidOperationException(\"关联已选项不存在、已删除或不在当前可选范围。\");")
                .AppendLine($"        return entities.Select(entity => new GeneratedOption(entity.Id, entity.Name, entity.Id, entity.Name)).ToArray();")
                .AppendLine("    }");
        }
        return result.ToString();
    }
    /// <summary>生成仅首次创建的业务扩展文件。</summary>
    private static string HandwrittenService(GenerationModel model, TableModel table) => TrustedSecurityServiceCompilation.Applies(model,table) ? TrustedSecurityServiceCompilation.Template(model,table) : $$"""
namespace {{model.Namespace}}.Generated.Services;

/// <summary>手写扩展只首次创建。可实现 BeforeSave、BeforeDelete，不会被再生成覆盖。</summary>
public partial class {{table.Name}}Service
{
}

""";
    /// <summary>构造签名与生成DI工厂共享授权依赖判定，固定父关系不能只在手工构造时得到真实授权。</summary>
    private static bool RequiresRelationAuthorization(GenerationModel model, TableModel table) => table.Pages.Any(page => MultiSelectCompilation.ForService(model, table, page).Count > 0 || FixedRoleCheckboxCompilation.ForService(model,table,page) is not null || FixedRoleAdminNameCompilation.ForPage(model,table,page) is not null) || ExportRenderer.RequiresAuthorization(model, table) || BoundRelationValidationRenderer.RequiresAuthorization(table);
    /// <summary>固定角色名称只在受限读取后进入VO，不新增实体写入字段。</summary>
    private static string RoleResult(GenerationModel model, TableModel table, PageModel page,string operation) => FixedRoleCheckboxCompilation.ForPage(model,table,page) is null && (FixedRoleAdminNameCompilation.ForPage(model,table,page) is not {} admin || !FixedRoleAdminNameCompilation.Used(page,admin,operation)) ? $"{page.Name}Vo.FromEntity(entity)" : "("+ProjectResults(model,table,page,"[entity]",operation)+")[0]";
    /// <summary>页读取和写回执共享整页名称计划，避免单个入口遗漏或逐行补名。</summary>
    private static string ProjectResults(GenerationModel model,TableModel table,PageModel page,string entities,string operation) => FixedRoleCheckboxCompilation.ForPage(model,table,page) is not null ? FixedRoleCheckboxRenderer.ProjectCall(page,entities,operation,operation != "list" || !ManagerCredentialCompilation.IsOriginal(table,page))
        : FixedRoleAdminNameCompilation.ForPage(model,table,page) is {} admin && FixedRoleAdminNameCompilation.Used(page,admin,operation) ? FixedRoleAdminNameRenderer.ProjectCall(page,entities,operation) : ManagerCredentialCompilation.IsOriginal(table,page) ? $"{entities}.Select(entity => {page.Name}Vo.FromEntity(entity,{(operation == "list" ? "false" : "true")})).ToArray()" : $"{entities}.Select({page.Name}Vo.FromEntity).ToArray()";
    /// <summary>生成可独立选择数据库会话的服务注册。</summary>
    private static string Registration(GenerationModel model)
    {
        var result = new StringBuilder(CSharpGenerator.Header).AppendLine("using Microsoft.Extensions.DependencyInjection;").AppendLine("using Metarill.Data;").AppendLine("using Metarill.Core.Security;").AppendLine("using Metarill.Core.Persistence;")
            .AppendLine($"namespace {model.Namespace}.Generated;").AppendLine("internal sealed record GeneratedDatabaseSession(DatabaseSession Value);").AppendLine("public static class GeneratedRegistration").AppendLine("{")
            .AppendLine("    /// <summary>不同生成项目选择独立业务库；工厂返回由宿主管理生命周期的连接会话。</summary>")
            .AppendLine("    public static IServiceCollection AddGeneratedServices(this IServiceCollection services, Func<IServiceProvider, DatabaseSession>? databaseFactory = null)").AppendLine("    {")
            .AppendLine("        services.AddScoped<GeneratedDatabaseSession>(provider => new((databaseFactory ?? (sp => sp.GetRequiredService<DatabaseSession>()))(provider)));");
        foreach (var table in model.Tables)
            result.AppendLine($"        services.AddScoped<Mappers.{table.Name}Mapper>(provider => new(provider.GetRequiredService<GeneratedDatabaseSession>().Value));")
                .AppendLine($"        services.AddScoped<Services.{table.Name}Service>(provider => new(provider.GetRequiredService<GeneratedDatabaseSession>().Value, provider.GetRequiredService<Mappers.{table.Name}Mapper>(), provider.GetRequiredService<IIdGenerator>(), provider.GetServices<IRecordMutationObserver>(), provider.GetServices<IRecordMutationGuard>(){(RequiresRelationAuthorization(model, table) ? ", provider.GetRequiredService<IRuntimeAuthorization>()" : "")}));");
        return result.AppendLine("        return services;").AppendLine("    }").AppendLine("}").ToString();
    }
}
