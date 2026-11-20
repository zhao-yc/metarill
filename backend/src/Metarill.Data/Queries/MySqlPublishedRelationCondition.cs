using Metarill.Core.Metadata;

namespace Metarill.Data.Queries;

/// <summary>已发布物理条件及其触发字段，供候选、联接及缓存传播共用。</summary>
internal sealed record MySqlPublishedRelationCondition(string Definition,IReadOnlyList<string> Fields,
    [property:System.Text.Json.Serialization.JsonIgnore] FixedConditionSqlPlan Plan)
{
    /// <summary>只接受普通数据库复选在表级实际声明的固定目标页。</summary>
    internal static PageMetadata RequireProjectCheckboxPage(MetadataCatalog catalog,TableMetadata source,ColumnMetadata relation,
        TableMetadata target,string pageId)
    {
        var definition=DatabaseCheckboxDefinition.Parse(relation.ControlDetail);
        var linkedPage=relation.Extra.GetValueOrDefault("linkConfigPageId").ToString();
        var declared=definition.PageId.Length>0?definition.PageId:linkedPage;
        var page=catalog.ConfigPages.SingleOrDefault(item=>item.Id==pageId&&item.ConfigTableId==target.Id);
        if(relation.ControlKind!="MultiCheckBoxDatabase"||relation.ConfigTableId!=source.Id||
            source.IsRongyu!=1||GlobalCrossTableTarget.Declared(source)||GlobalCrossTableTarget.Declared(target)||
            declared!=pageId||linkedPage.Length>0&&linkedPage!=pageId||
            definition.TableId.Length>0&&definition.TableId!=target.Id||definition.Table.Length>0&&definition.Table!=target.TableName||
            relation.LinkConfigTableId.Length>0&&relation.LinkConfigTableId!=target.Id||page is null||
            page.YzProjectId!=catalog.YzProjectId||target.YzProjectId!=catalog.YzProjectId||source.YzProjectId!=catalog.YzProjectId)
            throw new InvalidOperationException("复选物理缓存的目标页与发布关系不一致。");
        return page;
    }

    /// <summary>只从真实目标表解析类型；调用方已证明页面和目标同属发布目录。</summary>
    internal static MySqlPublishedRelationCondition? Compile(MetadataCatalog catalog,TableMetadata table,PageMetadata page,bool allowAccount=false,bool projectParent=false)
    {
        if(string.IsNullOrWhiteSpace(page.WhereSql))return null;
        var storage=MetadataStoragePolicy.ForTable(catalog,table);
        var physical=catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id&&GlobalPhysicalRelationValue.IsScalar(storage,table,column))
            .ToDictionary(column=>column.ColumnName,column=>column.LogicalType,StringComparer.OrdinalIgnoreCase);
        // 普通父的项目已绑定在联接范围中，固定条件只登记需独立重算的真实业务字段。
        var definition=projectParent?GlobalSingleRelationTarget.ProjectParentConstants(page,physical.Keys):page.WhereSql;
        if(definition.Length==0)return null;
        var syntax=FixedConditionPlan.Compile(definition,physical.Keys);
        if(!allowAccount&&syntax.Variables.Count!=0)throw new NotSupportedException("全局候选只支持已发布的物理常量条件。");
        return new(definition,Array.AsReadOnly(syntax.Fields.ToArray()),FixedConditionSqlPlan.Compile(syntax,physical));
    }
}
