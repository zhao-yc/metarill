using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;

namespace Metarill.Runtime.Files;

/// <summary>文件模块只读取已发布的业务模型快照，不依赖平台的双项目注册器。</summary>
public sealed class FileMetadataContext
{
    public IReadOnlyList<MetadataCatalog> Catalogs { get; }
    private readonly Dictionary<(string Project,string Table),IReadOnlyList<ColumnMetadata>> physicalColumns=[];
    private readonly Dictionary<(string Project,string Page,string Field),NonRedundantReadPlan> derivedExactPlans=[];
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
            var column=catalog.ConfigColumns.Single(column=>column.ConfigTableId==table.Id&&ExactDataManageValues.Field(column.ColumnName)==name);
            if(physical.Any(item=>item.Id==column.Id))return;
            var match=NonRedundantReads.CompileMatch(catalog,table.Id,column.ColumnName);
            if(match.OutputTypes[column.ColumnName]!=column.LogicalType)throw new NotSupportedException("文件来源派生匹配类型不一致。");
            var scopes=FileReferenceScope.Compile(catalog,table,page);
            foreach(var file in physical.Where(item=>item.ControlKind=="FileLibrary"))
            {
                var names=new[]{file.ColumnName,"id"}.Concat(physical.Any(item=>item.ColumnName=="yz_project_id")?new[]{"yz_project_id"}:Array.Empty<string>()).Concat(scopes.Select(scope=>scope.Source.ColumnName)).Distinct(StringComparer.Ordinal).ToArray();
                derivedExactPlans.Add((catalog.YzProjectId,page.Id,file.Id),NonRedundantReads.Compile(catalog,table.Id,names,[column.ColumnName]));
            }
        }
        catch(Exception invalid)when(invalid is NotSupportedException or InvalidOperationException or ArgumentException or JsonException){/* 当前来源缺少可执行证明，读取时明确拒权，不能降级为物理列或无限制。 */}
    }
}
