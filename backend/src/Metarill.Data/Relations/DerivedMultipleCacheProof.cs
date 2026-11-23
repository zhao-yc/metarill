using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
namespace Metarill.Data.Relations;

/// <summary>派生多选／数据库复选分别遵循原目标声明，名称缓存不开放任意数组或请求SQL。</summary>
internal static class DerivedMultipleCacheProof
{
    /// <summary>全局业务数组回填只接受同表原多选及其实际固定账号目标页。</summary>
    internal static bool IsStoredGlobalSource(MetadataCatalog catalog,TableMetadata source,RelationCacheDefinition binding)
    {
        if(!binding.Multiple||!GlobalCrossTableTarget.Declared(source)||binding.SourceTableId!=source.Id)return false;
        var foreign=catalog.ConfigColumns.SingleOrDefault(column=>column.Id==binding.ForeignColumnId&&column.ConfigTableId==source.Id);
        var target=catalog.ConfigTables.SingleOrDefault(table=>table.Id==binding.TargetTableId);
        return foreign is {ControlKind:"MultiSelectPage"}&&target is {IsRongyu:1}&&GlobalCrossTableTarget.Declared(target)&&
            MySqlNonRedundantReadCompiler.MultiTargetLogicalPage(catalog,target,foreign) is not null&&
            GlobalMultipleRelationTarget.RequireCached(catalog,source,foreign,target).Name.Id==binding.ValueColumnId;
    }

    /// <summary>数据层独立核对生成器声明；派生值仍须由单值匹配编译器取得终点证明。</summary>
    internal static void Require(MetadataCatalog catalog,TableMetadata source,ColumnMetadata foreign,TableMetadata target,ColumnMetadata value,ColumnMetadata cache,
        string? checkboxTargetPageId,string? checkboxSourceParentId=null)
    {
        if(foreign.LogicalType!="string"||!foreign.ColumnName.EndsWith("_ids",StringComparison.Ordinal))throw Invalid();
        var global=GlobalCrossTableTarget.Declared(target);
        var sharedPage=checkboxTargetPageId is null?null:
            MySqlPublishedRelationCondition.RequireSharedCheckboxCachePage(catalog,source,foreign,target,value,checkboxTargetPageId,checkboxSourceParentId);
        if(sharedPage is null&&global&&GlobalMultipleRelationTarget.RequireCached(catalog,source,foreign,target).Name.Id!=value.Id)
            throw Invalid();
        if(foreign.ControlKind=="MultiCheckBoxDatabase")
        {
            RequireCheckbox(catalog,foreign,target,value,cache,sharedPage is not null);
            RequireValue(catalog,source,foreign,target,value,sharedPage);
            return;
        }
        if(foreign.ControlKind!="MultiSelectPage")throw Invalid();using var doc=JsonDocument.Parse(foreign.ControlDetail);var root=doc.RootElement;if(root.ValueKind!=JsonValueKind.Object||root.EnumerateObject().Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=root.EnumerateObject().Count())throw Invalid();
        string Text(string key,string fallback="")=>!root.TryGetProperty(key,out var v)||v.ValueKind==JsonValueKind.Null?fallback:v.ValueKind==JsonValueKind.String?string.IsNullOrWhiteSpace(v.GetString())?fallback:v.GetString()!:
            v.ValueKind==JsonValueKind.Number&&key is "pageId" or "tableId"&&v.TryGetInt64(out var id)
                ?id.ToString(System.Globalization.CultureInfo.InvariantCulture):throw Invalid();
        var pageId=Text("pageId",foreign.Extra.GetValueOrDefault("linkConfigPageId").ToString());var page=catalog.ConfigPages.SingleOrDefault(p=>p.Id==pageId&&p.ConfigTableId==target.Id);if(page is null||page.Extra.GetValueOrDefault("type").ToString()!="2"||Text("nameField","name")!=value.ColumnName||Text("whereSql").Length>0||new[]{"sql","where"}.Any(k=>root.TryGetProperty(k,out _))||Text("tableId") is{Length:>0} tableId&&tableId!=target.Id||Text("table") is{Length:>0} tableName&&tableName!=target.TableName)throw Invalid();
        if(!catalog.ConfigPageColumns.Any(f=>f.ConfigPageId==page.Id&&f.ConfigColumnId==value.Id)||page.YzProjectId.Length>0&&page.YzProjectId!=catalog.YzProjectId||foreign.Extra.GetValueOrDefault("linkConfigPageId").ToString() is{Length:>0} link&&link!=page.Id)throw Invalid();
        var stem=foreign.ColumnName[..^4];if(cache.ColumnName!=stem+"_"+(value.ColumnName.EndsWith('s')?value.ColumnName:value.ColumnName+"s"))throw Invalid();
        RequireValue(catalog,source,foreign,target,value);
    }
    /// <summary>原复选按来源动作授权，目标页只用于关系归属，不借用type=2或目标列表权限。</summary>
    private static void RequireCheckbox(MetadataCatalog catalog,ColumnMetadata foreign,TableMetadata target,ColumnMetadata value,ColumnMetadata cache,
        bool sharedName)
    {
        var definition=DatabaseCheckboxDefinition.Parse(foreign.ControlDetail);
        if(!sharedName&&definition.NameField!=value.ColumnName||cache.ColumnName!=foreign.ColumnName[..^4]+"_"+(value.ColumnName.EndsWith('s')?value.ColumnName:value.ColumnName+"s"))throw Invalid();
        if(foreign.LinkConfigTableId.Length>0&&foreign.LinkConfigTableId!=target.Id||definition.TableId.Length>0&&definition.TableId!=target.Id||definition.Table.Length>0&&definition.Table!=target.TableName)throw Invalid();
        var joined=foreign.Extra.GetValueOrDefault("joinTable").ToString();var linked=foreign.Extra.GetValueOrDefault("linkConfigPageId").ToString();
        if(joined.Length>0&&joined!=target.TableName||linked.Length>0&&definition.PageId.Length>0&&linked!=definition.PageId)throw Invalid();
        foreach(var pageId in new[]{linked,definition.PageId}.Where(id=>id.Length>0).Distinct(StringComparer.Ordinal))
        {
            var page=catalog.ConfigPages.SingleOrDefault(p=>p.Id==pageId&&p.ConfigTableId==target.Id);
            if(page is null||page.YzProjectId.Length>0&&page.YzProjectId!=catalog.YzProjectId)throw Invalid();
        }
        // 原四段前两项不决定目标；无有效声明才采用旧目标字面值或字段前缀，与原候选解析保持一致。
        if(foreign.LinkConfigTableId.Length==0&&definition.TableId.Length==0&&definition.Table.Length==0&&joined.Length==0&&linked.Length==0&&definition.PageId.Length==0)
        {
            var hint=definition.LegacyTargetHint.Length>0?definition.LegacyTargetHint:foreign.ColumnName[..^4];if(hint!=target.TableName)throw Invalid();
        }
    }

