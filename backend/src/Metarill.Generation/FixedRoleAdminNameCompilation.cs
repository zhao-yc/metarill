using System.Text.Json;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

internal sealed record FixedRoleAdminNameModel(ColumnModel Source, TableModel Admins)
{
    internal const string Alias = "yzAdminTypeName", Property = "YzAdminTypeName";
}
internal sealed record FixedRoleAdminNameResult(bool Declared, FixedRoleAdminNameModel? Definition, string? Reason);

/// <summary>只证明原角色页后台名称的固定归属图，不恢复冗余物理列或解释任意联接SQL。</summary>
internal static class FixedRoleAdminNameCompilation
{
    internal const string RoleTable = "1692532858499969026", RolePage = "1692532858529329154", AdminColumn = "1692533179766878209";
    internal const string AdminTable = "1696799308939239425", AdminPage = "1696799309006348290";

    /// <summary>固定角色页上的原字段改名或改ID仍保留缺口，不能回退ID冒充原名称。</summary>
    internal static bool Declared(TableModel table, PageModel page, ColumnMetadata source) => table.Source.Id == RoleTable && table.Source.TableName == "yz_role"
        && page.Source.Id == RolePage && (source.Id == AdminColumn || source.ColumnName == "yz_admin_type_id");

    /// <summary>来源、页覆盖、目标物理字段及名称冲突必须一起证明，客户端不参与解释。</summary>
    internal static FixedRoleAdminNameResult Resolve(GenerationModel model, TableModel table, PageModel page, ColumnModel source)
    {
        if (!Declared(table,page,source.Source)) return new(false,null,null);
        FixedRoleAdminNameResult Gap(string reason) => new(true,null,"固定角色后台名称尚未证明："+reason);
        var field=model.Catalog.ConfigPageColumns.SingleOrDefault(item=>item.ConfigPageId==page.Source.Id&&item.ConfigColumnId==source.Source.Id);
        if (source.Source.Id!=AdminColumn || source.Source.ColumnName!="yz_admin_type_id" || source.Source.ControlKind!="List" || source.Type!="long?" || source.Source.LogicalType!="int64"
            || source.Source.IsVirtualColumn!=0 || GenerationModel.Enabled(source.Source.Extra,"isJoinColumn") || table.IsGlobal || table.Source.IsRongyu!=1 || page.Scopes.Any(scope=>scope.Source=="parent")) return Gap("来源不是原非联接物理后台ID。");
        if (field?.Extra.GetValueOrDefault("controlKind").ToString() is {Length:>0} kind && kind!="List") return Gap("页面控件覆盖不是原List。");
        if (!string.IsNullOrWhiteSpace(source.Source.LinkConfigTableId) && source.Source.LinkConfigTableId!=AdminTable
            || source.Source.Extra.GetValueOrDefault("linkConfigPageId").ToString() is {Length:>0} target && target!=AdminPage
            || !string.IsNullOrWhiteSpace(source.Source.Extra.GetValueOrDefault("joinTable").ToString())) return Gap("关联表页或联接声明冲突。");
        try
        {
            if (!Definition(source.Source.ControlDetail,false) || field?.Extra.GetValueOrDefault("controlDetail").ToString() is {Length:>0} overlay && !Definition(overlay,true)) return Gap("只接受原固定table/tableId/nameField及空whereSql/orderBy，不能夹带SQL或未知键。");
        }
        catch(JsonException) { return Gap("关联明细不是有效的固定JSON对象。"); }
        if (model.Catalog.ConfigColumns.Any(column=>column.ConfigTableId==table.Source.Id&&GenerationModel.Camel(column.ColumnName).Equals(FixedRoleAdminNameModel.Alias,StringComparison.OrdinalIgnoreCase))) return Gap("计算名称与已声明字段冲突。");
        var admins=model.Tables.SingleOrDefault(item=>item.Source.Id==AdminTable&&item.Source.TableName=="yz_admin_type");
        bool Column(string id,string name,string type) => admins?.Columns.Any(column=>column.Source.Id==id&&column.Source.ColumnName==name&&column.Type==type&&column.Source.IsVirtualColumn==0&&!GenerationModel.Enabled(column.Source.Extra,"isJoinColumn"))==true;
        if (admins is null || admins.IsGlobal || admins.Source.IsRongyu!=1 || !Column("1696799602892840962","id","long") || !Column("1696799602439856130","name","string?")
            || !Column("1696799602636988418","yz_project_id","long?") || !Column("2200000000000000222","delete_no","DateTime?")) return Gap("后台ID、名称、真实项目或软删除物理图不完整。");
        var targetPage=admins.Pages.SingleOrDefault(item=>item.Source.Id==AdminPage);
        if (targetPage is null || !targetPage.Readable.Any(column=>column.Source.Id=="1696799602439856130")) return Gap("目标固定页没有可读名称声明。");
        return new(true,new(source,admins),null);
    }

    /// <summary>页级显示和回执只消费本页实际可读字段，隐藏字段不公开额外名称。</summary>
    internal static FixedRoleAdminNameModel? ForPage(GenerationModel model,TableModel table,PageModel page) => page.Readable
        .Where(column=>Declared(table,page,column.Source)).Select(column=>Resolve(model,table,page,column).Definition).OfType<FixedRoleAdminNameModel>().SingleOrDefault();

    /// <summary>公共VO保持兼容，实际名称查询仍按列表/详情自己的用途列决定；未使用用途只返回null。</summary>
    internal static bool Used(PageModel page,FixedRoleAdminNameModel definition,string operation) => (operation switch
    {
        "list" => page.ListColumns,
        "detail" => page.DetailColumns,
        _ => page.Readable
    }).Any(column=>column.Source.Id==definition.Source.Source.Id);

    /// <summary>页面覆盖仅允许继承或重复原映射；空字符串与未知键都不能变成动态查询。</summary>
    private static bool Definition(string text,bool overlay)
    {
        using var document=JsonDocument.Parse(text);
        if(document.RootElement.ValueKind!=JsonValueKind.Object)return false;
        var properties=document.RootElement.EnumerateObject().ToArray();
        if(properties.Select(item=>item.Name).Distinct(StringComparer.Ordinal).Count()!=properties.Length || properties.Any(item=>item.Value.ValueKind!=JsonValueKind.String))return false;
        var expected=new Dictionary<string,string>(StringComparer.Ordinal){{"table","yz_admin_type"},{"tableId",AdminTable},{"nameField","name"},{"whereSql",""},{"orderBy",""}};
        if(properties.Any(item=>!expected.TryGetValue(item.Name,out var value)||item.Value.GetString()!=value))return false;
        return overlay || new[]{"table","tableId","nameField"}.All(key=>document.RootElement.TryGetProperty(key,out _));
    }
}
