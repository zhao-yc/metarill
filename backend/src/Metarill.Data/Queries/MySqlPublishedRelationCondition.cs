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
        TableMetadata target,ColumnMetadata value,string pageId,string? sourceParentId=null)
    {
        var baseline=DatabaseCheckboxDefinition.Parse(relation.ControlDetail);
        var global=GlobalCrossTableTarget.Declared(target)
            ?GlobalMultipleRelationTarget.RequireCached(catalog,source,relation,target):null;
        var page=sourceParentId is null?global?.Page??RequireProjectCheckboxPage(catalog,source,relation,target,pageId):
            RequireProjectCheckboxParent(catalog,source,relation,target,pageId,sourceParentId).Page;
        var original=global?.Name??catalog.ConfigColumns.Single(column=>column.ConfigTableId==target.Id&&column.ColumnName==baseline.NameField);
        var storage=MetadataStoragePolicy.ForTable(catalog,target);
        var exactNonRedundant=global is {UsesNonRedundantExactCheckboxPage:true};
        bool PublishedText(ColumnMetadata label)=>exactNonRedundant
            ?GlobalMultipleRelationTarget.IsPublishedCheckboxText(catalog,target,label,page)
            :label.LogicalType=="string"&&(GlobalPhysicalRelationValue.IsScalar(storage,target,label)||
                MultiNameStorageColumns.IsDerivedCheckboxText(target,storage,label));
        // 固定页的联接条件已由实际字典页证明，可参与控件条件覆盖核对。
        var fields=storage.PhysicalColumns.Select(column=>column.ColumnName)
            .Concat(exactNonRedundant?GlobalCrossTableTarget.PageJoinedConditionFields(catalog,target,page):[])
            .ToArray();
        var storedAccount=global is {UsesStoredAccountPage:true,UsesStoredExactCheckboxPage:false}&&sourceParentId is null;
        var storedStatic=exactNonRedundant||global is {UsesProjectPage:true} or {UsesStoredPhysicalParentPage:true}||
            global is {UsesStoredExactCheckboxPage:true}&&(global.UsesStoredAccountPage||global.UsesStoredStaticPage)||
            GlobalCrossTableTarget.Declared(source)&&global is {UsesStoredStaticPage:true} or {UsesNonRedundantAccountPage:true};
        var staticCondition=storedStatic?FixedConditionPlan.Compile(page.WhereSql,fields):null;
        var projectAccount=global is null&&MySqlNonRedundantReadCompiler.IsProjectFixedMultiPage(catalog,target,page,checkboxAccount:true);
        var baselineExtra=projectAccount?baseline.AdditionalProjectConditions(page.WhereSql,fields):"";
        var ordinaryForm=global is null?baseline.AdditionalPhysicalFormConditions(catalog,target,page):null;
        // 复用精确字典的两份副本都必须有已证明的物理终点，维护按来源行绑定。
        var exactPhysical=global is {UsesStoredPhysicalParentPage:true} or {UsesStoredExactCheckboxPage:true}&&
            storage.IsPhysical(original)&&storage.IsPhysical(value);
        if(relation.ControlKind!="MultiCheckBoxDatabase"||page.Id!=pageId||
            !storedAccount&&!projectAccount&&ordinaryForm is null&&
                (staticCondition is null?!string.IsNullOrWhiteSpace(baseline.WhereSql):!baseline.IsCoveredByStaticPage(staticCondition,fields))||
            page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString().Length>0&&!exactPhysical&&!exactNonRedundant||
            !PublishedText(original)||!PublishedText(value))
            throw new InvalidOperationException("复选多名称必须使用固定目标页的已证明文本。");
        if(sourceParentId is null&&(!storage.IsPhysical(original)||!storage.IsPhysical(value)))
        {
            // 没有逻辑或账号范围时，必须证明页条件只由物理常量与普通项目归属组成。
            var scoped=MySqlNonRedundantReadCompiler.MultiTargetLogicalPage(catalog,target,relation);
            if(scoped is null)_=Compile(catalog,target,page,projectParent:global is null);
            else if(scoped.Id!=page.Id)throw new InvalidOperationException("复选名称与实际目标页范围不一致。");
        }
        if(value.Id==original.Id&&ordinaryForm is null)return page;
        var sourcePages=catalog.ConfigPages.Where(page=>page.ConfigTableId==source.Id&&page.YzProjectId==catalog.YzProjectId)
            .Select(page=>page.Id).ToHashSet(StringComparer.Ordinal);
        var overrides=catalog.ConfigPageColumns.Where(field=>field.ConfigColumnId==relation.Id&&sourcePages.Contains(field.ConfigPageId)&&
                field.Extra.GetValueOrDefault("controlKind").ToString() is "" or "MultiCheckBoxDatabase")
            .Select(field=>baseline.WithPageOverride(field.Extra.GetValueOrDefault("controlDetail").ToString())).ToArray();
        bool SameRange(DatabaseCheckboxDefinition definition)=>
            (storedAccount?baseline.HasSameCondition(definition,fields):
                staticCondition is not null?definition.IsCoveredByStaticPage(staticCondition,fields):
                projectAccount?baselineExtra==definition.AdditionalProjectConditions(page.WhereSql,fields):
                ordinaryForm is not null?ordinaryForm==definition.AdditionalPhysicalFormConditions(catalog,target,page):
                string.IsNullOrWhiteSpace(definition.WhereSql))&&
            (definition.TableId.Length==0||definition.TableId==target.Id)&&
            (definition.Table.Length==0||definition.Table==target.TableName)&&
            (definition.PageId.Length==0||definition.PageId==pageId);
        if(ordinaryForm is not null&&overrides.Any(definition=>!SameRange(definition)))
            throw new InvalidOperationException("复选名称副本各来源页须使用同一表单条件。");
        if(value.Id==original.Id)return page;
        if(!CheckboxLabelPublished(catalog,pageId,value.Id)||
            !overrides.Any(definition=>definition.NameField==value.ColumnName&&SameRange(definition)))
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
    internal static (PageMetadata Page,MultiTargetParentScope TargetParent,MultiSourceParent SourceParent) RequireProjectCheckboxParent(
        MetadataCatalog catalog,TableMetadata source,ColumnMetadata relation,TableMetadata target,string pageId,string sourceParentId)
    {
        var page=RequireProjectCheckboxPage(catalog,source,relation,target,pageId);
        var fields=MetadataStoragePolicy.ForTable(catalog,target).PhysicalColumns;
        var syntax=FixedConditionPlan.Compile(page.WhereSql,fields.Select(column=>column.ColumnName));
        var projectStoredAccount=MySqlNonRedundantReadCompiler.IsProjectFixedMultiPage(catalog,target,page,checkboxAccount:true);
        var parent=projectStoredAccount
            ?MultiTargetParentScope.CompileCheckboxPage(catalog,target,syntax,allowGlobalParent:true,allowStoredPhysicalParents:true)
            :MultiTargetParentScope.CompileProjectCheckboxPhysicalPage(catalog,target,page);
        var projectGlobalParent=projectStoredAccount&&parent is {Global:true,Logical:false};
        if(parent is null||parent.Global&&!projectGlobalParent||parent.Logical||syntax.Variables.Any(variable=>
            !MultiTargetParentScope.IsParameter(variable)&&!variable.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase))&&
            !projectStoredAccount)
            throw new InvalidOperationException("复选多名称要求实际普通物理目标父键。");
        // 重证所有来源页的完整物理父键集合，维护不能把复合范围降成第一个外键。
        var sourceKey=parent.CheckboxSource(catalog,source,relation,allowProjectPhysicalColumns:true);
        if(sourceKey.Column.Id!=sourceParentId||sourceKey.LogicalPages.Count>0)
            throw new InvalidOperationException("复选多名称要求所有来源页使用同一物理父键。");
        if(projectGlobalParent)
        {
            // 运行维护宿主重证目标与所有参与的来源范围仍指向同一个实际父页。
            _=MySqlNonRedundantReadCompiler.PublishedMultiGlobalParent(catalog,target.Id,page.Id);
            MySqlNonRedundantReadCompiler.RequireCompoundGlobalSourceParent(catalog,target.Id,page.Id,sourceKey);
        }
        return (page,parent,sourceKey);
    }

    /// <summary>只从真实目标表解析类型；调用方已证明页面和目标同属发布目录。</summary>
    internal static MySqlPublishedRelationCondition? Compile(MetadataCatalog catalog,TableMetadata table,PageMetadata page,
        bool allowAccount=false,bool projectParent=false,bool storedProject=false)
    {
        if(string.IsNullOrWhiteSpace(page.WhereSql))return null;
        var storage=MetadataStoragePolicy.ForTable(catalog,table);
        var physical=catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id&&GlobalPhysicalRelationValue.IsScalar(storage,table,column))
            .ToDictionary(column=>column.ColumnName,column=>column.LogicalType,StringComparer.OrdinalIgnoreCase);
        // 普通父的项目已绑定在联接范围中，固定条件只登记需独立重算的真实业务字段。
        var definition=projectParent?GlobalSingleRelationTarget.ProjectParentConstants(page,physical.Keys):page.WhereSql;
        if(definition.Length==0)return null;
        var syntax=FixedConditionPlan.Compile(definition,physical.Keys);
        if(storedProject)
        {
            // 项目等值已在 MySQL 范围内绑定，剩余门店和常量保持原类型及绑定时机。
            definition=string.Join(" AND ",syntax.Terms.Where(term=>!GlobalMultipleRelationTarget.IsProjectScope(term))
                .Select(term=>term.ToDefinition()));
            syntax=FixedConditionPlan.Compile(definition,physical.Keys);
            if(syntax.Terms.Count==0)return null;
        }
        if(!allowAccount&&syntax.Variables.Count!=0)throw new NotSupportedException("全局候选只支持已发布的物理常量条件。");
        return new(definition,Array.AsReadOnly(syntax.Fields.ToArray()),FixedConditionSqlPlan.Compile(syntax,physical));
    }
}
