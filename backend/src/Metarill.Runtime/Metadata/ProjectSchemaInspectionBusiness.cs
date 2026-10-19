using System.Globalization;
using System.Text.Json;
using Metarill.Data.Schema;
using Metarill.Runtime.Files;
using Metarill.Runtime.Security;

namespace Metarill.Runtime.Metadata;

/// <summary>明确列出本次适配器未检查的定义，避免误报为物理缺失。</summary>
public sealed record SchemaInspectionExclusion(string TableId, string TableName, string Reason);

/// <summary>结构快照只有只读语义，不具有结构计划标识或执行权限。</summary>
public sealed record ProjectSchemaInspection(string ProjectId, SchemaInspectionSnapshot Snapshot, IReadOnlyList<string> ManagedTables, IReadOnlyList<string> MissingTables, IReadOnlyList<SchemaInspectionExclusion> ExcludedTables)
{
    public bool Executable => false;
}

/// <summary>反查只使用真实管理员当前项目及服务端连接，不能由请求指定库、表名或SQL。</summary>
public sealed class ProjectSchemaInspectionBusiness(MetadataRepository metadata, ProjectSchemaService schemas, ProjectFileRuntimes files, HttpRuntimeAuthorization authorization)
{
    /// <summary>按当前有效元数据反查受管表；缺失物理表明确列出，不写元数据、DDL或执行历史。</summary>
    public async Task<ProjectSchemaInspection> InspectAsync(JsonElement request, CancellationToken token = default)
    {
        var identity = authorization.RequireIdentity();
        if (identity.GrantId is not null) throw new UnauthorizedAccessException("结构反查需要网页登录管理员身份。");
        var actor = authorization.RequireAdministrator();
        MetadataWorkbenchRules.Object(request, "tableId");
        var catalog = files.ComposePhysical(await metadata.LoadProjectAsync(actor.ProjectId, token));
        if (request.TryGetProperty("tableId", out _))
        {
            var id = MetadataWorkbenchRules.Id(request, "tableId").ToString(CultureInfo.InvariantCulture);
            var selected = catalog.ConfigTables.SingleOrDefault(table => table.Id == id) ?? throw new UnauthorizedAccessException("受管表不存在或不属于当前项目。");
            if (selected.IsFunctionTable != 0 || selected.IsView != 0) throw new NotSupportedException("当前受管实体结构反查不支持功能表或视图。");
            catalog.ConfigTables = catalog.ConfigTables.Where(table => table.Id == id).ToList();
            catalog.ConfigColumns = catalog.ConfigColumns.Where(column => column.ConfigTableId == id).ToList();
            var indexes = catalog.ConfigIndexes.Where(index => index.TryGetProperty("configTableId", out var table) && table.ToString() == id).ToList();
            var indexIds = indexes.Select(index => index.GetProperty("id").ToString()).ToHashSet(StringComparer.Ordinal);
            catalog.ConfigIndexes = indexes;
            catalog.ConfigIndexColumns = catalog.ConfigIndexColumns.Where(column => column.TryGetProperty("configIndexId", out var index) && indexIds.Contains(index.ToString())).ToList();
            catalog.ConfigPages = []; catalog.ConfigPageColumns = []; catalog.ConfigChildren = []; catalog.ConfigButtons = []; catalog.ConfigMenus = [];
        }
        // 适配器只反查普通实体表，功能表/视图明确排除，不能误报成缺失物理表。
        var excluded = catalog.ConfigTables.Where(table => table.IsFunctionTable != 0 || table.IsView != 0)
            .OrderBy(table => table.TableName, StringComparer.Ordinal).Select(table => new SchemaInspectionExclusion(table.Id, table.TableName, table.IsView != 0 ? "视图反查尚未接入" : "功能表不属于实体结构反查范围")).ToArray();
        var managed = catalog.ConfigTables.Where(table => table.IsFunctionTable == 0 && table.IsView == 0).Select(table => table.TableName).Order(StringComparer.Ordinal).ToArray();
        var snapshot = await schemas.Resolve(actor.ProjectId).InspectSnapshotAsync(catalog, token);
        var present = snapshot.Schema.Tables.Select(table => table.Name).ToHashSet(StringComparer.Ordinal);
        return new(actor.ProjectId, snapshot, managed, managed.Where(name => !present.Contains(name)).ToArray(), excluded);
    }
}
