using Metarill.Core.Metadata;
using Metarill.Core.Security;
namespace Metarill.Data.Relations;

/// <summary>已证明的跨表、自树及多选派生缓存进入完整维护／混合队列，物理传播器不处理逻辑字段。</summary>
public static class DerivedCrossTableCaches
{
    /// <summary>逐项验证来源、固定层与R0派生终点；只编译，不连接业务库。</summary>
    public static void Validate(MetadataCatalog catalog,IReadOnlyList<RelationCacheDefinition> derived,IReadOnlyList<RelationCacheDefinition> physical)
    {
        MetadataValidation.Validate(catalog);if(catalog.DatabaseProvider!="mysql")throw new NotSupportedException("派生跨表缓存尚未完成此方言验收。");var all=physical.Concat(derived).ToArray();
        foreach(var d in derived)
        {
            var source=catalog.ConfigTables.SingleOrDefault(t=>t.Id==d.SourceTableId)??throw new InvalidOperationException("跨表来源未发布。");var target=catalog.ConfigTables.SingleOrDefault(t=>t.Id==d.TargetTableId)??throw new InvalidOperationException("派生目标未发布。");
            var checkboxPage=d.CheckboxTargetPageId is {} formPageId
                ?catalog.ConfigPages.SingleOrDefault(page=>page.Id==formPageId&&page.ConfigTableId==target.Id):null;
            var checkboxRelation=checkboxPage is null?null:
                catalog.ConfigColumns.SingleOrDefault(column=>column.Id==d.ForeignColumnId&&column.ConfigTableId==source.Id);
            var projectForm=d.Multiple&&checkboxPage is not null&&checkboxRelation is {ControlKind:"MultiCheckBoxDatabase"}&&
                DatabaseCheckboxDefinition.Parse(checkboxRelation.ControlDetail).AdditionalPhysicalFormConditions(catalog,target,checkboxPage) is not null;
            if(d.CheckboxSourceParentId is not null)
            {
                if(!d.Multiple||d.CheckboxTargetPageId is null)
                    throw new InvalidOperationException("复选父级缓存缺少实际发布的多值目标页。");
                var targetValue=RelationStorageColumns.Expand(target,catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id))
                    .Single(column=>column.Id==d.ValueColumnId);
                var storedAccount=checkboxPage is not null&&Metarill.Data.Queries.NonRedundantReads
                    .IsProjectStoredCheckboxAccountPage(catalog,target,checkboxPage);
                var relation=catalog.ConfigColumns.Single(column=>column.Id==d.ForeignColumnId&&column.ConfigTableId==source.Id);
                var scoped=Metarill.Data.Queries.NonRedundantReads.HasScopedMultiPage(catalog,target,relation);
                if(MetadataStoragePolicy.ForTable(catalog,target).IsPhysical(targetValue)&&!storedAccount&&!scoped&&!projectForm)
                {
                    // 普通物理副本沿用原列证明；已带固定范围的副本走下方完整维护计划。
                    _=RelationMutationPlan.Compile(catalog,[d with{CheckboxTargetPageId=null,CheckboxSourceParentId=null}]);
                    var cacheColumn=RelationStorageColumns.Expand(source,catalog.ConfigColumns.Where(column=>column.ConfigTableId==source.Id))
                        .Single(column=>column.Id==d.CacheColumnId);
                    _=Metarill.Data.Queries.MySqlNonRedundantReadCompiler.CompileRefresh(catalog,source.Id,[cacheColumn.ColumnName],[d]);
                    continue;
                }
            }
            if(d.ScopedSinglePageId is not null)
            {
                RequireScopedSingle(catalog,source,target,d);
                continue;
            }
            var global=GlobalCrossTableTarget.Declared(target);
            var globalSource=GlobalCrossTableTarget.Declared(source);
            var globalTree=globalSource&&d.SelfTreePageId is not null;
            var globalMultiple=globalSource&&DerivedMultipleCacheProof.IsStoredGlobalSource(catalog,source,d);
            if(source.IsRongyu!=1||
                globalSource&&!globalTree&&!globalMultiple||source.IsView!=0||source.IsFunctionTable!=0||target.IsView!=0||target.IsFunctionTable!=0)
                throw new InvalidOperationException("派生级联缓存要求已发布的R1来源和固定单值目标。");
            var sourceStorage=MetadataStoragePolicy.ForTable(catalog,source);var targetStorage=MetadataStoragePolicy.ForTable(catalog,target);var columns=RelationStorageColumns.Expand(source,catalog.ConfigColumns.Where(c=>c.ConfigTableId==source.Id));var foreign=columns.SingleOrDefault(c=>c.Id==d.ForeignColumnId);var cache=columns.SingleOrDefault(c=>c.Id==d.CacheColumnId);var value=RelationStorageColumns.Expand(target,catalog.ConfigColumns.Where(c=>c.ConfigTableId==target.Id)).SingleOrDefault(c=>c.Id==d.ValueColumnId);
            var scopedTree=SelfTreeCache.UsesWholePath(catalog,source,d.SelfTreePageId);
            var scopedMultiple=projectForm||d.Multiple&&foreign is not null&&
                Metarill.Data.Queries.NonRedundantReads.HasScopedMultiPage(catalog,target,foreign);
            if(target.IsRongyu!=0&&d.CrossTableAccountPageId is null&&!globalTree&&!scopedMultiple)
                throw new InvalidOperationException("派生级联缓存要求已发布的R1来源和固定单值目标。");
            if(foreign is null||cache is null||value is null||foreign.Id==cache.Id||!sourceStorage.IsPhysical(foreign)||Enabled(foreign)||!sourceStorage.IsPhysical(cache)||cache.IsVirtualColumn!=0||cache.LogicalType!="string"||foreign.LogicalType is not("string" or "int64")||value.IsVirtualColumn!=0||
                (targetStorage.IsPhysical(value)?d.CrossTableAccountPageId is null&&d.CrossTableRecursivePageId is null&&!scopedMultiple&&!scopedTree:targetStorage.Kind(value)!=MetadataFieldStorage.Join&&!(d.Multiple&&targetStorage.Kind(value)==MetadataFieldStorage.RelationDisplay)))throw new InvalidOperationException("派生跨表关系ID、缓存或逻辑名称不明确。");
            if(cache.ControlKind is not("" or "TextArea")||cache.LinkConfigTableId.Length>0||cache.Extra.GetValueOrDefault("joinTable").ToString().Length>0||cache.Extra.GetValueOrDefault("linkConfigPageId").ToString().Length>0||ExactDataManageValues.IsSensitiveField(cache.ColumnName)||foreign.LinkConfigTableId.Length>0&&foreign.LinkConfigTableId!=target.Id)throw new InvalidOperationException("派生跨表缓存不属于同一固定关系。");
            foreach(var table in new[]{source,target})
            {
                if(table.YzProjectId.Length>0&&table.YzProjectId!=catalog.YzProjectId)throw new InvalidOperationException("派生跨表实体超出发布项目。");var globalTarget=global&&table.Id==target.Id||globalTree&&table.Id==source.Id;var storage=MetadataStoragePolicy.ForTable(catalog,table);var members=RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(c=>c.ConfigTableId==table.Id));var key=members.SingleOrDefault(c=>c.ColumnName=="id");var project=members.SingleOrDefault(c=>c.ColumnName=="yz_project_id");var deleted=members.SingleOrDefault(c=>c.ColumnName=="delete_no");if(key is null||key.LogicalType!="int64"||key.IsDbNotNull!=1||!storage.IsPhysical(key)||Enabled(key)||!globalTarget&&project is null||project is not null&&(project.LogicalType is not("int64" or "string")||!storage.IsPhysical(project)||Enabled(project))||deleted is not null&&(deleted.LogicalType!="datetime"||deleted.IsDbNotNull!=0||!storage.IsPhysical(deleted)||Enabled(deleted)))throw new InvalidOperationException("派生跨表实体缺少真实身份或活动范围。");
            }
            foreach(var field in new[]{foreign,cache,value})if(field.YzProjectId.Length>0&&field.YzProjectId!=catalog.YzProjectId)throw new InvalidOperationException("派生跨表字段超出发布项目。");
            var cross=CrossTableCacheProof.Require(catalog,source,foreign,target,value,cache,d,all);
            if(d.CrossTableRecursivePageId is not null)
            {
                if(!cross||d.Multiple||d.SelfTreePageId is not null||d.CrossTableAccountPageId is not null)
                    throw new InvalidOperationException("递归名称缓存必须绑定单一固定跨表层。");
                var (page,_,treeNames)=CrossTableCacheProof.RequireRecursivePage(catalog,target,value,d);
                _=Metarill.Data.Queries.AccountScopedCrossCache.CompileRecursive(catalog,source,target,page,selfTreeFields:treeNames);
                // 内层账号范围也须由存储行证明，发布时直接编译整条回填表达式。
                if(treeNames.Count>0)
                    _=Metarill.Data.Queries.MySqlNonRedundantReadCompiler.CompileRefresh(catalog,source.Id,[cache.ColumnName],[d]);
            }
            if(d.CrossTableAccountPageId is{} pageId)
            {
                if(!cross||d.Multiple||d.SelfTreePageId is not null)throw new InvalidOperationException("账号过滤仅支持固定跨表名称缓存。");
                var page=catalog.ConfigPages.SingleOrDefault(item=>item.Id==pageId&&item.ConfigTableId==target.Id)
                    ??throw new InvalidOperationException("账号过滤名称缓存缺少已发布目标页。");
                _=Metarill.Data.Queries.AccountScopedCrossCache.Compile(catalog,source,target,page);
            }
            var self=SelfTreeCache.Require(catalog,source,foreign,target,value,cache,d,cross);
            if(d.Multiple)DerivedMultipleCacheProof.Require(catalog,source,foreign,target,value,cache,d.CheckboxTargetPageId,d.CheckboxSourceParentId);
            else if(!cross&&!self)throw new InvalidOperationException("逻辑缓存缺少跨表层或固定自树证明。");
            // 附加表单范围必须通过实际来源行的完整刷新计划，基础名称验证只编译固定目标页。
            var projectCheckboxControl=d.Multiple&&foreign.ControlKind=="MultiCheckBoxDatabase"&&
                !string.IsNullOrWhiteSpace(Metarill.Core.Metadata.DatabaseCheckboxDefinition.Parse(foreign.ControlDetail).WhereSql)&&
                Metarill.Data.Queries.MySqlNonRedundantReadCompiler.MultiTargetLogicalPage(catalog,target,foreign) is {} controlPage&&
                Metarill.Data.Queries.NonRedundantReads.IsProjectStoredCheckboxAccountPage(catalog,target,controlPage);
            if(d.CheckboxSourceParentId is not null||projectCheckboxControl||projectForm)
                _=Metarill.Data.Queries.MySqlNonRedundantReadCompiler.CompileRefresh(catalog,source.Id,[cache.ColumnName],[d]);
            // 嵌套叶名条件的归属要在整条 SQL 中证明，仅凭外层候选能查到不能发布副本。
            if(self&&SelfTreeCache.HasSelfTreeScopedPage(catalog,d.SelfTreePageId))
                _=Metarill.Data.Queries.MySqlNonRedundantReadCompiler.CompileRefresh(catalog,source.Id,[cache.ColumnName],[d]);
            if(Enabled(cache)){var stem=foreign.ColumnName[..(foreign.ColumnName.Length-(d.Multiple?4:3))];var path=cache.Extra.GetValueOrDefault("columnFullName").ToString();if(!(self&&path.Length==0)&&path!=stem+"."+value.ColumnName&&path!=foreign.ColumnName+"."+value.ColumnName)throw new InvalidOperationException("派生级联缓存路径与固定定义不一致。");}
        }
        if(all.GroupBy(d=>(d.SourceTableId,d.CacheColumnId)).Any(g=>g.Distinct().Count()!=1))throw new InvalidOperationException("同一缓存存在相互冲突的物理／派生绑定。");
    }
    /// <summary>按实际维护计划保留固定单值、复选及跨表层名称联接涉及的表。</summary>
    public static IReadOnlySet<string> ScopedSingleReferencedTables(MetadataCatalog catalog,IReadOnlyList<RelationCacheDefinition> definitions)
    {
        var referenced=new HashSet<string>(StringComparer.Ordinal);
        var byTable=definitions.Where(definition=>definition.ScopedSinglePageId is not null||HasJoinedCheckboxName(definition)||HasJoinedCascadeName(definition))
            .GroupBy(definition=>definition.SourceTableId);
        foreach(var group in byTable)
        {
            var bindings=group.ToArray();
            var source=catalog.ConfigTables.Single(table=>table.Id==group.Key);
            var columns=RelationStorageColumns.Expand(source,catalog.ConfigColumns.Where(column=>column.ConfigTableId==source.Id));
            var outputs=bindings.Select(binding=>columns.Single(column=>column.Id==binding.CacheColumnId).ColumnName).ToArray();
            var read=Metarill.Data.Queries.MySqlNonRedundantReadCompiler.CompileRefresh(catalog,group.Key,outputs,bindings);
            referenced.UnionWith(read.PhysicalReferences.Select(reference=>reference.Table));
        }
        return referenced;

        bool HasJoinedCascadeName(RelationCacheDefinition definition)
        {
            if(definition.CrossTableColumnId is null&&definition.SelfTreePageId is null)return false;
            var target=catalog.ConfigTables.Single(table=>table.Id==definition.TargetTableId);
            if(target.IsRongyu!=0)return false;
            var label=catalog.ConfigColumns.SingleOrDefault(column=>column.Id==definition.ValueColumnId);
            return label is not null&&MetadataStoragePolicy.ForTable(catalog,target).Kind(label)==MetadataFieldStorage.Join;
        }

        bool HasJoinedCheckboxName(RelationCacheDefinition definition)
        {
            if(!definition.Multiple||definition.CheckboxTargetPageId is null)return false;
            var target=catalog.ConfigTables.Single(table=>table.Id==definition.TargetTableId);
            var label=catalog.ConfigColumns.SingleOrDefault(column=>column.Id==definition.ValueColumnId);
            return label is not null&&MultiNameStorageColumns.IsDerivedCheckboxText(target,MetadataStoragePolicy.ForTable(catalog,target),label);
        }
    }

    /// <summary>固定单值副本只绑定原表级关系、同类型物理缓存及实际目标页。</summary>
    private static void RequireScopedSingle(MetadataCatalog catalog,TableMetadata source,TableMetadata target,RelationCacheDefinition definition)
    {
        if(source.IsRongyu!=1||definition.Multiple||
            definition.CrossTableColumnId is not null||definition.CrossTableLevelKey is not null||
            definition.CrossTablePageId is not null||definition.CrossTableGlobalPages is not null||
            definition.CrossTableRecursivePageId is not null||definition.CrossTableAccountPageId is not null||definition.SelfTreePageId is not null)
            throw new InvalidOperationException("固定单值缓存要求已证明的冗余来源及目标页。");
        var fields=catalog.ConfigColumns.Where(column=>column.ConfigTableId==source.Id).ToArray();
        var foreign=fields.Single(column=>column.Id==definition.ForeignColumnId);
        var cache=fields.Single(column=>column.Id==definition.CacheColumnId);
        var proof=ScopedSingleTarget(catalog,source,foreign,target);
        var stem=foreign.ColumnName[..^3];
        var storage=MetadataStoragePolicy.ForTable(catalog,source);
        var path=cache.Extra.GetValueOrDefault("columnFullName").ToString();
        if(proof.Page.Id!=definition.ScopedSinglePageId||proof.Name.Id!=definition.ValueColumnId||
            cache.Id==foreign.Id||cache.ColumnName!=stem+"_"+proof.Name.ColumnName||cache.LogicalType!=proof.Name.LogicalType||
            !storage.IsPhysical(cache)||!Enabled(cache)||cache.IsVirtualColumn!=0||cache.ControlKind is not("" or "TextArea")||
            cache.LinkConfigTableId.Length>0||cache.Extra.GetValueOrDefault("joinTable").ToString().Length>0||
            cache.Extra.GetValueOrDefault("linkConfigPageId").ToString().Length>0||
            new[]{foreign,cache,proof.Name}.Any(column=>column.YzProjectId.Length>0&&column.YzProjectId!=catalog.YzProjectId)||
            ExactDataManageValues.IsSensitiveField(cache.ColumnName)||
            path!=stem+"."+proof.Name.ColumnName&&path!=foreign.ColumnName+"."+proof.Name.ColumnName)
            throw new InvalidOperationException("固定单值缓存与实际目标页或名称声明不一致。");
        if(GlobalCrossTableTarget.Declared(source))
        {
            var published=GlobalCachedRelationSource.RequireCache(catalog,source,cache);
            if(!published.ScopedSingle||published.Foreign.Id!=foreign.Id||published.Target.Id!=target.Id||published.Page.Id!=proof.Page.Id)
                throw new InvalidOperationException("全局固定副本须与明确发布的物理单值关系一致。");
        }
        // 完整编译固定路径和行归属。任何缺失的内层证明都阻断生成。
        _=Metarill.Data.Queries.MySqlNonRedundantReadCompiler.CompileRefresh(catalog,source.Id,[cache.ColumnName],[definition]);
    }

    /// <summary>实际目标页和名称由表级关系证明；不能用维护输入更换范围。</summary>
    internal static (PageMetadata Page,ColumnMetadata Name,FixedConditionPlan? Control) ScopedSingleTarget(MetadataCatalog catalog,
        TableMetadata source,ColumnMetadata foreign,TableMetadata target)
    {
        if(GlobalCrossTableTarget.Declared(target))
        {
            var global=GlobalSingleRelationTarget.RequireCached(catalog,source,foreign,target);
            if(global.RequiresScopedCache)return(global.Page,global.Name,global.CacheControl);
        }
        else if(ProjectScopedSingleRelationTarget.Resolve(catalog,source,foreign,target) is {} project)
            return(project.Page,project.Name,ProjectScopedSingleRelationTarget.ControlScope(catalog,source,foreign,target));
        throw new InvalidOperationException("固定单值缓存缺少已发布的目标页范围。");
    }

    /// <summary>只沿原明确联接标志，不把任意文本解释为关系。</summary>
    private static bool Enabled(ColumnMetadata c)=>c.Extra.TryGetValue("isJoinColumn",out var value)&&(value.ToString()=="1"||value.ValueKind==System.Text.Json.JsonValueKind.True);
}
