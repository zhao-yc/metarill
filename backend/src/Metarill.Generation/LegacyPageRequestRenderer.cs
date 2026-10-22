using System.Text.Json;

namespace Metarill.Generation;

/// <summary>仅 HTTP 原页面完整回传经过固定白名单，内部 DTO 与 Service 不放宽。</summary>
internal static class LegacyPageRequestRenderer
{
    /// <summary>把本页可写、可回显与父范围编译为常量，不从请求接收字段定义。</summary>
    internal static string Controller(GenerationModel model, TableModel table, PageModel page)
    {
        string Array(IEnumerable<string> fields) => "[" + string.Join(", ", fields.Distinct(StringComparer.Ordinal).Select(value => JsonSerializer.Serialize(value))) + "]";
        var echo = page.Readable.Select(column => column.JsonName).Concat(FixedOptionCatalogCompilation.ForPage(model, page).Where(item => page.Readable.Contains(item.Source)).Select(item => item.JsonName)).Concat(page.Readable.Where(column => column.Source.ControlKind is "ConstList" or "ConstDownList").Select(column => column.JsonName + "Show"));
        if(NonRedundantReadCompilation.Requires(model,table))echo=echo.Concat(NonRedundantReadCompilation.ForPage(model,table,page).Outputs.Select(output=>output.JsonName));
        if (FixedRoleCheckboxCompilation.ForPage(model,table,page) is not null) echo = echo.Append(FixedRoleCheckboxModel.Alias);
        if (FixedRoleAdminNameCompilation.ForPage(model,table,page) is not null) echo = echo.Append(FixedRoleAdminNameModel.Alias);
        echo=echo.Concat(CascadeTreeViewRenderer.ForPage(model,table,page).Select(plan=>plan.Source.JsonName+"DisplayText"));
        echo=echo.Concat(CascadeTreeViewRenderer.ImplicitNames(model,table,page).Select(name=>name.JsonName));
        echo=echo.Concat(CrossTableDisplayRenderer.ForPage(model,table,page).Select(plan=>plan.Read.Source.JsonName+"DisplayText"));
        var project = table.Columns.SingleOrDefault(column => column.Source.ColumnName == "yz_project_id");
        return $$"""
    private static readonly LegacyPageWriteContract LegacyWrite = new({{Array(ManagerCredentialCompilation.Writable(table,page).Select(column => column.JsonName))}}, {{Array(echo)}}, {{Array(page.Scopes.Where(scope => scope.Source == "parent").Select(scope => scope.Column.JsonName))}}, {{(project is null ? "null" : JsonSerializer.Serialize(project.JsonName))}});
    /// <summary>先核验真实身份，再剥离只读回显；使用宿主原 JSON 设置保留日期、ID 与 Optional 语义。</summary>
    private T LegacyRequest<T>(System.Text.Json.JsonElement entity, LegacyPageWriteOperation operation, RuntimeActor actor, string? parentId)
    {
        {{(ManagerCredentialCompilation.IsOriginal(table,page) ? "entity = ManagerCredentialValue.NormalizeLegacy(entity, operation);\n        " : "")}}var payload = LegacyWrite.Normalize(entity, operation, actor.ProjectId, parentId);
        var options = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>(HttpContext.RequestServices).Value.JsonSerializerOptions;
        return System.Text.Json.JsonSerializer.Deserialize<T>(payload, options) ?? throw new ArgumentException("请求不能为空。");
    }

""";
    }

    /// <summary>旧路由和响应不变，只在 HTTP 边界转成既有严格请求类型。</summary>
    internal static string Mutation(PageModel page, string method)
    {
        var create = method == "Add";
        var delete = method == "Delete";
        var permission = create ? "create" : delete ? "delete" : "update";
        var operation = create ? "Create" : delete ? "Delete" : "Update";
        var request = page.Name + (create ? "CreateRequest" : delete ? "RecordRequest" : "UpdateRequest");
        var result = delete ? "string" : page.Name + "Vo";
        var call = delete
            ? $"return await service.Delete{page.Source.Id}Async(request.Id, parentId, actor, cancellationToken) ? R<string>.Success(\"删除成功\") : R<string>.Error(\"删除失败\");"
            : $"return R<{result}>.Success(await service.{(create ? "Create" : "Update")}{page.Source.Id}Async(request, parentId, actor, cancellationToken));";
        return $$"""
    [LegacyPageWriteRequest(typeof({{request}}))]
    public async Task<R<{{result}}>> {{method}}([FromBody] System.Text.Json.JsonElement entity, [FromQuery] string? parentId, CancellationToken cancellationToken)
    {
        var actor = Actor("{{permission}}");
        var request = LegacyRequest<{{request}}>(entity, LegacyPageWriteOperation.{{operation}}, actor, parentId);
        {{call}}
    }

""";
    }
}
