using System.Globalization;
using System.Text.Json;
using Metarill.Core.Security;
namespace Metarill.Core.Metadata;

/// <summary>显式全局R0单值路径的发布证明；只读取已声明关系的当前值，不把逻辑名称伪装成物理缓存。</summary>
public static class GlobalDerivedRelationSource
{
    /// <summary>全局R0生产者必须真实属于当前目录；缺项目列本身不产生任何全局资格。</summary>
    public static void RequireSingle(MetadataCatalog catalog,TableMetadata source,ColumnMetadata foreign)
    {
        RequireTable(catalog,source);
        if(source.IsRongyu!=0)throw Invalid();
        RequireForeign(catalog,source,foreign);
    }

    /// <summary>沿明确的单值联接证明非凭据字符串终点，并与R1缓存共用活动字段栈，不能重置循环/深度边界。</summary>
    internal static void RequireName(MetadataCatalog catalog,TableMetadata source,ColumnMetadata name,ISet<string> resolving)
    {
        RequireTable(catalog,source);var storage=MetadataStoragePolicy.ForTable(catalog,source);
        if(source.IsRongyu!=0||name.ConfigTableId!=source.Id||name.IsVirtualColumn!=0||name.LogicalType!="string"||name.ControlKind is not("" or "TextArea")||storage.Kind(name)!=MetadataFieldStorage.Join||!Enabled(name)||ExactDataManageValues.IsSensitiveField(name.ColumnName)||!ReferenceEquals(catalog.ConfigColumns.SingleOrDefault(c=>c.Id==name.Id),name))throw Invalid();
        var path=name.Extra.GetValueOrDefault("columnFullName").ToString().Split('.');
        if(path.Length<2||path.Length>17||path.Any(string.IsNullOrWhiteSpace))throw Invalid();
        var current=source;GlobalSingleRelationPlan? last=null;
        foreach(var segment in path[..^1])
        {
            MetadataValidation.RequireIdentifier(segment);RequireTable(catalog,current);
            var fields=catalog.ConfigColumns.Where(c=>c.ConfigTableId==current.Id).ToArray();
            var foreign=fields.SingleOrDefault(c=>c.ColumnName==segment+"_id")??fields.SingleOrDefault(c=>c.ColumnName==segment)??throw Invalid();
            RequireForeign(catalog,current,foreign);
            // R1中间节点继续遵循原全局物理List生产者边界，不借R0新路径放宽旧缓存资格。
            if(current.IsRongyu==1)GlobalCachedRelationSource.RequireSingle(catalog,current,foreign);
            var target=Target(catalog,foreign);
            last=GlobalSingleRelationTarget.RequirePublishedTarget(catalog,current,foreign,target,resolving);RequireTable(catalog,target);current=target;
        }
        MetadataValidation.RequireIdentifier(path[^1]);
        var terminal=catalog.ConfigColumns.SingleOrDefault(c=>c.ConfigTableId==current.Id&&c.ColumnName==path[^1])??throw Invalid();
        if(terminal.LogicalType!="string"||ExactDataManageValues.IsSensitiveField(terminal.ColumnName))throw Invalid();
        // 常用名称就是上一步已证终点，避免同一递归链被重复展开成指数工作量。
        if(last?.Name.Id==terminal.Id)return;
        if(!GlobalPhysicalRelationValue.IsPublishedScalar(catalog,MetadataStoragePolicy.ForTable(catalog,current),current,terminal,resolving))throw Invalid();
    }

