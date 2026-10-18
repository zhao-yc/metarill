using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

/// <summary>生成固定类型访问与页级白名单，运行时不解释元数据。</summary>
internal static class BackendRenderer
{
    private static string Literal(string value) => JsonSerializer.Serialize(value);
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
    private static string Dto(GenerationModel model, TableModel table, PageModel page)
    {
        var result = new StringBuilder(CSharpGenerator.Header).AppendLine("using System.Text.Json.Serialization;").AppendLine("using Metarill.Core.Contracts;")
            .AppendLine($"namespace {model.Namespace}.Generated.DTOs;");
        foreach (var kind in new[] { "Create", "Update" })
        {
            result.AppendLine("[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]").AppendLine($"public sealed class {page.Name}{kind}Request").AppendLine("{");
            if (kind == "Update") result.AppendLine("    public long Id { get; set; }");
            foreach (var column in page.Writable)
                result.AppendLine($"    [JsonPropertyName({Literal(column.JsonName)})]").AppendLine($"    public Optional<{column.Type}> {column.Property} {{ get; set; }}");
            result.AppendLine("}");
        }
        result.AppendLine("[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]").AppendLine($"public sealed class {page.Name}RecordRequest {{ public long Id {{ get; set; }} }}");
        return result.ToString();
    }
    private static string Vo(GenerationModel model, TableModel table, PageModel page)
    {
        var result = new StringBuilder(CSharpGenerator.Header).AppendLine("using System.Text.Json.Serialization;").AppendLine($"using {model.Namespace}.Generated.Entities;")
            .AppendLine($"namespace {model.Namespace}.Generated.VOs;").AppendLine($"public sealed class {page.Name}Vo").AppendLine("{");
        foreach (var column in page.Readable)
            result.AppendLine($"    [JsonPropertyName({Literal(column.JsonName)})]").AppendLine($"    public {column.Type} {column.Property} {{ get; init; }}");
        result.AppendLine("    /// <summary>只投影本页声明可读字段。</summary>").AppendLine($"    public static {page.Name}Vo FromEntity({table.Name} entity) => new()").AppendLine("    {");
        foreach (var column in page.Readable) result.AppendLine($"        {column.Property} = entity.{column.Property},");
        return result.AppendLine("    };").AppendLine("}").ToString();
    }
    private static string Mapper(GenerationModel model, TableModel table)
    {
        return CSharpGenerator.Header + $$"""
using FreeSql;
using Metarill.Data;
using {{model.Namespace}}.Generated.Entities;
namespace {{model.Namespace}}.Generated.Mappers;

/// <summary>固定类型持久化，当前事务由 DatabaseSession 自动附加。</summary>
public sealed class {{table.Name}}Mapper(DatabaseSession session)
{
    /// <summary>创建仅包含未删除记录的查询起点。</summary>
    public ISelect<{{table.Name}}> Query() => session.Select<{{table.Name}}>(){{(table.Columns.Any(c => c.Source.ColumnName == "delete_no") ? ".Where(entity => entity.DeleteNo == null)" : "")}};
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
    private static string Service(GenerationModel model, TableModel table)
    {
        var result = new StringBuilder(CSharpGenerator.Header).AppendLine("using System.Globalization;").AppendLine("using System.Text.Json;").AppendLine("using FreeSql;")
            .AppendLine("using Metarill.Core.Contracts;").AppendLine("using Metarill.Core.Security;").AppendLine("using Metarill.Core.Persistence;").AppendLine("using Metarill.Data;")
            .AppendLine($"using {model.Namespace}.Generated.Entities;").AppendLine($"using {model.Namespace}.Generated.DTOs;")
            .AppendLine($"using {model.Namespace}.Generated.VOs;").AppendLine($"using {model.Namespace}.Generated.Mappers;")
            .AppendLine($"namespace {model.Namespace}.Generated.Services;").AppendLine($"public partial class {table.Name}Service").AppendLine("{").AppendLine("    private readonly DatabaseSession session;").AppendLine($"    private readonly {table.Name}Mapper mapper;").AppendLine("    private readonly IIdGenerator ids;").AppendLine("    private readonly IEnumerable<IRecordMutationObserver> observers;").AppendLine("    /// <summary>注入当前事务、类型持久化及标识生成器。</summary>").AppendLine($"    public {table.Name}Service(DatabaseSession session, {table.Name}Mapper mapper, IIdGenerator ids, IEnumerable<IRecordMutationObserver> observers) {{ this.session = session; this.mapper = mapper; this.ids = ids; this.observers = observers; }}")
            .AppendLine("    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();")
            .AppendLine("    /// <summary>查询筛选值沿用字符串 ID 协议。</summary>")
            .AppendLine("    private static JsonSerializerOptions CreateJsonOptions() { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); options.Converters.Add(new StringLongJsonConverter()); return options; }")
            .AppendLine("    /// <summary>服务层再次检查身份来源，不能用客户端项目选择代替真实身份。</summary>")
            .AppendLine("    private static void ValidateActor(RuntimeActor actor) { if (actor.AccountId <= 0 || string.IsNullOrWhiteSpace(actor.ProjectId)) throw new UnauthorizedAccessException(\"登录身份无效。\"); }")
            .AppendLine("    /// <summary>所有单对象写入共用手写校验和归一化钩子。</summary>")
            .AppendLine($"    partial void BeforeSave({table.Name} entity, RuntimeActor actor, bool creating, ISet<string> submittedFields);")
            .AppendLine("    /// <summary>删除前规则在当前事务内执行。</summary>")
            .AppendLine($"    partial void BeforeDelete({table.Name} entity, RuntimeActor actor);");
        foreach (var page in table.Pages)
        {
            result.Append(Query(model, table, page)); result.Append(Relations(model, page));
            result.Append(Mutations(model, table, page));
        }
        result.Append(Observer(model, table)); return result.AppendLine("}").ToString();
    }
    private static string Query(GenerationModel model, TableModel table, PageModel page)
    {
        var suffix = page.Source.Id;
        var result = new StringBuilder().AppendLine($"    /// <summary>固定页面范围先进入 SQL，再执行明细、数量或分页查询。</summary>")
            .AppendLine($"    private ISelect<{table.Name}> Scope{suffix}(RuntimeActor actor, string? parentId)").AppendLine("    {")
            .AppendLine("        ValidateActor(actor);").AppendLine("        var query = mapper.Query();");
        var project = table.Columns.SingleOrDefault(c => c.Source.ColumnName == "yz_project_id");
        if (project is not null) result.AppendLine($"        query = query.Where(entity => entity.{project.Property} == {ScopeValue(project, "actor.ProjectId")});");
        foreach (var (column, source) in page.Scopes.Where(x => x.Source == "parent"))
            result.AppendLine("        if (string.IsNullOrWhiteSpace(parentId)) throw new ArgumentException(\"当前页面要求 parentId。\");")
                .AppendLine($"        query = query.Where(entity => entity.{column.Property} == {ScopeValue(column, "parentId")});");
        result.AppendLine("        return query;").AppendLine("    }")
            .AppendLine("    /// <summary>请求筛选只接受本页开放键；所有值通过表达式参数绑定。</summary>")
            .AppendLine($"    private ISelect<{table.Name}> Query{suffix}(PageRequest request, RuntimeActor actor)").AppendLine("    {")
            .AppendLine("        ArgumentNullException.ThrowIfNull(request);")
            .AppendLine($"        var query = Scope{suffix}(actor, request.ParentId);")
            .AppendLine("        foreach (var filter in request.Filters ?? throw new ArgumentException(\"filters 不能为空。\"))")
            .AppendLine("        {").AppendLine("            switch (filter.Key)").AppendLine("            {");
        foreach (var column in page.Searchable)
        {
            result.AppendLine($"                case {Literal(column.JsonName)}:").AppendLine("                {")
                .AppendLine($"                    var value = JsonSerializer.Deserialize<{column.Type}>(filter.Value.GetRawText(), JsonOptions);");
            var field = model.Catalog.ConfigPageColumns.Single(x => x.ConfigPageId == page.Source.Id && x.ConfigColumnId == column.Source.Id);
            var matchName = field.Extra.TryGetValue("searchType", out var match) ? match.ToString() : field.Extra.TryGetValue("matchType", out match) ? match.ToString() : ""; var like = column.Type == "string?" && (matchName == "like" || (string.IsNullOrEmpty(matchName) && column.Source.ControlKind is not "List" and not "ConstList" and not "ConstDownList" and not "SelectPage"));
            result.AppendLine(like ? $"                    query = query.Where(entity => entity.{column.Property} != null && value != null && entity.{column.Property}.Contains(value));" : $"                    query = query.Where(entity => entity.{column.Property} == value);")
                .AppendLine("                    break;").AppendLine("                }");
        }
        result.AppendLine("                default: throw new ArgumentException(\"页面未开放该筛选字段：\" + filter.Key);").AppendLine("            }").AppendLine("        }")
            .AppendLine("        if (request.SelectedIds.Count > 0)").AppendLine("        {").AppendLine("            var selected = request.SelectedIds.Select(id => long.Parse(id, CultureInfo.InvariantCulture)).ToArray();")
            .AppendLine("            query = query.Where(entity => selected.Contains(entity.Id));").AppendLine("        }").AppendLine("        return query;").AppendLine("    }")
            .AppendLine("    /// <summary>排序只能引用已生成的开放列，方向不能成为 SQL 片段。</summary>")
            .AppendLine($"    private static ISelect<{table.Name}> Order{suffix}(ISelect<{table.Name}> query, PageRequest request)").AppendLine("    {")
            .AppendLine("        if (request.OrderBy is not null)").AppendLine("        {")
            .AppendLine("            var descending = request.OrderBy.Order switch { \"descending\" => true, \"ascending\" => false, _ => throw new ArgumentException(\"排序方向无效。\") };")
            .AppendLine("            return (request.OrderBy.Column ?? request.OrderBy.Field) switch").AppendLine("            {");
        foreach (var column in page.Sortable)
            result.AppendLine($"                {Literal(column.JsonName)} => descending ? query.OrderByDescending(entity => entity.{column.Property}).OrderByDescending(entity => entity.Id) : query.OrderBy(entity => entity.{column.Property}).OrderByDescending(entity => entity.Id),");
        result.AppendLine("                _ => throw new ArgumentException(\"页面未开放该排序字段。\")").AppendLine("            };").AppendLine("        }");
        var order = GenerationModel.ValidateOrderBy(page.Source.OrderBy, table.Columns);
        result.AppendLine("        return query" + string.Concat(order.Select(item => $".{(item.Descending ? "OrderByDescending" : "OrderBy")}(entity => entity.{item.Column.Property})")) + (order.Any(item => item.Column.IsId) ? ";" : ".OrderByDescending(entity => entity.Id);"))
            .AppendLine("    }").AppendLine("    /// <summary>分页与统计使用同一条已受限查询。</summary>")
            .AppendLine($"    public async Task<PageResult<{page.Name}Vo>> Page{suffix}Async(PageRequest request, RuntimeActor actor, CancellationToken cancellationToken)").AppendLine("    {")
            .AppendLine("        if (request.PageNo < 1 || request.PageSize < 1 || request.PageSize > 1000) throw new ArgumentException(\"分页参数无效，每页最多 1000 条。\");")
            .AppendLine($"        var query = Query{suffix}(request, actor);").AppendLine("        var total = await query.CountAsync(cancellationToken);")
            .AppendLine($"        var entities = await Order{suffix}(query, request).Page(request.PageNo, request.PageSize).ToListAsync(cancellationToken);")
            .AppendLine($"        return new(entities.Select({page.Name}Vo.FromEntity).ToArray(), total, (total + request.PageSize - 1) / request.PageSize, request.PageNo, request.PageSize);")
            .AppendLine("    }").AppendLine("    /// <summary>数量查询复用相同的数据范围和筛选白名单。</summary>")
            .AppendLine($"    public Task<long> Count{suffix}Async(PageRequest request, RuntimeActor actor, CancellationToken cancellationToken) => Query{suffix}(request, actor).CountAsync(cancellationToken);")
            .AppendLine("    /// <summary>明细读不到时保持数据不存在或无权限的统一语义。</summary>")
            .AppendLine($"    public async Task<{page.Name}Vo?> Detail{suffix}Async(long id, string? parentId, RuntimeActor actor, CancellationToken cancellationToken)").AppendLine("    {")
            .AppendLine($"        var entity = await Scope{suffix}(actor, parentId).Where(row => row.Id == id).FirstAsync(cancellationToken);")
            .AppendLine($"        return entity is null ? null : {page.Name}Vo.FromEntity(entity);").AppendLine("    }");
        return result.ToString();
    }
    private static string Mutations(GenerationModel model, TableModel table, PageModel page)
    {
        var suffix = page.Source.Id;
        var result = new StringBuilder();
        if (page.Source.IsNew == 1)
        {
            result.AppendLine("    /// <summary>新增使用页级白名单及真实身份补齐系统字段。</summary>")
                .AppendLine($"    public Task<{page.Name}Vo> Create{suffix}Async({page.Name}CreateRequest request, string? parentId, RuntimeActor actor, CancellationToken cancellationToken) => session.InTransactionAsync(async () =>")
                .AppendLine("    {").AppendLine("        ArgumentNullException.ThrowIfNull(request);").AppendLine("        ValidateActor(actor);")
                .AppendLine($"        var entity = new {table.Name} {{ Id = ids.NextId() }};").AppendLine("        var submitted = new HashSet<string>(StringComparer.Ordinal);");
            foreach (var column in page.Writable)
            {
                result.AppendLine($"        if (request.{column.Property}.IsSpecified) {{ entity.{column.Property} = request.{column.Property}.Value; submitted.Add({Literal(column.Source.ColumnName)}); }}");
                if (!string.IsNullOrWhiteSpace(column.Source.DefaultValue))
                    result.AppendLine($"        else entity.{column.Property} = {DefaultExpression(column)};");
            }
            result.Append(Audit(table, true));
            foreach (var (column, source) in page.Scopes.Where(x => x.Source == "parent"))
                result.AppendLine("        if (string.IsNullOrWhiteSpace(parentId)) throw new ArgumentException(\"当前页面要求 parentId。\");")
                    .AppendLine($"        entity.{column.Property} = {ScopeValue(column, "parentId")};");
            result.AppendLine("        BeforeSave(entity, actor, true, submitted);").Append(RequiredValidation(model, table, page, false))
                .AppendLine("        if (await mapper.InsertAsync(entity, cancellationToken) != 1) throw new InvalidOperationException(\"新增失败。\");")
                .AppendLine($"        await Notify{table.Name}Async({Literal(page.Source.Id)}, entity, actor, submitted, false, true, cancellationToken);").AppendLine($"        return {page.Name}Vo.FromEntity(entity);").AppendLine("    }, cancellationToken);");
        }
        if (page.Source.IsSave == 1)
        {
            result.AppendLine("    /// <summary>按存在性更新指定字段，省略不修改，显式 null 清空。</summary>")
                .AppendLine($"    public Task<{page.Name}Vo> Update{suffix}Async({page.Name}UpdateRequest request, string? parentId, RuntimeActor actor, CancellationToken cancellationToken) => session.InTransactionAsync(async () =>")
                .AppendLine("    {").AppendLine("        ArgumentNullException.ThrowIfNull(request);")
                .AppendLine($"        var entity = await Scope{suffix}(actor, parentId).Where(row => row.Id == request.Id).ForUpdate().FirstAsync(cancellationToken) ?? throw new InvalidOperationException(\"数据不存在或无权限。\");")
                .AppendLine("        var submitted = new HashSet<string>(StringComparer.Ordinal);");
            foreach (var column in page.Writable)
                result.AppendLine($"        if (request.{column.Property}.IsSpecified) {{ entity.{column.Property} = request.{column.Property}.Value; submitted.Add({Literal(column.Source.ColumnName)}); }}");
            result.Append(Audit(table, false)).AppendLine("        BeforeSave(entity, actor, false, submitted);").Append(RequiredValidation(model, table, page, true));
            foreach (var (column, source) in page.Scopes.Where(x => x.Source == "parent"))
                result.AppendLine($"        if (entity.{column.Property} != {ScopeValue(column, "parentId!")}) throw new InvalidOperationException(\"不能修改页面归属字段。\");");
            result.AppendLine("        if (submitted.Count > 0 && await mapper.UpdateAsync(entity, submitted.ToArray(), cancellationToken) != 1) throw new InvalidOperationException(\"修改失败。\");")
                .AppendLine($"        await Notify{table.Name}Async({Literal(page.Source.Id)}, entity, actor, submitted, false, false, cancellationToken);").AppendLine($"        return {page.Name}Vo.FromEntity(entity);").AppendLine("    }, cancellationToken);");
        }
        if (page.Source.IsDelete == 1)
        {
            result.AppendLine("    /// <summary>先校验数据范围，再在同一事务内删除。</summary>")
                .AppendLine($"    public Task<bool> Delete{suffix}Async(long id, string? parentId, RuntimeActor actor, CancellationToken cancellationToken) => session.InTransactionAsync(async () =>")
                .AppendLine("    {").AppendLine($"        var entity = await Scope{suffix}(actor, parentId).Where(row => row.Id == id).ForUpdate().FirstAsync(cancellationToken) ?? throw new InvalidOperationException(\"数据不存在或无权限。\");")
                .AppendLine("        BeforeDelete(entity, actor);");
            if (table.Columns.Any(x => x.Source.ColumnName == "delete_no"))
                result.AppendLine("        entity.DeleteNo = DateTime.UtcNow;").AppendLine("        var deleted = await mapper.UpdateAsync(entity, [\"delete_no\"], cancellationToken) == 1;");
            else result.AppendLine("        var deleted = await mapper.DeleteAsync(id, cancellationToken) == 1;"); result.AppendLine($"        if (deleted) await Notify{table.Name}Async({Literal(page.Source.Id)}, entity, actor, new HashSet<string>(), true, true, cancellationToken);").AppendLine("        return deleted;");
            result.AppendLine("    }, cancellationToken);");
        }
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
            .AppendLine($"        var context = new RecordMutationContext(pageId, {Literal(table.Source.TableName)}, entity.Id, {ProjectExpression(table)}, values, deleting, transaction);")
            .AppendLine("        foreach (var observer in observers) await observer.BeforeCommitAsync(context, cancellationToken);")
            .AppendLine("    }");
        return result.ToString();
    }
    private static string ProjectExpression(TableModel table) => table.Columns.Any(c => c.Source.ColumnName == "yz_project_id") ? "Convert.ToString(entity.YzProjectId, CultureInfo.InvariantCulture) ?? string.Empty" : "actor.ProjectId";
    private static string Audit(TableModel table, bool creating)
    {
        var result = new StringBuilder();
        var assignments = new Dictionary<string, string>
        {
            ["update_time"] = "DateTime.UtcNow", ["update_manager_id"] = "actor.AccountId", ["update_manager_name"] = "actor.AccountName"
        };
        if (creating)
        {
            assignments["insert_time"] = "DateTime.UtcNow"; assignments["insert_manager_id"] = "actor.AccountId"; assignments["insert_manager_name"] = "actor.AccountName";
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
    private static string RequiredValidation(GenerationModel model, TableModel table, PageModel page, bool updating)
    {
        var result = new StringBuilder();
        foreach (var column in page.Writable.Where(c => c.Source.IsDbNotNull == 1 || model.Catalog.ConfigPageColumns.Any(f => f.ConfigPageId == page.Source.Id && f.ConfigColumnId == c.Source.Id && (f.IsRequired == 1 || GenerationModel.Enabled(f.Extra, "isMust")))))
        {
            var condition = column.Type == "string?" ? $"string.IsNullOrWhiteSpace(entity.{column.Property})" : $"entity.{column.Property} is null";
            result.AppendLine($"        if ({condition}) throw new ArgumentException({Literal(column.Source.Name + "不能为空。")});");
        }
        return result.ToString();
    }
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
    private static string ScopeValue(ColumnModel column, string value) => column.Type switch
    {
        "string?" => value,
        "long?" or "long" => $"long.Parse({value}, CultureInfo.InvariantCulture)",
        "int?" => $"int.Parse({value}, CultureInfo.InvariantCulture)",
        _ => throw new InvalidOperationException("查询范围字段必须为整型或字符串。")
    };
    private static string Controller(GenerationModel model, TableModel table, PageModel page)
    {
        var suffix = page.Source.Id;
        var result = new StringBuilder(CSharpGenerator.Header).AppendLine("using Microsoft.AspNetCore.Mvc;").AppendLine("using Metarill.Core.Contracts;").AppendLine("using Metarill.Core.Security;")
            .AppendLine($"using {model.Namespace}.Generated.DTOs;").AppendLine($"using {model.Namespace}.Generated.VOs;").AppendLine($"using {model.Namespace}.Generated.Services;")
            .AppendLine($"namespace {model.Namespace}.Generated.Controllers;").AppendLine("[ApiController]")
            .AppendLine($"[Route({Literal("api/admin/auto/" + table.Source.TableName + "/" + suffix)})]")
            .AppendLine($"public sealed class {page.Name}Controller({table.Name}Service service, IRuntimeAuthorization authorization) : ControllerBase").AppendLine("{")
            .AppendLine("    /// <summary>HTTP 边界先检查页面操作权限，再传递真实身份。</summary>")
            .AppendLine($"    private RuntimeActor Actor(string operation) {{ authorization.RequirePagePermission({Literal(suffix)}, operation); return authorization.RequireActor(); }}")
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
                .AppendLine($"    public async Task<R<{page.Name}Vo>> Add([FromBody] {page.Name}CreateRequest request, [FromQuery] string? parentId, CancellationToken cancellationToken) => R<{page.Name}Vo>.Success(await service.Create{suffix}Async(request, parentId, Actor(\"create\"), cancellationToken));");
        if (page.Source.IsSave == 1)
            result.AppendLine("    /// <summary>更新保留省略字段与显式 null 的区别。</summary>").AppendLine("    [HttpPost(\"update\")]")
                .AppendLine($"    public async Task<R<{page.Name}Vo>> Update([FromBody] {page.Name}UpdateRequest request, [FromQuery] string? parentId, CancellationToken cancellationToken) => R<{page.Name}Vo>.Success(await service.Update{suffix}Async(request, parentId, Actor(\"update\"), cancellationToken));");
        if (page.Source.IsDelete == 1)
            result.AppendLine("    /// <summary>兼容原 DELETE 请求体及逻辑删除流程。</summary>").AppendLine("    [HttpDelete]")
                .AppendLine($"    public async Task<R<string>> Delete([FromBody] {page.Name}RecordRequest request, [FromQuery] string? parentId, CancellationToken cancellationToken) => await service.Delete{suffix}Async(request.Id, parentId, Actor(\"delete\"), cancellationToken) ? R<string>.Success(\"删除成功\") : R<string>.Error(\"删除失败\");");
        foreach (var relation in RelationCompilation.ForPage(model, page))
            result.AppendLine("    /// <summary>关系选项同时验证来源页和目标页读取权限。</summary>")
                .AppendLine($"    [HttpPost({Literal(relation.Endpoint)})]")
                .AppendLine($"    public async Task<R<IReadOnlyList<GeneratedOption>>> {relation.Endpoint}([FromBody] PageRequest request, CancellationToken cancellationToken) {{ var actor = Actor(\"list\"); authorization.RequirePagePermission({Literal(relation.TargetPage.Source.Id)}, \"list\"); return R<IReadOnlyList<GeneratedOption>>.Success(await service.Options{page.Source.Id}_{relation.SourceColumn.Source.Id}Async(request, actor, cancellationToken)); }}");
        return result.AppendLine("}").ToString();
    }
    /// <summary>选项查询复用目标页和关联配置范围，仅输出 id/name/value/text。</summary>
    private static string Relations(GenerationModel model, PageModel page)
    {
        var result = new StringBuilder();
        foreach (var relation in RelationCompilation.ForPage(model, page))
        {
            result.AppendLine("    /// <summary>关联选项先收窄到目标页数据范围，再投影显示字段。</summary>")
                .AppendLine($"    public async Task<IReadOnlyList<GeneratedOption>> Options{page.Source.Id}_{relation.SourceColumn.Source.Id}Async(PageRequest request, RuntimeActor actor, CancellationToken cancellationToken)")
                .AppendLine("    {").AppendLine("        ValidateActor(actor);")
                .AppendLine($"        var query = session.Select<{relation.Target.Name}>();");
            if (relation.Target.Columns.Any(c => c.Source.ColumnName == "delete_no")) result.AppendLine("        query = query.Where(entity => entity.DeleteNo == null);");
            var project = relation.Target.Columns.SingleOrDefault(c => c.Source.ColumnName == "yz_project_id");
            if (project is not null) result.AppendLine($"        query = query.Where(entity => entity.{project.Property} == {ScopeValue(project, "actor.ProjectId")});");
            foreach (var (column, source) in relation.TargetPage.Scopes.Concat(relation.Scopes).Where(s => s.Source == "parent"))
                result.AppendLine("        if (string.IsNullOrWhiteSpace(request.ParentId)) throw new ArgumentException(\"关联选项要求 parentId。\");")
                    .AppendLine($"        query = query.Where(entity => entity.{column.Property} == {ScopeValue(column, "request.ParentId")});");
            result.AppendLine($"        var entities = await query.OrderBy(entity => entity.{relation.NameColumn.Property}).OrderBy(entity => entity.Id).Limit(5000).ToListAsync(cancellationToken);")
                .AppendLine($"        return entities.Select(entity => new GeneratedOption(entity.Id, entity.{relation.NameColumn.Property}, entity.Id, entity.{relation.NameColumn.Property})).ToArray();")
                .AppendLine("    }");
        }
        return result.ToString();
    }
    private static string HandwrittenService(GenerationModel model, TableModel table) => $$"""
namespace {{model.Namespace}}.Generated.Services;

/// <summary>手写扩展只首次创建。可实现 BeforeSave、BeforeDelete，不会被再生成覆盖。</summary>
public partial class {{table.Name}}Service
{
}

""";
    private static string Registration(GenerationModel model)
    {
        var result = new StringBuilder(CSharpGenerator.Header).AppendLine("using Microsoft.Extensions.DependencyInjection;").AppendLine("using Metarill.Data;").AppendLine("using Metarill.Core.Security;").AppendLine("using Metarill.Core.Persistence;")
            .AppendLine($"namespace {model.Namespace}.Generated;").AppendLine("internal sealed record GeneratedDatabaseSession(DatabaseSession Value);").AppendLine("public static class GeneratedRegistration").AppendLine("{")
            .AppendLine("    /// <summary>不同生成项目选择独立业务库；工厂返回由宿主管理生命周期的连接会话。</summary>")
            .AppendLine("    public static IServiceCollection AddGeneratedServices(this IServiceCollection services, Func<IServiceProvider, DatabaseSession>? databaseFactory = null)").AppendLine("    {")
            .AppendLine("        services.AddScoped<GeneratedDatabaseSession>(provider => new((databaseFactory ?? (sp => sp.GetRequiredService<DatabaseSession>()))(provider)));");
        foreach (var table in model.Tables)
            result.AppendLine($"        services.AddScoped<Mappers.{table.Name}Mapper>(provider => new(provider.GetRequiredService<GeneratedDatabaseSession>().Value));")
                .AppendLine($"        services.AddScoped<Services.{table.Name}Service>(provider => new(provider.GetRequiredService<GeneratedDatabaseSession>().Value, provider.GetRequiredService<Mappers.{table.Name}Mapper>(), provider.GetRequiredService<IIdGenerator>(), provider.GetServices<IRecordMutationObserver>()));");
        return result.AppendLine("        return services;").AppendLine("    }").AppendLine("}").ToString();
    }
}

