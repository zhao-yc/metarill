using System.Globalization;
using System.Text.Json;

namespace Metarill.Generation;

internal sealed record TrustedSecurityProjectOptionsPlan(TableModel Source,PageModel Page,ColumnModel Column,TableModel Project,long ProjectId)
{
    internal const string Endpoint="findNameIdYzProject1696799602636988418";
}

/// <summary>仅固定可信模块的隐藏项目字段可使用本应用候选；普通List和平台多项目路径保持原样。</summary>
internal static class TrustedSecurityProjectOptionsCompilation
{
    internal const string AdminTable="1696799308939239425",AdminPage="1696799309006348290",ProjectColumn="1696799602636988418";
    /// <summary>受信完整目录/命名空间先验证，再核固定列、原隐藏声明与两列投影所需物理图。</summary>
    internal static TrustedSecurityProjectOptionsPlan? ForTable(GenerationModel model,TableModel table)
    {
        if(!TrustedSecurityServiceCompilation.Applies(model,table)||table.Source.Id!=AdminTable)return null;
        var page=table.Pages.SingleOrDefault(item=>item.Source.Id==AdminPage);
        var column=table.Columns.SingleOrDefault(item=>item.Source.Id==ProjectColumn);
        var field=model.Catalog.ConfigPageColumns.SingleOrDefault(item=>item.ConfigPageId==AdminPage&&item.ConfigColumnId==ProjectColumn);
        var project=model.Tables.SingleOrDefault(item=>item.Source.Id=="1669251289347932162"&&item.Source.TableName=="yz_project");
        bool Physical(TableModel owner,string id,string name,string type)=>owner.Columns.Any(item=>item.Source.Id==id&&item.Source.ColumnName==name&&item.Type==type&&item.Source.IsVirtualColumn==0&&!GenerationModel.Enabled(item.Source.Extra,"isJoinColumn"));
        if(page is null||column is null||field is null||field.Id!="1696799602699902978"||project is null||project.Pages.Count!=0||table.Source.TableName!="yz_admin_type"
            ||page.Source.WhereSql!="yz_project_id=@current_yz_project_id"||column.Source.ColumnName!="yz_project_id"||column.Type!="long?"||column.Source.LogicalType!="int64"
            ||column.Source.IsVirtualColumn!=0||GenerationModel.Enabled(column.Source.Extra,"isJoinColumn")||column.Source.ControlKind!="List"
            ||!string.IsNullOrEmpty(column.Source.LinkConfigTableId)&&column.Source.LinkConfigTableId!=project.Source.Id
            ||field.IsEdit!=0||field.IsShow!=0||new[]{"isInEdit","isInGrid","isInFilter"}.Any(key=>GenerationModel.Enabled(field.Extra,key))
            ||field.Extra.GetValueOrDefault("initValue").ToString()!="current_yz_project_id"
            ||!OriginalDefinition(column.Source.ControlDetail)||field.Extra.GetValueOrDefault("controlDetail").ToString() is{Length:>0} detail&&!OriginalDefinition(detail)
            ||field.Extra.GetValueOrDefault("controlKind").ToString() is{Length:>0} kind&&kind!="List"
            ||!Physical(project,"2200000000000000172","id","long")||!Physical(project,"1669255869418848258","name","string?")||!Physical(project,"2200000000000000181","delete_no","DateTime?")
            ||!long.TryParse(model.Catalog.YzProjectId,NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0||id.ToString(CultureInfo.InvariantCulture)!=model.Catalog.YzProjectId)
            throw new InvalidOperationException("可信后台项目候选的固定声明或归属图不完整，不能退化成普通项目目录。");
        return new(table,page,column,project,id);
    }
    /// <summary>构造签名与生成注册只共用这一判定，隐藏字段也不能遗漏真实授权依赖。</summary>
    internal static bool RequiresAuthorization(GenerationModel model,TableModel table)=>ForTable(model,table) is not null;
    /// <summary>原List只允许固定五键，未知SQL、重复键或目标覆盖都不进入专用计划。</summary>
    private static bool OriginalDefinition(string text)
    {
        using var parsed=JsonDocument.Parse(text);var root=parsed.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||root.EnumerateObject().Count()!=5||root.EnumerateObject().Select(item=>item.Name).Distinct(StringComparer.Ordinal).Count()!=5)return false;
        var expected=new Dictionary<string,string>{{"table","yz_project"},{"tableId","1669251289347932162"},{"nameField","name"},{"whereSql",""},{"orderBy",""}};
        return root.EnumerateObject().All(item=>item.Value.ValueKind==JsonValueKind.String&&expected.TryGetValue(item.Name,out var value)&&item.Value.GetString()==value);
    }
}