    /// <summary>缓存重算只沿已存 ID 读取名称；整数别名必须与物理终点类型相同。</summary>
    private static void RequireValue(MetadataCatalog catalog,TableMetadata source,ColumnMetadata foreign,TableMetadata target,
        ColumnMetadata value,PageMetadata? sharedPage=null)
    {
        var storage=MetadataStoragePolicy.ForTable(catalog,target);
        var scoped=sharedPage??MySqlNonRedundantReadCompiler.MultiTargetLogicalPage(catalog,target,foreign);
        var typed=scoped is not null&&storage.IsPhysical(value)
            ?MultiNameStorageColumns.IsPhysicalScalarLabel(source,foreign,target,value,storage)
            :MultiNameStorageColumns.IsDerivedScalarLabel(target,value,storage);
        if(value.LogicalType!="string"&&!typed)throw Invalid();
        // 验证也读取与实际名称副本相同的固定页及联接条件。
        if(target.IsRongyu==1&&foreign.ControlKind=="MultiSelectPage"&&scoped is not null)
        {
            _=AccountScopedCrossCache.Compile(catalog,source,target,scoped,globalTreeSource:false,
                globalSingleSource:GlobalCrossTableTarget.Declared(source));
            var scopedRead=MySqlNonRedundantReadCompiler.CompileScopedMultiLabel(catalog,target.Id,scoped.Id,
                value.ColumnName,[value.ColumnName],[]);
            if(scopedRead.Types[value.ColumnName]!=value.LogicalType)throw Invalid();
            return;
        }
        var read=MySqlNonRedundantReadCompiler.CompileMultiLabelForCache(catalog,source,target,value.ColumnName,scoped,foreign,sharedPage is not null);
        if(read.Types[value.ColumnName]!=value.LogicalType)throw Invalid();
    }
    /// <summary>异常不包含目录原文或任何连接配置。</summary>
    private static NotSupportedException Invalid()=>new("派生多值缓存缺少固定目标、单值名称或辅助列证明。");
}
