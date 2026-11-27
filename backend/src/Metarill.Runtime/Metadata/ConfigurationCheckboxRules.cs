using System.Globalization;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Metadata;

/// <summary>复选配置只验证已声明的目标及显示字段，候选范围由发布的生成计划执行。</summary>
internal static class ConfigurationCheckboxRules
{
    /// <summary>校验表级和有效页面定义，确保页面覆盖仍指向同一业务表。</summary>
    internal static async Task<TableMetadata?> ValidateAsync(SqlRowStore store, long project, TableMetadata owner,
        ColumnMetadata source, string? overlay, CancellationToken token)
    {
        if (!source.ColumnName.EndsWith("_ids", StringComparison.Ordinal) || source.LogicalType != "string")
            throw Error("来源必须为字符串 _ids 字段。");
        if (PlatformRoleCheckboxBinding.IsProtectedTable(owner))
        {
            RequireFixedRoles(owner, source, overlay);
            return null;
        }

        DatabaseCheckboxDefinition baseline, effective;
        try
        {
            baseline = DatabaseCheckboxDefinition.Parse(source.ControlDetail);
            effective = baseline.WithPageOverride(overlay);
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
        {
            throw Error("控件明细无效，请检查原目标表、四段或 JSON 配置。");
        }
        if (!MetadataValidation.IsSortExpression(effective.OrderBy))
            throw Error("排序只能填写字段名及可选的 ASC 或 DESC。");

        var original = baseline.TargetReferences(source);
        var current = effective.TargetReferences(source);
        var target = await ResolveTargetAsync(store, project, current, token);
        if ((!original.TableIds.SequenceEqual(current.TableIds) || !original.TableNames.SequenceEqual(current.TableNames) ||
                !original.PageIds.SequenceEqual(current.PageIds)) &&
            (await ResolveTargetAsync(store, project, original, token)).Id != target.Id)
            throw Error("页面覆盖必须沿用原目标表。");

        await ConfigurationSelectionRules.RequireDisplayFieldAsync(store, project, target,
            Id(target.Id, "目标表"), effective.NameField, "数据库复选", token);
        return target;
    }

    /// <summary>固定账号角色字段按原协议保存，不进入普通候选表查找。</summary>
    private static void RequireFixedRoles(TableMetadata owner, ColumnMetadata source, string? overlay)
    {
        var linkedPage = source.Extra.GetValueOrDefault("linkConfigPageId").ToString();
        var joined = source.Extra.GetValueOrDefault("isJoinColumn");
        if (owner.Id != PlatformRoleCheckboxBinding.ManagerTable || owner.TableName != "yz_manager" ||
            source.Id != PlatformRoleCheckboxBinding.RoleColumn || source.ColumnName != "yz_role_ids" ||
            source.ColumnLength != "500" || source.IsVirtualColumn != 0 || joined.ValueKind == JsonValueKind.True || joined.ToString() == "1" ||
            source.ControlDetail != "yz_role" || !string.IsNullOrEmpty(overlay) && overlay != "yz_role" ||
            source.LinkConfigTableId is { Length: >0 } linkedTable && linkedTable != PlatformRoleCheckboxBinding.RoleTable ||
            linkedPage.Length > 0 && linkedPage != PlatformRoleCheckboxBinding.RolePage ||
            source.Extra.GetValueOrDefault("joinTable").ToString().Length > 0)
            throw Error("平台安全表只允许原账号角色复选配置。");
    }

    /// <summary>全部表名、表 ID 和页 ID 都要存在于同项目，并指向同一个活动目标表。</summary>
    private static async Task<TableMetadata> ResolveTargetAsync(SqlRowStore store, long project,
        (string[] TableIds, string[] TableNames, string[] PageIds) references, CancellationToken token)
    {
        var tableIds = references.TableIds.Select(value => Id(value, "目标表")).ToHashSet();
        foreach (var pageId in references.PageIds)
        {
            var page = (await store.ReadForUpdateAsync("config_page", new Dictionary<string, object?>
            {
                ["id"] = Id(pageId, "目标页"), ["yz_project_id"] = project
            }, token)).SingleOrDefault();
            if (page is null) throw Error("目标页不存在或不属于当前项目。");
            tableIds.Add(SqlRowStore.Id(page, "config_table_id"));
        }

        TableMetadata? target = null;
        if (tableIds.Count > 1) throw Error("目标表和目标页的声明不一致。");
        if (tableIds.Count == 1)
            target = await RequireTableAsync("id", tableIds.Single());

        foreach (var name in references.TableNames)
        {
            if (!MetadataValidation.IsIdentifier(name)) throw Error("目标表名称无效。");
            var declared = await RequireTableAsync("table_name", name);
            if (declared.TableName != name || target is not null && target.Id != declared.Id)
                throw Error("目标表名称与已声明的关系不一致。");
            target = declared;
        }

        if (target is null || target.IsFunctionTable == 1 || PlatformRoleCheckboxBinding.IsProtectedTable(target))
            throw Error("目标必须是普通业务数据表。");
        return target;

        async Task<TableMetadata> RequireTableAsync(string field, object value)
        {
            var tables = await store.ReadForUpdateAsync("config_table",
                new Dictionary<string, object?> { [field] = value, ["yz_project_id"] = project }, token);
            return tables.Count == 1 ? MetadataRepository.DecodeRow<TableMetadata>(tables[0])
                : throw Error("目标表不存在、不属于当前项目或无法唯一确定。");
        }
    }

    private static long Id(string value, string label) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 &&
        id.ToString(CultureInfo.InvariantCulture) == value
            ? id : throw Error(label + "标识无效。");

    private static BusinessRuleException Error(string message) => new("数据库复选 " + message);
}
