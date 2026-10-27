using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>独立R0自树的节点名称来自固定单值联接，原路径和叶子ID合同保持。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class DerivedSelfTreeTests
{
    /// <summary>显式和自动叶子名称从真实元数据生成编译，名称不落物理列。</summary>
    [Theory]
    [InlineData("string","string",false)][InlineData("string","int64",false)][InlineData("int64","string",false)][InlineData("int64","int64",false)]
    [InlineData("string","string",true)][InlineData("string","int64",true)][InlineData("int64","string",true)][InlineData("int64","int64",true)]
    public async Task Derived_tree_names_generate_and_compile(string sourceType,string parentType,bool automatic)
    {
        var c=Catalog("derived_tree",sourceType,parentType,automatic);var before=JsonSerializer.Serialize(c);var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-self-tree");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),"Example.DerivedTree");await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.Equal(before,JsonSerializer.Serialize(c));Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(c,"Example.DerivedTree")));Assert.DoesNotContain("name",MetadataStoragePolicy.ForTable(c,c.ConfigTables.Single(table=>table.Id=="3")).PhysicalColumns.Select(column=>column.ColumnName));}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>同时开放的独立R0物理名称目标仍生成编译完整三列路径，不能只验证派生分支。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Independent_physical_R0_target_keeps_the_original_reader(bool automatic)
    {
        var c=NonRedundantSelfTreeTests.Catalog("physical_tree",automatic:automatic);c.ConfigTables.Single(x=>x.Id=="3").IsRongyu=0;RedundantMultiNameCacheTests.Published(c);var boundary=Path.Combine(Path.GetTempPath(),"metarill-physical-r0-tree");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),"Example.PhysicalR0Tree");await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(c,"Example.PhysicalR0Tree")));}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>完整包仍沿原Vue、原接口和partial更新保护，不能以编译替代页面合同。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Full_package_and_original_vue_preserve_the_existing_interaction(bool automatic)
    {await NonRedundantSelfTreeTests.ExerciseFull(automatic,true);await NonRedundantSelfTreeTests.ExerciseOriginalVue(automatic,true);}
    /// <summary>未证明的终点、拓扑或范围均拒绝；名称联接不能放开数组、凭据和缓存。</summary>
    [Theory][InlineData("sourceR1BadCache")][InlineData("global")][InlineData("array")][InlineData("credential")][InlineData("wrongPath")][InlineData("derivedParent")][InlineData("derivedCondition")][InlineData("derivedProject")][InlineData("derivedSort")]
    public void Unproven_derived_tree_is_rejected(string scenario)
    {
        var c=Catalog("nr_tree","string","int64",false);var name=c.ConfigColumns.Single(x=>x.Id=="32");
        if(scenario=="sourceR1BadCache"){c.ConfigTables.Single(x=>x.Id=="2").IsRongyu=1;c.ConfigColumns.Single(x=>x.Id=="26").IsVirtualColumn=1;}
        if(scenario=="global")c.ConfigTables.Single(x=>x.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(scenario=="array")c.ConfigColumns.Single(x=>x.Id=="42").ControlKind="FileLibrary";
        if(scenario=="credential"){c.ConfigColumns.Single(x=>x.Id=="42").ColumnName="password";name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.password");}
        if(scenario=="wrongPath")name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("missing.name");
        if(scenario is "derivedParent" or "derivedCondition" or "derivedProject"){var field=c.ConfigColumns.Single(x=>x.Id==(scenario=="derivedParent"?"35":scenario=="derivedProject"?"33":"37"));field.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);field.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.id");}
        if(scenario=="derivedSort"){c.ConfigColumns.Add(new(){Id="39",ConfigTableId="3",ColumnName="other_label",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("company.name")}});var source=c.ConfigColumns.Single(x=>x.Id=="25");source.ControlDetail=source.ControlDetail.Replace("\"rootMode\":\"blank\"","\"rootMode\":\"blank\",\"orderBy\":\"other_label asc\"",StringComparison.Ordinal);Assert.Contains("other_label asc",source.ControlDetail);}
        GenerationModel model;try{model=GenerationModel.Create(c,"Example.RejectDerivedTree");}catch(InvalidOperationException)when(scenario is "derivedCondition" or "derivedProject"){return;}var table=model.Tables.Single(x=>x.Source.Id=="2");var plan=CascadeTreeReadCompilation.Resolve(model,table,table.Pages.Single(),table.Columns.Single(x=>x.Source.Id=="25"));Assert.Null(plan.Definition);Assert.NotEmpty(plan.Reason!);
    }
    /// <summary>沿原自树元数据，仅令树节点name来自company.name；父结构及发布条件仍是物理列。</summary>
    internal static MetadataCatalog Catalog(string prefix,string sourceType,string parentType,bool automatic)
    {
        var c=NonRedundantSelfTreeTests.Catalog(prefix,sourceType,parentType,automatic);c.ConfigTables.Single(table=>table.Id=="3").IsRongyu=0;var name=c.ConfigColumns.Single(column=>column.Id=="32");name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.name");
        c.ConfigTables.Add(new(){Id="4",TableName=prefix+"_company",IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="41",ConfigTableId="4",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="42",ConfigTableId="4",ColumnName="name",LogicalType="string"},new(){Id="43",ConfigTableId="4",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="44",ConfigTableId="4",ColumnName="delete_no",LogicalType="datetime"},new(){Id="38",ConfigTableId="3",ColumnName="company_id",LogicalType="int64",LinkConfigTableId="4",ControlKind="List",ControlDetail="{\"tableId\":\"4\",\"nameField\":\"name\"}"}]);
        c.ConfigPages.Add(new(){Id="401",ConfigTableId="4",WhereSql="yz_project_id=@current_yz_project_id"});c.ConfigPageColumns.Add(new(){Id="40142",ConfigPageId="401",ConfigColumnId="42"});c.ConfigPageColumns.Add(new(){Id="30138",ConfigPageId="301",ConfigColumnId="38"});return RedundantMultiNameCacheTests.Published(c);
    }
}
