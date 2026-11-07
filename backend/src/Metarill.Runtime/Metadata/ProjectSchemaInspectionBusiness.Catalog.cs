using System.Text.Json;
using Metarill.Data.Schema;

namespace Metarill.Runtime.Metadata;

/// <summary>登记状态只相对当前项目；共享库中的未登记对象不表示可归属或可导入。</summary>
public sealed record ProjectSchemaCatalogObject(SchemaCatalogObject Physical, bool RegisteredInCurrentProject);
public sealed record ProjectSchemaCatalog(string ProjectId, string Provider, string Database, IReadOnlyList<ProjectSchemaCatalogObject> Objects, string CatalogFingerprint, bool CaseSensitiveNames)
{
    public bool Executable => false;
}

public sealed partial class ProjectSchemaInspectionBusiness
{
    /// <summary>沿用受管反查的真实平台管理员边界；不接受任意库名、SQL、连接或客户端登记声明。</summary>
    public async Task<ProjectSchemaCatalog> CatalogAsync(JsonElement request, CancellationToken token = default)
    {
        var identity = authorization.RequireIdentity();
        if (identity.GrantId is not null) throw new UnauthorizedAccessException("结构反查需要网页登录管理员身份。");
        var actor = authorization.RequireAdministrator();
        MetadataWorkbenchRules.Object(request);
        var catalog = files.ComposePhysical(await metadata.LoadProjectAsync(actor.ProjectId, token));
        var snapshot = await schemas.Resolve(actor.ProjectId).InspectCatalogAsync(token);
        // 名称比较遵循适配器核实的数据库规则，不用平台操作系统推断大小写。
        var registered = catalog.ConfigTables.Where(table => table.IsFunctionTable == 0).Select(table => table.TableName)
            .ToHashSet(snapshot.CaseSensitiveNames ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        return new(actor.ProjectId, snapshot.Provider, snapshot.Database,
            snapshot.Objects.Select(item => new ProjectSchemaCatalogObject(item, registered.Contains(item.Name))).ToArray(), snapshot.CatalogFingerprint, snapshot.CaseSensitiveNames);
    }
}
