using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>R1派生自树只能经完整维护与混合传播，物理执行器仍拒绝逻辑值。</summary>
public sealed class DerivedSelfTreeCacheTests
{
    /// <summary>四键及旧受管标记共用固定定义，完整计划必须包含树与名称终点依赖。</summary>
    [Theory][InlineData("string","string")][InlineData("string","int64")][InlineData("int64","string")][InlineData("int64","int64")]
    public void Logical_leaf_builds_one_frozen_complete_maintenance_closure(string type,string parent)
    {
        foreach(var marker in new[]{false,true})
        {
            var c=Catalog(type,parent);if(marker)c.ConfigColumns.Single(x=>x.Id=="cache").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);var before=JsonSerializer.Serialize(c);var d=SelfTreeCache.Compile(c,"source","foreign");DerivedCrossTableCaches.Validate(c,[d],[]);Assert.ThrowsAny<Exception>(()=>RelationMutationPlan.Compile(c,[d]));var plan=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(c,[],[d]);Assert.Contains("tree_name",plan.MixedSourceColumns["ds_source"]);Assert.Contains("ds_tree",plan.MixedTargets);Assert.Contains("ds_company",plan.MixedTargets);Assert.NotEmpty(plan.Fingerprint);Assert.Equal(before,JsonSerializer.Serialize(c));
        }
    }
    /// <summary>改名与目标可见性进入同一反查计划；根只刷新当前项目，且回填沿用树目标的常量。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Global_derived_leaf_tracks_publisher_and_its_name_producer(bool hasRowProject)
    {
        var catalog=Catalog("string","int64");
        foreach(var table in catalog.ConfigTables)table.YzProjectId=catalog.YzProjectId;
        foreach(var table in catalog.ConfigTables.Where(table=>table.Id is "tree" or "company"))
            table.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(!hasRowProject)catalog.ConfigColumns.RemoveAll(column=>column.Id=="tree_project");
        catalog.YzAdminTypes.Add(new(){Id="admin",YzProjectId=catalog.YzProjectId});
        catalog.ConfigColumns.Add(new(){Id="status",ConfigTableId="tree",ColumnName="status",LogicalType="int32"});
        catalog.ConfigPages.AddRange([
            new(){Id="tree_page",ConfigTableId="tree",YzAdminTypeId="admin",YzProjectId=catalog.YzProjectId,WhereSql="status=1"},
            new(){Id="company_page",ConfigTableId="company",YzAdminTypeId="admin",YzProjectId=catalog.YzProjectId}]);
        var company=catalog.ConfigColumns.Single(column=>column.Id=="tree_company");
        company.ControlKind="List";
        company.ControlDetail="{\"tableId\":\"company\",\"pageId\":\"company_page\",\"nameField\":\"caption\"}";
        var definition=SelfTreeCache.Compile(catalog,"source","foreign","tree_page");
        var maintenance=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[definition]);
        var propagation=Assert.Single(maintenance.Mixed.Consumers);
        Assert.Contains("b.`yz_project_id`",propagation.Read.Scope);
        Assert.DoesNotContain("r0.`yz_project_id`",propagation.Read.From);
        Assert.Contains("status",propagation.Read.Reverse.Single(item=>item.TargetTable=="ds_tree").Outputs["tree_name"]);
        Assert.Contains("caption",propagation.Read.Reverse.Single(item=>item.TargetTable=="ds_company").Outputs["tree_name"]);
        Assert.Equal(propagation.Read.From,maintenance.Plans["source"].Read!.From);
    }
    /// <summary>逻辑列需要真正的单值终点、物理父结构与固定缓存，不能借后缀取得权限。</summary>
    [Theory][InlineData("missingPath")][InlineData("array")][InlineData("credential")][InlineData("global")][InlineData("virtualParent")][InlineData("derivedParent")][InlineData("cachePath")][InlineData("foreignType")]
    public void Unproven_derived_name_or_topology_stays_closed(string kind)
    {
        var c=Catalog("string","int64");var name=c.ConfigColumns.Single(x=>x.Id=="tree_name");var parent=c.ConfigColumns.Single(x=>x.Id=="parent");var terminal=c.ConfigColumns.Single(x=>x.Id=="company_name");if(kind=="missingPath")name.Extra.Remove("columnFullName");if(kind=="array")terminal.ControlKind="FileLibrary";if(kind=="credential"){terminal.ColumnName="password";name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.password");}if(kind=="global")c.ConfigTables.Single(x=>x.Id=="tree").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind=="virtualParent")parent.IsVirtualColumn=1;if(kind=="derivedParent"){parent.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);parent.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.id");}if(kind=="cachePath"){var cache=c.ConfigColumns.Single(x=>x.Id=="cache");cache.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("tree.unrelated");}if(kind=="foreignType")c.ConfigColumns.Single(x=>x.Id=="foreign").LogicalType="int32";Assert.ThrowsAny<Exception>(()=>SelfTreeCache.Compile(c,"source","foreign"));
    }
    /// <summary>普通物理传播入口、伪造值／缓存和跨表标识均不能绕开完整自树证明。</summary>
    [Fact]public void Forged_cross_binding_and_incomplete_physical_plan_are_rejected()
    {
        var c=Catalog("int64","string");var d=SelfTreeCache.Compile(c,"source","foreign");Assert.ThrowsAny<Exception>(()=>RelationMaintenanceCatalog.Compile(c,[d]));Assert.ThrowsAny<Exception>(()=>DerivedCrossTableCaches.Validate(c,[d with{CrossTableColumnId="foreign",CrossTableLevelKey="fake"}],[]));Assert.ThrowsAny<Exception>(()=>DerivedCrossTableCaches.Validate(c,[d with{ValueColumnId="company_name"}],[]));Assert.ThrowsAny<Exception>(()=>DerivedCrossTableCaches.Validate(c,[d with{Multiple=true}],[]));
    }
    /// <summary>物理副本反向依赖来源缓存形成循环，整份目录不得以任一方向先运行。</summary>
    [Fact]public void Mixed_cycle_through_a_physical_name_cache_rejects_before_runtime()
    {
        var c=Catalog("string","int64");var company=c.ConfigColumns.Single(x=>x.Id=="company_name");company.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);company.Extra["columnFullName"]=JsonSerializer.SerializeToElement("source.tree_name");c.ConfigColumns.Add(new(){Id="back",ConfigTableId="company",ColumnName="source_id",LogicalType="int64",LinkConfigTableId="source"});var d=SelfTreeCache.Compile(c,"source","foreign");Assert.ThrowsAny<Exception>(()=>RelationMaintenanceCatalog.CompileWithDerivedCrossTables(c,[],[d]));
    }
    /// <summary>纯元数据目录：树名称来自独立公司caption，源缓存仍沿原tree_name命名。</summary>
    internal static MetadataCatalog Catalog(string type,string parent)
    {
        var c=new MetadataCatalog{YzProjectId="1"};c.ConfigTables.AddRange([new(){Id="source",TableName="ds_source",IsRongyu=1},new(){Id="tree",TableName="ds_tree",IsRongyu=0},new(){Id="company",TableName="ds_company",IsRongyu=1}]);
        foreach(var (table,prefix) in new[]{("source","source"),("tree","tree"),("company","company")})c.ConfigColumns.AddRange([new(){Id=prefix+"_id",ConfigTableId=table,ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id=prefix+"_project",ConfigTableId=table,ColumnName="yz_project_id",LogicalType="int64"},new(){Id=prefix+"_deleted",ConfigTableId=table,ColumnName="delete_no",LogicalType="datetime"}]);
        c.ConfigColumns.AddRange([new(){Id="foreign",ConfigTableId="source",ColumnName="tree_id",LogicalType=type,ControlKind="CascadeTree",LinkConfigTableId="tree",ControlDetail="{\"sourceMode\":\"selfTree\",\"selfTree\":{\"table\":\"ds_tree\",\"parentField\":\"parent_node_id\",\"nameField\":\"name\"}}"},new(){Id="cache",ConfigTableId="source",ColumnName="tree_name",LogicalType="string"},new(){Id="tree_name",ConfigTableId="tree",ColumnName="name",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("company.caption")}},new(){Id="parent",ConfigTableId="tree",ColumnName="parent_node_id",LogicalType=parent,LinkConfigTableId="tree"},new(){Id="tree_company",ConfigTableId="tree",ColumnName="company_id",LogicalType="int64",LinkConfigTableId="company"},new(){Id="company_name",ConfigTableId="company",ColumnName="caption",LogicalType="string"}]);return c;
    }
}
