using System.Globalization;
using System.Text.Json;
namespace Metarill.Core.Metadata;

public sealed record GlobalCachedRelationBinding(ColumnMetadata Foreign,TableMetadata Target,PageMetadata Page,ColumnMetadata Name,ColumnMetadata Cache);

/// <summary>原无项目列R1单值来源的发布证明；仅界定目录归属，不替代页面授权或真实登录身份。</summary>
public static class GlobalCachedRelationSource
{
    /// <summary>必须显式全局、同一发布目录、真实主键且完全没有项目列；不能以缺字段自动取得全局许可。</summary>
    public static void Require(MetadataCatalog catalog,TableMetadata source)
    {
        if(!long.TryParse(catalog.YzProjectId,NumberStyles.None,CultureInfo.InvariantCulture,out var project)||project<=0||catalog.YzProjectId!=project.ToString(CultureInfo.InvariantCulture))throw Invalid();
        if(!GlobalCrossTableTarget.Declared(source)||source.IsRongyu!=1||source.IsView!=0||source.IsFunctionTable!=0||source.YzProjectId!=catalog.YzProjectId||!ReferenceEquals(catalog.ConfigTables.SingleOrDefault(table=>table.Id==source.Id),source))throw Invalid();
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==source.Id).ToArray();
        if(columns.Any(column=>column.ColumnName=="yz_project_id"||column.YzProjectId.Length>0&&column.YzProjectId!=catalog.YzProjectId))throw Invalid();
        var storage=MetadataStoragePolicy.ForTable(catalog,source);var key=columns.SingleOrDefault(column=>column.ColumnName=="id");
        if(key is null||key.LogicalType!="int64"||key.IsDbNotNull!=1||!storage.IsPhysical(key)||Joined(key)||key.IsVirtualColumn!=0)throw Invalid();
        var deleted=columns.SingleOrDefault(column=>column.ColumnName=="delete_no");
        if(deleted is not null&&(deleted.LogicalType!="datetime"||deleted.IsDbNotNull!=0||!storage.IsPhysical(deleted)||Joined(deleted)||deleted.IsVirtualColumn!=0))throw Invalid();
        var pages=catalog.ConfigPages.Where(page=>page.ConfigTableId==source.Id).ToArray();
        if(pages.Length==0||pages.Any(page=>page.YzProjectId!=catalog.YzProjectId||!catalog.YzAdminTypes.Any(admin=>admin.Id==page.YzAdminTypeId&&admin.YzProjectId==catalog.YzProjectId)||page.Extra.TryGetValue("exactDataManageMatchField",out var exact)&&!string.IsNullOrWhiteSpace(exact.ToString())))throw Invalid();
    }
    /// <summary>新增路径只接本表物理List单值键；R0、多值、级联及猜测目标继续使用既有门槛。</summary>
    public static void RequireSingle(MetadataCatalog catalog,TableMetadata source,ColumnMetadata foreign)
    {
        Require(catalog,source);var storage=MetadataStoragePolicy.ForTable(catalog,source);
        if(!ReferenceEquals(catalog.ConfigColumns.SingleOrDefault(column=>column.Id==foreign.Id),foreign)||foreign.ConfigTableId!=source.Id||foreign.ControlKind!="List"||!foreign.ColumnName.EndsWith("_id",StringComparison.Ordinal)||foreign.LogicalType is not("int64" or "string")||!storage.IsPhysical(foreign)||foreign.IsVirtualColumn!=0||Joined(foreign))throw Invalid();
    }
    /// <summary>物理名称副本须有精确单层路径和同一List目标，不能把任意join列作为全局写入。</summary>
    public static GlobalCachedRelationBinding RequireCache(MetadataCatalog catalog,TableMetadata source,ColumnMetadata cache)
    {
        Require(catalog,source);var storage=MetadataStoragePolicy.ForTable(catalog,source);var path=cache.Extra.GetValueOrDefault("columnFullName").ToString().Split('.');
        if(!ReferenceEquals(catalog.ConfigColumns.SingleOrDefault(column=>column.Id==cache.Id),cache)||cache.ConfigTableId!=source.Id||path.Length!=2||!Joined(cache)||cache.IsVirtualColumn!=0||!storage.IsPhysical(cache)||cache.LogicalType!="string"||cache.ControlKind is not("" or "TextArea")||cache.LinkConfigTableId.Length>0||cache.Extra.GetValueOrDefault("joinTable").ToString().Length>0||cache.Extra.GetValueOrDefault("linkConfigPageId").ToString().Length>0)throw Invalid();
        var foreign=catalog.ConfigColumns.SingleOrDefault(column=>column.ConfigTableId==source.Id&&(column.ColumnName==path[0]+"_id"||column.ColumnName==path[0]));
        if(foreign is null)throw Invalid();RequireSingle(catalog,source,foreign);
        using var definition=JsonDocument.Parse(foreign.ControlDetail);var root=definition.RootElement;
        string Value(string key)=>root.TryGetProperty(key,out var value)&&value.ValueKind is JsonValueKind.String or JsonValueKind.Number?value.ToString():"";
        var targetId=foreign.LinkConfigTableId.Length>0?foreign.LinkConfigTableId:Value("tableId");var name=Value("table");
        var targets=catalog.ConfigTables.Where(table=>targetId.Length>0?table.Id==targetId:table.TableName==name).ToArray();if(targets.Length!=1||!GlobalCrossTableTarget.Declared(targets[0]))throw Invalid();
        var proof=GlobalSingleRelationTarget.RequireCached(catalog,source,foreign,targets[0]);var stem=foreign.ColumnName[..^3];
        if(path[1]!=proof.Name.ColumnName||cache.ColumnName!=stem+"_"+proof.Name.ColumnName)throw Invalid();
        return new(foreign,targets[0],proof.Page,proof.Name,cache);
    }
    /// <summary>与现有元数据标志一致，只承认1/true。</summary>
    private static bool Joined(ColumnMetadata column)=>column.Extra.TryGetValue("isJoinColumn",out var value)&&(value.ValueKind==JsonValueKind.True||value.ToString()=="1");
    /// <summary>不能降级为无发布证明的全局来源。</summary>
    private static NotSupportedException Invalid()=>new("无项目列的全局缓存来源必须为当前目录明确发布、有真实主键和后台页面的R1物理List来源。");
}
