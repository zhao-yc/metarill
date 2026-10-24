namespace Metarill.Generation;

internal sealed record FixedRoleCheckboxModel(ColumnModel Source, TableModel Roles, TableModel Admins, PageModel TargetPage)
{
    internal const string Alias = "yzRoleNames";
    internal string Endpoint => "findNameIdYzRole" + Source.Source.Id;
}
internal sealed record FixedRoleCheckboxResult(FixedRoleCheckboxModel? Definition, string? Reason);

/// <summary>首期只证明原账号角色复选的稳定元数据图，不把任意表名或SQL当作已支持。</summary>
internal static class FixedRoleCheckboxCompilation
{
    internal const string ManagerTable = "6031906809708544", ManagerPage = "6031906822946816", RoleColumn = "1692528723801219073";
    internal const string RoleTable = "1692532858499969026", RolePage = "1692532858529329154", AdminTable = "1696799308939239425";

    /// <summary>待验收的账号写协调属于固定安全表服务，不能因隐藏、改坏或删除角色字段声明而取消发布门禁。</summary>
    internal static bool RequiresRuntimeCoordination(TableModel table, PageModel page) => table.Source.Id == ManagerTable && table.Source.TableName == "yz_manager"
        && (page.Source.Id == ManagerPage || page.Source.IsNew == 1 || Metarill.Core.Metadata.PageUpdatePolicy.CanUpdate(page.Source)
            || page.Source.IsDelete == 1 || page.Source.IsCopy == 1 || ImportRenderer.Enabled(page));

    /// <summary>来源或页覆盖有角色复选声明时均受审，虚拟字段不能借模型物理过滤逃过门禁。</summary>
    internal static bool Declared(Metarill.Core.Metadata.ColumnMetadata source, Metarill.Core.Metadata.PageColumnMetadata? field = null) => source.ControlKind == "MultiCheckBoxDatabase"
        || field?.Extra.GetValueOrDefault("controlKind").ToString() == "MultiCheckBoxDatabase";

    /// <summary>所有来源、目标、归属及名称依赖均须为同批次明确物理列；冲突继续保留缺口。</summary>
    internal static FixedRoleCheckboxResult Resolve(GenerationModel model, TableModel table, PageModel page, ColumnModel source)
    {
        FixedRoleCheckboxResult Gap(string text) => new(null, "MultiCheckBoxDatabase固定角色复选尚未证明：" + text);
        var field = model.Catalog.ConfigPageColumns.SingleOrDefault(item => item.ConfigPageId == page.Source.Id && item.ConfigColumnId == source.Source.Id);
        if (table.Source.Id != ManagerTable || table.Source.TableName != "yz_manager" || page.Source.Id != ManagerPage || source.Source.Id != RoleColumn || source.Source.ColumnName != "yz_role_ids") return Gap("来源不是原固定账号角色字段。");
        if (source.Type != "string?" || source.Source.LogicalType != "string" || source.Source.ColumnLength != "500" || source.Source.IsVirtualColumn != 0 || GenerationModel.Enabled(source.Source.Extra,"isJoinColumn") || source.Source.ControlKind != "MultiCheckBoxDatabase" || table.Source.IsRongyu != 1 || table.IsGlobal) return Gap("来源必须为原500字符物理数组字段。");
        if (source.Source.ControlDetail != "yz_role" || field?.Extra.GetValueOrDefault("controlDetail").ToString() is {Length:>0} overlay && overlay != "yz_role"
            || field?.Extra.GetValueOrDefault("controlKind").ToString() is {Length:>0} kind && kind != "MultiCheckBoxDatabase") return Gap("目标或页面覆盖夹带未知配置，不能执行SQL或改换控件。");
        if (!string.IsNullOrWhiteSpace(source.Source.LinkConfigTableId) && source.Source.LinkConfigTableId != RoleTable || source.Source.Extra.GetValueOrDefault("linkConfigPageId").ToString() is {Length:>0} targetId && targetId != RolePage) return Gap("关联表或页面声明冲突。");
        if (!string.IsNullOrWhiteSpace(source.Source.Extra.GetValueOrDefault("joinTable").ToString())) return Gap("原角色声明不能另指定联接目标。");
        if (model.Catalog.ConfigColumns.Any(column => column.ConfigTableId == table.Source.Id && GenerationModel.Camel(column.ColumnName).Equals(FixedRoleCheckboxModel.Alias,StringComparison.OrdinalIgnoreCase))) return Gap("计算角色名称与已声明字段冲突。");
        var roles = model.Tables.SingleOrDefault(item => item.Source.Id == RoleTable && item.Source.TableName == "yz_role");
        var admins = model.Tables.SingleOrDefault(item => item.Source.Id == AdminTable && item.Source.TableName == "yz_admin_type");
        var target = roles?.Pages.SingleOrDefault(item => item.Source.Id == RolePage);
        bool Column(TableModel? owner, string id, string name, string type) => owner?.Columns.Any(column => column.Source.Id == id && column.Source.ColumnName == name && column.Type == type && column.Source.IsVirtualColumn == 0 && !GenerationModel.Enabled(column.Source.Extra,"isJoinColumn")) == true;
        if (roles is null || admins is null || target is null || roles.IsGlobal || admins.IsGlobal || roles.Source.IsRongyu!=1 || admins.Source.IsRongyu!=1 || page.Scopes.Any(scope=>scope.Source=="parent") || !string.IsNullOrWhiteSpace(target.Source.WhereSql) || !string.IsNullOrWhiteSpace(target.Source.OrderBy)
            || !Column(table,"1679698329655750657","yz_admin_type_id","long?") || !Column(roles,"2200000000000000209","id","long")
            || !Column(roles,"1692533307588292610","name","string?") || !Column(roles,"1692533179766878209","yz_admin_type_id","long?")
            || !Column(roles,"2200000000000000218","delete_no","DateTime?") || !Column(admins,"1696799602892840962","id","long")
            || !Column(admins,"1696799602636988418","yz_project_id","long?") || !Column(admins,"2200000000000000222","delete_no","DateTime?")) return Gap("角色→后台→项目归属图或物理列不完整。");
        if (!target.Readable.Any(column=>column.Source.Id=="1692533307588292610")) return Gap("目标角色页未声明可读名称。");
        if (new[]{table.Columns.Single(column=>column.Source.Id=="1679698329655750657"),roles.Columns.Single(column=>column.Source.Id=="1692533179766878209")}.Any(column=>!string.IsNullOrWhiteSpace(column.Source.LinkConfigTableId)&&column.Source.LinkConfigTableId!=AdminTable)) return Gap("后台关联边与固定目标冲突。");
        return new(new(source,roles,admins,target),null);
    }

    /// <summary>只对来源页已声明可读的固定字段接线，不能借其它页扩大VO或候选入口。</summary>
    internal static FixedRoleCheckboxModel? ForPage(GenerationModel model, TableModel table, PageModel page) => page.Readable.Any(column=>column.Source.Id==RoleColumn)
        ? Resolve(model,table,page,page.Readable.Single(column=>column.Source.Id==RoleColumn)).Definition : null;

    /// <summary>隐藏ID仍可能参与既有账号生命周期；服务校验与DI不能依赖页面读取投影。</summary>
    internal static FixedRoleCheckboxModel? ForService(GenerationModel model, TableModel table, PageModel page) => table.Columns.SingleOrDefault(column=>column.Source.Id==RoleColumn) is {} source
        ? Resolve(model,table,page,source).Definition : null;
}
