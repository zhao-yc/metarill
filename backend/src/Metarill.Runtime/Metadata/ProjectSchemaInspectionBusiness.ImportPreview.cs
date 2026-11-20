using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Data.Schema;

namespace Metarill.Runtime.Metadata;

/// <summary>第一阶段只生成逻辑草案，既不是导入计划也不提供保存许可；剩余检查必须在后续确认流程完成。</summary>
public sealed record ProjectSchemaImportPreview(string ProjectId, string MetadataFingerprint, SchemaImportDraft Draft, IReadOnlyList<string> PendingChecks)
{
    public bool Executable => false;
    public bool CanImport => false;
}

public sealed partial class ProjectSchemaInspectionBusiness
{
    /// <summary>当前项目管理员只读检查未登记表；不分配配置ID、不调用新建表钩子、不自动补字段或页面。</summary>
    public async Task<ProjectSchemaImportPreview> PreviewImportAsync(JsonElement request, CancellationToken token = default)
    {
        var identity = authorization.RequireIdentity();
        if (identity.GrantId is not null) throw new UnauthorizedAccessException("反向结构预览需要网页登录管理员身份。");
        var actor = authorization.RequireAdministrator();
        MetadataWorkbenchRules.Object(request, "tableNames");
        if (!request.TryGetProperty("tableNames", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is < 1 or > 16)
            throw new ArgumentException("tableNames必须是1至16个明确物理表名的数组。");
        var names = values.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new ArgumentException("物理表名必须是字符串。")).ToArray();
        foreach (var name in names) Metarill.Core.Metadata.MetadataValidation.RequireIdentifier(name);
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) throw new ArgumentException("物理表名不能重复。");
        var catalog = files.ComposePhysical(await metadata.LoadProjectAsync(actor.ProjectId, token));
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(catalog)));
        return await schemas.Resolve(actor.ProjectId).WithImportPreviewAsync(names, (inspection, _) =>
        {
            var registered = catalog.ConfigTables.Select(table => table.TableName).ToHashSet(inspection.CaseSensitiveNames ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            if (inspection.Snapshot.Schema.Tables.Any(table => registered.Contains(table.Name))) throw new InvalidOperationException("已在当前项目登记的表应使用原受管反查，不允许以反向草案覆盖。");
            var draft = SchemaImportDraftCompiler.Compile(inspection, actor.ProjectId);
            draft.LogicalMetadata.YzProjectName = catalog.YzProjectName;
            draft.LogicalMetadata.PrimaryColor = catalog.PrimaryColor;
            var checks = new List<string>();
            if (inspection.Views.Count > 0)
                checks.Add("视图仅预览列。请先在数据库确认 id 唯一且稳定，再通过原表配置登记视图。实体表如需审核保存，请单独选择实体表。");
            if (inspection.Views.Count < inspection.Snapshot.Schema.Tables.Count)
                checks.AddRange(
                [
                    "审核前请核对草案中的类型、索引、约束等缺口。",
                    "调用 /api/admin/sys/database/import/prepare 审核。服务端复核其他项目及已删除配置的归属，并绑定当前身份、物理结构和元数据版本。",
                    "审核后使用 planId 和 confirmSha256 调用 /api/admin/sys/database/import/apply 确认，仅在平台事务中保存元数据。",
                    "触发器的可见性与运行副作用仍需另行确认。"
                ]);
            return Task.FromResult(new ProjectSchemaImportPreview(actor.ProjectId, fingerprint, draft, checks));
        }, token);
    }
}