    /// <summary>真实实体、列、主键、软删及后台页归属同一目录；条件由实际选中目标页的共享证明约束。</summary>
    internal static void RequireTable(MetadataCatalog catalog,TableMetadata source)
    {
        if(!long.TryParse(catalog.YzProjectId,NumberStyles.None,CultureInfo.InvariantCulture,out var project)||project<=0||catalog.YzProjectId!=project.ToString(CultureInfo.InvariantCulture)||!GlobalCrossTableTarget.Declared(source)||source.IsRongyu is not(0 or 1)||source.IsView!=0||source.IsFunctionTable!=0||source.YzProjectId!=catalog.YzProjectId||!ReferenceEquals(catalog.ConfigTables.SingleOrDefault(t=>t.Id==source.Id),source))throw Invalid();
        var fields=catalog.ConfigColumns.Where(c=>c.ConfigTableId==source.Id).ToArray();var storage=MetadataStoragePolicy.ForTable(catalog,source);
        bool Scalar(ColumnMetadata field)=>GlobalPhysicalRelationValue.IsScalar(storage,source,field);
        var key=fields.SingleOrDefault(c=>c.ColumnName=="id");var deleted=fields.SingleOrDefault(c=>c.ColumnName=="delete_no");var scope=fields.SingleOrDefault(c=>c.ColumnName=="yz_project_id");
        if(fields.Any(c=>c.YzProjectId.Length>0&&c.YzProjectId!=catalog.YzProjectId)||key is null||key.LogicalType!="int64"||key.IsDbNotNull!=1||!Scalar(key)||deleted is not null&&(deleted.LogicalType!="datetime"||deleted.IsDbNotNull!=0||!Scalar(deleted))||scope is not null&&(scope.LogicalType is not("int64" or "string")||!Scalar(scope)))throw Invalid();
        var pages=catalog.ConfigPages.Where(p=>p.ConfigTableId==source.Id).ToArray();
        if(pages.Length==0)throw Invalid();
        foreach(var page in pages)
        {
            if(page.YzProjectId!=catalog.YzProjectId||!catalog.YzAdminTypes.Any(a=>a.Id==page.YzAdminTypeId&&a.YzProjectId==catalog.YzProjectId))throw Invalid();
            // 仅实际关联选中的目标页约束名称读取；不把同表其它后台页的父上下文借入本次路径。
        }
    }

    /// <summary>路径每一步只能经过明确的物理单值键；缓存ID、数组、文件和虚拟字段不能充当外键。</summary>
    private static void RequireForeign(MetadataCatalog catalog,TableMetadata source,ColumnMetadata foreign)
    {
        if(foreign.ConfigTableId!=source.Id||foreign.ControlKind is not("List" or "SelectPage")||!foreign.ColumnName.EndsWith("_id",StringComparison.Ordinal)||foreign.LogicalType is not("int64" or "string")||!GlobalPhysicalRelationValue.IsScalar(MetadataStoragePolicy.ForTable(catalog,source),source,foreign)||!ReferenceEquals(catalog.ConfigColumns.SingleOrDefault(c=>c.Id==foreign.Id),foreign))throw Invalid();
    }

    /// <summary>只从同目录显式表/页或原关系前缀解析目标，随后由共享证明核对冲突声明和真实目标页。</summary>
    private static TableMetadata Target(MetadataCatalog catalog,ColumnMetadata foreign)
    {
        using var document=JsonDocument.Parse(foreign.ControlDetail);var definition=document.RootElement;
        if(definition.ValueKind!=JsonValueKind.Object||definition.EnumerateObject().Select(f=>f.Name).Distinct(StringComparer.Ordinal).Count()!=definition.EnumerateObject().Count())throw Invalid();
        string Value(string key)=>!definition.TryGetProperty(key,out var value)||value.ValueKind==JsonValueKind.Null?"":value.ValueKind is JsonValueKind.String or JsonValueKind.Number?value.ToString():throw Invalid();
        var id=foreign.LinkConfigTableId.Length>0?foreign.LinkConfigTableId:Value("tableId");var page=Value("pageId");if(page.Length==0)page=foreign.Extra.GetValueOrDefault("linkConfigPageId").ToString();
        if(id.Length==0&&page.Length>0)id=catalog.ConfigPages.SingleOrDefault(p=>p.Id==page)?.ConfigTableId??throw Invalid();
        var table=Value("table");if(table.Length==0)table=foreign.Extra.GetValueOrDefault("joinTable").ToString();if(table.Length==0)table=foreign.ColumnName[..^3];
        var matches=catalog.ConfigTables.Where(t=>id.Length>0?t.Id==id:t.TableName==table).ToArray();
        if(matches.Length!=1)throw Invalid();return matches[0];
    }
    /// <summary>沿用原1/true联接标志，任意文本不构成声明。</summary>
    private static bool Enabled(ColumnMetadata column)=>column.Extra.TryGetValue("isJoinColumn",out var value)&&(value.ValueKind==JsonValueKind.True||value.ToString()=="1");
    /// <summary>固定异常只描述能力边界，不回显字段值、SQL或凭据。</summary>
    private static NotSupportedException Invalid()=>new("全局R0名称要求同目录明确发布的单值关系、固定页面和非凭据字符串终点。");
}
