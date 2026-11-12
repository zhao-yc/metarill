using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;

namespace Metarill.Runtime.Files;

/// <summary>文件模块只读取已发布的业务模型快照，不依赖平台的双项目注册器。</summary>
public sealed class FileMetadataContext
{
    public IReadOnlyList<MetadataCatalog> Catalogs { get; }
    /// <summary>只有带真实文件字段的逻辑父页需要来源／目标共用读快照。</summary>
    internal bool HasLogicalFileParents{get;}
    private readonly Dictionary<(string Project,string Table),IReadOnlyList<ColumnMetadata>> physicalColumns=[];
    private readonly Dictionary<(string Project,string Page,string Field),NonRedundantReadPlan> derivedExactPlans=[];
    private readonly Dictionary<(string Project,string Page),(ColumnMetadata Column,bool Derived,bool Shared)> exactKinds=[];
    /// <summary>物理外键槽可能是共享逻辑值；坏目录不能回退成物理比较。</summary>
    internal (ColumnMetadata Column,bool Derived,bool Shared) ExactField(MetadataCatalog catalog,PageMetadata page)=>
        exactKinds.TryGetValue((catalog.YzProjectId,page.Id),out var kind)?kind:
            throw new NotSupportedException("文件来源没有固定精确范围证明。");
    private readonly Dictionary<(string Project,string Page),FilePageScope?> pageScopes=[];
    /// <summary>只读取本发布快照的固定计划；坏定义按来源拒绝，不在请求内重新解析或回退。</summary>
    internal FilePageScope PageScope(MetadataCatalog catalog,PageMetadata page)
    {
        if(!Catalogs.Contains(catalog)||!catalog.ConfigPages.Contains(page))throw new InvalidOperationException("文件范围不属于当前发布快照。");
        return pageScopes.GetValueOrDefault((catalog.YzProjectId,page.Id))??throw new NotSupportedException("文件来源页面没有完整固定范围证明。");
    }
    /// <summary>保护文件用途只取当前页、当前文件列的固定窄投影，缺证明时不回退为无限制。</summary>
    internal NonRedundantReadPlan? DerivedExactPlan(MetadataCatalog catalog,PageMetadata page,ColumnMetadata field)=>derivedExactPlans.GetValueOrDefault((catalog.YzProjectId,page.Id,field.Id));
    /// <summary>发布时编译文件引用所用物理列，运行时不按提交值或派生名称重新解释存储归属。</summary>
    internal IReadOnlyList<ColumnMetadata> PhysicalColumns(MetadataCatalog catalog,TableMetadata table)
    {
        if(!Catalogs.Contains(catalog)||!catalog.ConfigTables.Contains(table))throw new InvalidOperationException("文件存储计划不属于当前发布快照。");
        return physicalColumns[(catalog.YzProjectId,table.Id)];
    }
    /// <summary>初始化时固定来源并拒绝重复归属，防止同一页面被不同项目解释。</summary>
    public FileMetadataContext(IReadOnlyList<MetadataCatalog> catalogs)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshots=catalogs.Select(catalog=>JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(catalog,options),options)!).ToArray();
        foreach(var snapshot in snapshots) MetadataValidation.Validate(snapshot);
        if(snapshots.Length==0 || snapshots.Select(c=>c.YzProjectId).Distinct(StringComparer.Ordinal).Count()!=snapshots.Length
            || snapshots.SelectMany(c=>c.ConfigPages).GroupBy(p=>p.Id,StringComparer.Ordinal).Any(group=>group.Count()>1))
            throw new InvalidOperationException("文件发布元数据的项目或页面重复。");
        Catalogs=Array.AsReadOnly(snapshots);
        foreach(var catalog in snapshots)foreach(var table in catalog.ConfigTables)physicalColumns.Add((catalog.YzProjectId,table.Id),MetadataStoragePolicy.ForTable(catalog,table).PhysicalColumns);
        foreach(var catalog in snapshots)foreach(var page in catalog.ConfigPages)
        {
            FilePageScope? scope=null;
            try{scope=FileReferenceScope.CompilePage(catalog,catalog.ConfigTables.Single(table=>table.Id==page.ConfigTableId),page);}
            catch(Exception invalid)when(invalid is NotSupportedException or ArgumentException or InvalidOperationException){/* 坏定义只使该来源拒绝，不影响其它合法文件页面。 */}
            pageScopes.Add((catalog.YzProjectId,page.Id),scope);
        }
        HasLogicalFileParents=snapshots.Any(catalog=>catalog.ConfigPages.Any(page=>pageScopes[(catalog.YzProjectId,page.Id)] is{Crosses.Count:>0}&&physicalColumns[(catalog.YzProjectId,page.ConfigTableId)].Any(column=>column.ControlKind=="FileLibrary")));
        foreach(var catalog in snapshots)foreach(var page in catalog.ConfigPages)CompileDerivedExact(catalog,page);
    }
    /// <summary>启动时核验派生终点并编译文件/主键/项目/父范围；坏配置保留原逐来源拒权语义。</summary>
    private void CompileDerivedExact(MetadataCatalog catalog,PageMetadata page)
    {
        try
        {
            if(!page.Extra.TryGetValue("exactDataManageMatchField",out var value)||value.ValueKind!=JsonValueKind.String)return;
            var name=ExactDataManageValues.Field(value.GetString());if(name.Length==0)return;
            var table=catalog.ConfigTables.Single(table=>table.Id==page.ConfigTableId);var physical=PhysicalColumns(catalog,table);
            var column=RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id))
                .Single(column=>ExactDataManageValues.Field(column.ColumnName)==name);
            var group=NonRedundantReads.SharedExactGroup(catalog,table,column.ColumnName);
            var derived=group is not null||!physical.Any(item=>item.Id==column.Id);
            exactKinds.Add((catalog.YzProjectId,page.Id),(column,derived,group is not null));
            if(!derived)return;
            var scope=PageScope(catalog,page);
            var raw=scope.Crosses.SelectMany(cross=>cross.StorageNames).Distinct(StringComparer.Ordinal).ToArray();
            IReadOnlyList<NonRedundantCrossTableScope> bindings=group is null?[]:SharedExactBindings(catalog,table,page,group,raw);
            var match=group is null?NonRedundantReads.CompileMatch(catalog,table.Id,column.ColumnName):
                NonRedundantReads.CompileSharedExactMatch(catalog,table.Id,column.ColumnName,bindings);
            if(match.OutputTypes[column.ColumnName]!=column.LogicalType)throw new NotSupportedException("文件来源派生匹配类型不一致。");
            var dependencies=new[]{column.ColumnName}.Concat(scope.ConditionFields).Distinct(StringComparer.Ordinal).ToArray();
            foreach(var file in physical.Where(item=>item.ControlKind=="FileLibrary"))
            {
                var names=new[]{file.ColumnName,"id"}.Concat(physical.Any(item=>item.ColumnName=="yz_project_id")?new[]{"yz_project_id"}:Array.Empty<string>())
                    .Concat(scope.Parents.Select(parent=>parent.Source.ColumnName)).Concat(scope.GlobalParents.Select(parent=>parent.ColumnName))
                    .Concat(raw).Distinct(StringComparer.Ordinal).ToArray();
                // 逻辑权限走同一条件；后续父链检查仍消费实际稀疏存储位。
                var plan=raw.Length==0?NonRedundantReads.CompileWithCrossTables(catalog,table.Id,names,bindings,dependencies):
                    NonRedundantReads.CompileRuleRecord(catalog,table.Id,names,raw,bindings,dependencies);
                derivedExactPlans.Add((catalog.YzProjectId,page.Id,file.Id),plan);
            }
        }
        catch(Exception invalid)when(invalid is NotSupportedException or InvalidOperationException or ArgumentException or JsonException){/* 当前来源缺少可执行证明，读取时明确拒权，不能降级为物理列或无限制。 */}
    }

    /// <summary>共享父范围和精确匹配使用同一目标页解析，避免原文件请求取到另一套候选。</summary>
    private static IReadOnlyList<NonRedundantCrossTableScope> SharedExactBindings(MetadataCatalog catalog,TableMetadata table,
        PageMetadata page,SharedCrossTableSparseSelection exact,IReadOnlyList<string> raw)
    {
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id).ToArray();
        var groups=raw.Count==0?[exact]:SharedCrossTableSparseSelection.CompileAll(table,columns)
            .Where(group=>group.Branches[0].SourceColumnId==exact.Branches[0].SourceColumnId||
                group.Slots.Any(slot=>raw.Contains(slot.ColumnName,StringComparer.Ordinal))).ToArray();
        return groups.SelectMany(group=>group.Branches.Select(branch=>FileCrossParentScope.CompileBranch(catalog,table,page,
            columns.Single(column=>column.Id==branch.SourceColumnId),group.BranchSelection(branch.SourceColumnId),0,allowExactSource:true).Binding)).ToArray();
    }
}
