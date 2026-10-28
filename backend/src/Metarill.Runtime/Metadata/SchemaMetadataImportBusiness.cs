using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data.Schema;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;

namespace Metarill.Runtime.Metadata;

public sealed record ImportedSchemaTable(string TableId, string TableName, IReadOnlyList<string> ColumnIds, IReadOnlyList<string> IndexIds);
public sealed record SchemaMetadataImportResult(string ProjectId, string PlanId, IReadOnlyList<ImportedSchemaTable> Tables)
{
    public bool Executable => false;
    public bool ExecutesDdl => false;
    public bool ConfigurationSaved => true;
    public int PagesCreated => 0;
}

public sealed record SchemaMetadataImportStatus(string ProjectId, string PlanId, string State, bool? ConfigurationSaved, IReadOnlyList<ImportedSchemaTable> Tables)
{
    public bool Executable => false;
    public bool ExecutesDdl => false;
}

/// <summary>完整配置导入由此编排；目标仅持读锁，平台配置使用原事务，不能宣称跨库分布式写事务。</summary>
public sealed class SchemaMetadataImportBusiness(SqlRowStore store, MetadataRepository metadata, MetadataRegistry registry, ProjectFileRuntimes files,
    ProjectSchemaService schemas, HttpRuntimeAuthorization authorization, SecurityRealmMutationGuard security, ConfigurationMutationObserver mutations,
    SchemaMetadataImportPlans plans, IIdGenerator ids)
{
    /// <summary>预览绑定真实会话和目标，不保存元数据；原只读草案命令仍固定canImport=false。</summary>
    public async Task<SchemaMetadataImportPlan> PrepareAsync(JsonElement request, CancellationToken token = default)
    {
        var owner = RequireOwner(); var names = Selection(request); RequireOwnTransaction();
        return await schemas.Resolve(owner.Actor.ProjectId).WithImportInspectionAsync(names, async (source, ct) =>
        {
            return await store.Session.InTransactionAsync(async () =>
            {
                var current = await security.RequireCurrentPlatformAdministratorAsync(ct);
                var catalog = files.ComposePhysical(await metadata.LoadProjectForImportAsync(current.Actor.ProjectId, ct));
                await RequireUnownedAsync(source, catalog, ct);
                var model = Model(source, catalog);
                return plans.Create(current, source, SchemaMetadataImportPlans.Fingerprint(catalog), model);
            }, ct);
        }, token);
    }

    /// <summary>只消费服务端计划ID及精确确认；结构持锁、当前身份、版本、归属和整批保存均重新复核。</summary>
    public async Task<SchemaMetadataImportResult> ApplyAsync(JsonElement request, CancellationToken token = default)
    {
        var owner = RequireOwner(); MetadataWorkbenchRules.Object(request, "planId", "confirmSha256"); RequireOwnTransaction();
        var binding = plans.Take(MetadataWorkbenchRules.Text(request, "planId"), MetadataWorkbenchRules.Text(request, "confirmSha256"), owner);
        try { return await schemas.Resolve(binding.ProjectId).WithImportInspectionAsync(binding.Names, async (source, ct) =>
        {
            if (source.Target != binding.Target || source.Snapshot.Fingerprint != binding.SourceFingerprint) throw new InvalidOperationException("源实例、数据库或结构已变化，请重新预览。");
            var result = await store.Session.InTransactionAsync(async () =>
            {
                var current = await security.RequireCurrentPlatformAdministratorAsync(ct);
                var catalog = files.ComposePhysical(await metadata.LoadProjectForImportAsync(binding.ProjectId, ct));
                if (SchemaMetadataImportPlans.Fingerprint(catalog) != binding.MetadataFingerprint) throw new MetadataRevisionConflictException();
                await RequireUnownedAsync(source, catalog, ct);
                var model = Model(source, catalog);
                if (SchemaMetadataImportPlans.Fingerprint(model) != binding.ModelFingerprint) throw new InvalidOperationException("源逻辑模型已变化，请重新预览。");
                plans.RequireNotExpired(binding);
                var saved = await SaveAsync(model, current, ct);
                // 保存后从真实配置回读，不能以将要插入的字典代替最终模型核验。
                var actual = await metadata.LoadProjectForImportAsync(binding.ProjectId, ct); var tableIds = saved.Select(table => table.TableId).ToHashSet(StringComparer.Ordinal);
                actual.ConfigTables = actual.ConfigTables.Where(table => tableIds.Contains(table.Id)).ToList();
                actual.ConfigColumns = actual.ConfigColumns.Where(column => tableIds.Contains(column.ConfigTableId)).ToList();
                actual.ConfigIndexes = actual.ConfigIndexes.Where(index => tableIds.Contains(index.GetProperty("configTableId").ToString())).ToList();
                var indexIds = actual.ConfigIndexes.Select(index => index.GetProperty("id").ToString()).ToHashSet(StringComparer.Ordinal);
                actual.ConfigIndexColumns = actual.ConfigIndexColumns.Where(column => indexIds.Contains(column.GetProperty("configIndexId").ToString())).ToList();
                actual.ConfigPages = []; actual.ConfigPageColumns = []; actual.ConfigChildren = []; actual.ConfigMenus = []; actual.ConfigButtons = [];
                var physical = MySqlSchemaPlanner.Compile(actual, source.Snapshot.Schema.Database);
                if (MySqlSchemaPlanner.Compare(source.Snapshot.Schema, physical).Count != 0 || MySqlSchemaPlanner.Compare(physical, source.Snapshot.Schema).Count != 0) throw new InvalidOperationException("保存后的逻辑模型不能无损往返，整笔配置已回滚。");
                plans.RequireNotExpired(binding);
                await security.RequireCurrentPlatformAdministratorAsync(ct);
                return new SchemaMetadataImportResult(binding.ProjectId, binding.PlanId, saved);
            }, ct);
            plans.Committed(binding, result); return result;
        }, token); }
        catch { plans.Unconfirmed(binding); throw; }
    }

    /// <summary>结果查询只返回同一身份的有限回执；unknown不声明没写入，客户端须结合原配置核实。</summary>
    public async Task<SchemaMetadataImportStatus> StatusAsync(JsonElement request, CancellationToken token = default)
    {
        RequireOwner(); MetadataWorkbenchRules.Object(request, "planId", "confirmSha256"); RequireOwnTransaction();
        return await store.Session.InTransactionAsync(async () =>
        {
            var current = await security.RequireCurrentPlatformAdministratorAsync(token);
            return plans.Status(MetadataWorkbenchRules.Text(request, "planId"), MetadataWorkbenchRules.Text(request, "confirmSha256"), current);
        }, token);
    }

    /// <summary>元数据范围锁包含墓碑及空隙；同名只有实际实例/库相同才视作同一物理对象，未知绑定拒绝猜测。</summary>
    private async Task RequireUnownedAsync(SchemaImportInspection source, MetadataCatalog catalog, CancellationToken token)
    {
        var target = source.Target ?? throw new InvalidOperationException("缺少源目标身份。");
        var comparer = target.CaseSensitiveNames ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var names = source.Snapshot.Schema.Tables.Select(table => table.Name).ToHashSet(comparer);
        if (catalog.ConfigTables.Any(table => names.Contains(table.TableName))) throw new InvalidOperationException("源表已在当前项目登记或属于当前基础设施。");
        // 安全基础表属于保留领域，不能作为普通业务表导入绕过安全运行时。
        var securityCatalog = MetadataRegistry.Load(Path.Combine(Path.GetDirectoryName(registry.PlatformPath)!, "security.json"));
        if (securityCatalog.ConfigTables.Any(table => names.Contains(table.TableName))) throw new InvalidOperationException("安全基础表不能作为普通业务结构导入。");
        var reservations = await store.ReadTableReservationsForImportAsync(token);
        var identities = new Dictionary<string,DatabaseTargetIdentity>(StringComparer.Ordinal) { [catalog.YzProjectId] = target };
        foreach (var row in reservations.Where(row => names.Contains(SqlRowStore.Text(row, "table_name"))))
        {
            var project = SqlRowStore.Text(row, "yz_project_id");
            if (!identities.TryGetValue(project, out var identity)) identities[project] = identity = await schemas.Resolve(project).InspectTargetIdentityAsync(token);
            if (identity.Fingerprint == target.Fingerprint) throw new InvalidOperationException("该物理表已有项目配置或删除墓碑，不能重复认领或恢复。");
        }
        if (registry.Platform.ConfigTables.Any(table => names.Contains(table.TableName)))
        {
            var platform = await schemas.Resolve(registry.Platform.YzProjectId).InspectTargetIdentityAsync(token);
            if (platform.Fingerprint == target.Fingerprint) throw new InvalidOperationException("不能通过导入重建平台固定基础表配置。");
        }
    }

    /// <summary>逻辑表达能力与配置类型词表同时校验，不通过改源DDL、猜类型或补列完成导入。</summary>
    private static MetadataCatalog Model(SchemaImportInspection source, MetadataCatalog current)
    {
        var draft = SchemaImportDraftCompiler.Compile(source, current.YzProjectId);
        if (!draft.ColumnsAndIndexesRepresentable) throw new InvalidOperationException("源结构存在未解决的类型、默认值、主键或约束缺口，不能保存导入计划。");
        draft.LogicalMetadata.YzProjectName = current.YzProjectName; draft.LogicalMetadata.PrimaryColor = current.PrimaryColor;
        return draft.LogicalMetadata;
    }

    /// <summary>真实ID、审计、必填布局与引用均由服务器建立；仅写新配置，不调用普通新增表的补列/建页钩子。</summary>
    private async Task<IReadOnlyList<ImportedSchemaTable>> SaveAsync(MetadataCatalog model, AuthenticatedIdentity owner, CancellationToken token)
    {
        var result = new List<ImportedSchemaTable>(); var project = long.Parse(owner.Actor.ProjectId, CultureInfo.InvariantCulture);
        foreach (var table in model.ConfigTables)
        {
            var tableId = ids.NextId(); var tableValues = Values(owner, model.YzProjectName, new() { ["id"] = tableId, ["name"] = table.Name, ["table_name"] = table.TableName, ["is_rongyu"] = 1, ["is_view"] = 0, ["is_function_table"] = 0 });
            Fill("config_table", tableValues); await store.InsertAsync("config_table", tableValues, token);
            var columns = new Dictionary<string,long>(StringComparer.Ordinal); var indexes = new List<string>();
            foreach (var column in model.ConfigColumns.Where(column => column.ConfigTableId == table.Id))
            {
                var id = ids.NextId(); columns.Add(column.Id, id);
                var values = Values(owner, model.YzProjectName, new() { ["id"] = id, ["config_table_id"] = tableId, ["config_table_name"] = table.Name, ["name"] = column.Name,
                    ["column_name"] = column.ColumnName, ["logical_type"] = column.LogicalType, ["column_type"] = column.ColumnType, ["column_length"] = column.ColumnLength,
                    ["is_db_not_null"] = column.IsDbNotNull, ["precision"] = column.Precision, ["scale"] = column.Scale, ["sort_no"] = column.SortNo, ["default_value"] = column.DefaultValue });
                ConfigurationColumnDefaults.Fill(values, registry.Platform); await store.InsertAsync("config_column", values, token);
            }
            foreach (var index in model.ConfigIndexes.Where(index => index.GetProperty("configTableId").GetString() == table.Id))
            {
                var id = ids.NextId(); indexes.Add(id.ToString(CultureInfo.InvariantCulture)); var name = index.GetProperty("indexName").GetString()!; var unique = index.GetProperty("isUnique").GetInt32();
                var values = Values(owner, model.YzProjectName, new() { ["id"] = id, ["name"] = name, ["config_table_id"] = tableId, ["config_table_name"] = table.Name, ["index_name"] = name, ["is_enabled"] = 1, ["is_unique"] = unique, ["index_type"] = unique == 1 ? "UNIQUE" : "NORMAL" });
                Fill("config_index", values); await store.InsertAsync("config_index", values, token);
                foreach (var child in model.ConfigIndexColumns.Where(child => child.GetProperty("configIndexId").GetString() == index.GetProperty("id").GetString()))
                {
                    var reference = columns[child.GetProperty("configColumnId").GetString()!]; var column = model.ConfigColumns.Single(column => column.Id == child.GetProperty("configColumnId").GetString());
                    var childValues = Values(owner, model.YzProjectName, new() { ["id"] = ids.NextId(), ["config_index_id"] = id, ["config_index_index_name"] = name, ["config_column_id"] = reference,
                        ["config_column_name"] = column.Name, ["column_name"] = column.ColumnName, ["sort_no"] = child.GetProperty("sortNo").GetInt32(), ["sort_direction"] = child.GetProperty("sortDirection").GetString()! });
                    Fill("config_index_column", childValues); await store.InsertAsync("config_index_column", childValues, token);
                }
            }
            await mutations.ValidateImportedTableAsync(tableId, project, token);
            result.Add(new(tableId.ToString(CultureInfo.InvariantCulture), table.TableName, columns.Values.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray(), indexes));
        }
        return result;
    }

    /// <summary>只补发布元模型要求的未赋值字段；已有源定义和审计值不被默认覆盖。</summary>
    private void Fill(string tableName, Dictionary<string,object?> values)
    {
        var table = registry.Platform.ConfigTables.Single(table => table.TableName == tableName);
        foreach (var column in registry.Platform.ConfigColumns.Where(column => column.ConfigTableId == table.Id && column.IsDbNotNull == 1 && !values.ContainsKey(column.ColumnName)))
            values[column.ColumnName] = column.LogicalType switch { "int32" or "int64" or "decimal" => 0, "bool" => false, _ => "" };
    }
    /// <summary>提交回执必须对应本协调器实际提交，不能在未知外层事务里提前宣称保存成功。</summary>
    private void RequireOwnTransaction()
    { if (store.Session.CurrentTransaction is not null) throw new InvalidOperationException("导入协调器必须拥有配置事务边界，不能加入未确认提交的外层事务。"); }
    /// <summary>客户端不能声明归属、时间或操作者；只使用锁后真实身份及服务端UTC。</summary>
    private static Dictionary<string,object?> Values(AuthenticatedIdentity owner, string projectName, Dictionary<string,object?> values)
    {
        values["yz_project_id"] = long.Parse(owner.Actor.ProjectId, CultureInfo.InvariantCulture);
        values["yz_project_name"] = projectName;
        values["insert_time"] = values["update_time"] = DateTime.UtcNow; values["insert_manager_id"] = values["update_manager_id"] = owner.Actor.AccountId;
        return values;
    }
    /// <summary>入口先认证，不让Agent或工作区选择绕过原平台管理员边界。</summary>
    private AuthenticatedIdentity RequireOwner() { var identity = authorization.RequireIdentity(); if (identity.GrantId is not null) throw new UnauthorizedAccessException("导入要求网页登录身份。"); authorization.RequireAdministrator(); return identity; }
    /// <summary>只接受明确的物理名称列表，不采纳任何客户端逻辑模型或连接参数。</summary>
    private static string[] Selection(JsonElement request)
    {
        MetadataWorkbenchRules.Object(request, "tableNames");
        if (!request.TryGetProperty("tableNames", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is < 1 or > 16) throw new ArgumentException("必须明确选择1至16张表。");
        var names = values.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new ArgumentException("表名必须是字符串。")).ToArray();
        foreach (var name in names) MetadataValidation.RequireIdentifier(name);
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) throw new ArgumentException("表名不能重复。"); return names;
    }
}
