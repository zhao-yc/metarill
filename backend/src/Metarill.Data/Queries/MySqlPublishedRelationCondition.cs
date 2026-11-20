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

    /// <summary>固定目标页只接受原名称和实际来源页声明且目标页开放的文本。</summary>
    internal static PageMetadata RequireSharedCheckboxCachePage(MetadataCatalog catalog,TableMetadata source,ColumnMetadata relation,
        TableMetadata target,ColumnMetadata value,string pageId)
    {
        var baseline=DatabaseCheckboxDefinition.Parse(relation.ControlDetail);
        var global=GlobalCrossTableTarget.Declared(target)
            ?GlobalMultipleRelationTarget.RequireCached(catalog,source,relation,target):null;
        var page=global?.Page??RequireProjectCheckboxPage(catalog,source,relation,target,pageId);
        var original=global?.Name??catalog.ConfigColumns.Single(column=>column.ConfigTableId==target.Id&&column.ColumnName==baseline.NameField);
        var storage=MetadataStoragePolicy.ForTable(catalog,target);
        bool PublishedText(ColumnMetadata label)=>label.LogicalType=="string"&&
            (GlobalPhysicalRelationValue.IsScalar(storage,target,label)||
             MultiNameStorageColumns.IsDerivedCheckboxText(target,storage,label)&&
             MySqlNonRedundantReadCompiler.MultiTargetLogicalPage(catalog,target,relation)?.Id==pageId);
        if(relation.ControlKind!="MultiCheckBoxDatabase"||page.Id!=pageId||
            !string.IsNullOrWhiteSpace(baseline.WhereSql)||page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString().Length>0||
            !PublishedText(original)||!PublishedText(value))
            throw new InvalidOperationException("复选多名称必须使用固定目标页的已证明文本。");
        if(value.Id==original.Id)return page;

        var sourcePages=catalog.ConfigPages.Where(page=>page.ConfigTableId==source.Id&&page.YzProjectId==catalog.YzProjectId)
            .Select(page=>page.Id).ToHashSet(StringComparer.Ordinal);
        bool PublishedOverride(PageColumnMetadata field)
        {
            var kind=field.Extra.GetValueOrDefault("controlKind").ToString();
            if(kind.Length>0&&kind!="MultiCheckBoxDatabase")return false;
            var definition=baseline.WithPageOverride(field.Extra.GetValueOrDefault("controlDetail").ToString());
            return definition.NameField==value.ColumnName&&string.IsNullOrWhiteSpace(definition.WhereSql)&&
                (definition.TableId.Length==0||definition.TableId==target.Id)&&
                (definition.Table.Length==0||definition.Table==target.TableName)&&
                (definition.PageId.Length==0||definition.PageId==pageId);
        }
        if(!CheckboxLabelPublished(catalog,pageId,value.Id)||
            !catalog.ConfigPageColumns.Any(field=>field.ConfigColumnId==relation.Id&&sourcePages.Contains(field.ConfigPageId)&&PublishedOverride(field)))
            throw new InvalidOperationException("复选名称副本缺少已发布的来源覆盖或目标显示字段。");
        return page;
    }

    /// <summary>页级名称只能读取目标页已发布的表格或详情字段。</summary>
    internal static bool CheckboxLabelPublished(MetadataCatalog catalog,string pageId,string columnId)=>
        catalog.ConfigPageColumns.Any(field=>field.ConfigPageId==pageId&&field.ConfigColumnId==columnId&&
            (PublishedFlag(field,"isInGrid",field.IsShow)||PublishedFlag(field,"isInEdit",field.IsEdit)));

    /// <summary>读取与生成模板一致的原表格和详情发布标记。</summary>
    private static bool PublishedFlag(PageColumnMetadata field,string key,int fallback)=>
        field.Extra.TryGetValue(key,out var value)
            ? value.ValueKind==System.Text.Json.JsonValueKind.True||value.ToString()=="1"
            : fallback==1;

    /// <summary>按发布页重证来源行使用的物理父键，避免维护和改名只按目标 ID 读取。</summary>
    internal static (PageMetadata Page,MultiTargetParentScope TargetParent,ColumnMetadata SourceParent) RequireProjectCheckboxParent(
        MetadataCatalog catalog,TableMetadata source,ColumnMetadata relation,TableMetadata target,string pageId,string sourceParentId)
    {
        var page=RequireProjectCheckboxPage(catalog,source,relation,target,pageId);
        var fields=MetadataStoragePolicy.ForTable(catalog,target).PhysicalColumns;
        var syntax=FixedConditionPlan.Compile(page.WhereSql,fields.Select(column=>column.ColumnName));
        var parent=MultiTargetParentScope.CompileCheckboxParent(catalog,target,syntax);
        if(parent is null||parent.Global||parent.Logical||syntax.Variables.Any(variable=>
            !MultiTargetParentScope.IsParameter(variable)&&!variable.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("复选多名称要求实际普通物理目标父键。");
        var sourcePages=catalog.ConfigPages.Where(candidate=>candidate.ConfigTableId==source.Id).ToArray();
        ColumnMetadata? sourceKey=null;
        foreach(var published in sourcePages)
        {
            var bound=parent.CheckboxSource(catalog,source,relation,published,allowFormParent:true);
            if(bound.Column.Id!=sourceParentId||bound.LogicalPages.Count>0||bound.AdditionalColumns.Count>0)
                throw new InvalidOperationException("复选多名称要求所有来源页使用同一物理父键。");
            sourceKey=bound.Column;
        }
        return (page,parent,sourceKey??throw new InvalidOperationException("复选多名称缺少实际来源页父级。"));
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
