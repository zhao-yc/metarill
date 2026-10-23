using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>非冗余自树沿原生成器和控件，只持久化叶子ID并批量派生名称／完整路径。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class NonRedundantSelfTreeTests
{
    /// <summary>两种ID及父键交叉编译，显式／隐式名称都不写数据库、严格DTO或手写扩展。</summary>
    [Theory]
    [InlineData("string","string",false)][InlineData("string","int64",false)]
    [InlineData("int64","string",false)][InlineData("int64","int64",false)]
    [InlineData("string","string",true)][InlineData("string","int64",true)]
    [InlineData("int64","string",true)][InlineData("int64","int64",true)]
    public async Task Generates_scoped_reads_filters_and_only_physical_selected_id(string sourceType,string parentType,bool automatic)
    {
        var catalog=Catalog("nr_tree",sourceType,parentType,automatic);var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.NonRedTree");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);
        Assert.DoesNotContain(MetadataStoragePolicy.ForTable(catalog,table.Source).PhysicalColumns,column=>column.ColumnName=="tree_name");Assert.DoesNotContain(page.Writable,column=>column.Source.ColumnName=="tree_name");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nonred-self");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var entity=assembly.GetTypes().Single(type=>type.Name=="NrTreeSource");var attribute=entity.GetProperty("TreeName")!.GetCustomAttributes(typeof(FreeSql.DataAnnotations.ColumnAttribute),false).Cast<FreeSql.DataAnnotations.ColumnAttribute>().Single();Assert.True(attribute.IsIgnore);
            var create=assembly.GetTypes().Single(type=>type.Name=="NrTreeSourcePage201CreateRequest");Assert.Null(create.GetProperty("TreeName"));var vo=assembly.GetTypes().Single(type=>type.Name=="NrTreeSourcePage201Vo");Assert.NotNull(vo.GetProperty("TreeName"));Assert.NotNull(vo.GetProperty("TreeIdDisplayText"));
            var service=File.ReadAllText(Path.Combine(directory,"generated/Services/NrTreeSourceService.g.cs"));Assert.Contains("ReadCascadeFilters201Async",service);Assert.Contains("ProjectRead201Async",service);Assert.Contains("PathsAsync",service);Assert.Contains("session.InReadSnapshotAsync",service);Assert.Equal(before,JsonSerializer.Serialize(catalog));
            // 固定非冗余独立目标子集解除控件门禁；完整包仍要求显式项目／后台发布目录。
            Assert.DoesNotContain(GenerationCapabilityAudit.Collect(model),gap=>gap.Column=="tree_id");
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>真实生成页和原级联组件共同在Vue3挂载，网络受控；不替代浏览器或正式宿主验收。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public Task Original_self_tree_vue_keeps_paths_selection_clear_readonly_and_physical_write_contract(bool automatic)=>ExerciseOriginalVue(automatic,false);
    /// <summary>相同原组件探针用于物理和派生目标，不复制或简化UI。</summary>
    internal static async Task ExerciseOriginalVue(bool automatic,bool derived)
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nonred-self-vue");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(derived?DerivedSelfTreeTests.Catalog("nr_tree","string","int64",automatic):Catalog("nr_tree",automatic:automatic),output,"Example.TreeVue");
            var start=new System.Diagnostics.ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};start.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.Root(),"backend/tests/Metarill.Generation.Tests/NonRedundantSelfTreeVueProbe.mjs"));start.ArgumentList.Add(output);
            using var process=System.Diagnostics.Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await process.WaitForExitAsync(timeout.Token);}catch(Exception error){await SharedSourceBuildGate.StopOwnProcessAsync(process,stdout,stderr,error);throw;}Assert.True(process.ExitCode==0,await stdout+await stderr);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>从包内基础源码独立构建原前端宿主，实际本地更新继续保护手写partial。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public Task Full_self_tree_package_builds_and_updates_without_replacing_original_component_or_handwritten_code(bool automatic)=>ExerciseFull(automatic,false);
    /// <summary>派生目标与原目标共用真实完整包构建／更新验收。</summary>
    internal static async Task ExerciseFull(bool automatic,bool derived)
    {
        var catalog=derived?DerivedSelfTreeTests.Catalog("nr_tree","string","int64",automatic):Catalog("nr_tree",automatic:automatic);
        foreach(var record in catalog.ConfigTables.Cast<MetadataRecord>().Concat(catalog.ConfigColumns).Concat(catalog.ConfigPages).Concat(catalog.ConfigPageColumns))record.YzProjectId=catalog.YzProjectId;
        catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId=catalog.YzProjectId,Name="自树后台"});foreach(var page in catalog.ConfigPages)page.YzAdminTypeId="5";
        catalog.ConfigMenus.Add(new(){Id="9001",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",Name="树关系",Kind="GENERATED_PAGE",ConfigPageId="201"});
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nonred-self-package");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,"Example.TreeApplication")));var package=new ApplicationPackager().Create(catalog,output,"Example.TreeApplication",NonRedundantApplicationFixture.Root());Assert.True(package.FrontendIncluded);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            var component="frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue";Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(NonRedundantApplicationFixture.Root(),"frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),await File.ReadAllBytesAsync(Path.Combine(output,component)));Assert.DoesNotContain("$set",await File.ReadAllTextAsync(Path.Combine(output,component)));
            using var profile=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")));var subset=Assert.Single(profile.RootElement.GetProperty("supportedControlSubsets").EnumerateArray());Assert.Equal("CascadeTree",subset.GetProperty("controlKind").GetString());Assert.Equal("selfTree",subset.GetProperty("sourceMode").GetString());Assert.False(subset.GetProperty("sameTableSupported").GetBoolean());Assert.False(subset.GetProperty("renamePropagationSupported").GetBoolean());Assert.Equal(derived,subset.GetProperty("derivedNameSupported").GetBoolean());
            var handwritten=Path.Combine(output,"backend/generated/Services/NrTreeSourceService.cs");await File.AppendAllTextAsync(handwritten,"\n// 自树包手写保护标记。\n");var bytes=await File.ReadAllBytesAsync(handwritten);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(catalog,candidate,"Example.TreeApplication",NonRedundantApplicationFixture.Root());var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(handwritten));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>完整包仍拒绝同表、来源父参数和缺失目标发布归属，独立后端已验不能提前解除所有组合。</summary>
    [Theory][InlineData("sameTable")][InlineData("sourceParent")][InlineData("targetRealm")]
    public void Package_subset_keeps_unproven_combinations_and_unpublished_target_gated(string kind)
    {
        var catalog=kind=="sameTable"?CascadeTreeSameRowTests.Catalog():Catalog("nr_tree");foreach(var table in catalog.ConfigTables.Where(table=>table.Id=="1"||table.Id=="2"))table.IsRongyu=0;
        foreach(var record in catalog.ConfigTables.Cast<MetadataRecord>().Concat(catalog.ConfigColumns).Concat(catalog.ConfigPages).Concat(catalog.ConfigPageColumns))record.YzProjectId=catalog.YzProjectId;
        catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId=catalog.YzProjectId});foreach(var page in catalog.ConfigPages)page.YzAdminTypeId="5";
        if(kind=="targetRealm")catalog.ConfigPages.Single(page=>page.Id=="301").YzProjectId="";
        if(kind=="sourceParent")catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql+=" AND id=@parent_id";
        var model=GenerationModel.Create(catalog,"Example.GatedTree");Assert.NotEmpty(GenerationCapabilityAudit.Collect(model));
    }
    /// <summary>不能把尚未证明的名称用途和目标派生范围落成宽查询或无效物理SQL。</summary>
    [Theory]
    [InlineData("defaultOrder")][InlineData("nameCondition")][InlineData("virtualName")][InlineData("conflictingNamePath")][InlineData("derivedTargetCondition")][InlineData("nestedName")]
    public void Rejects_unproven_name_dependencies_and_derived_target_scope(string kind)
    {
        var catalog=Catalog("nr_tree");
        if(kind=="defaultOrder")catalog.ConfigPages.Single(page=>page.Id=="201").OrderBy="tree_name asc";
        if(kind=="nameCondition")catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql+=" AND tree_name='隐藏'";
        if(kind=="virtualName")catalog.ConfigColumns.Single(column=>column.Id=="26").IsVirtualColumn=1;
        if(kind=="conflictingNamePath")catalog.ConfigColumns.Single(column=>column.Id=="26").Extra["columnFullName"]=JsonSerializer.SerializeToElement("other.name");
        if(kind=="derivedTargetCondition")
        {
            catalog.ConfigTables.Single(table=>table.Id=="3").IsRongyu=0;
            catalog.ConfigColumns.Single(column=>column.Id=="37").Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("parent.name")};
        }
        if(kind=="nestedName")
        {
            catalog.ConfigTables.Add(new(){Id="4",TableName="nr_consumer",IsRongyu=0});
            catalog.ConfigColumns.AddRange([new(){Id="41",ConfigTableId="4",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="42",ConfigTableId="4",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="43",ConfigTableId="4",ColumnName="ref_id",LogicalType="int64",LinkConfigTableId="2"},new(){Id="44",ConfigTableId="4",ColumnName="ref_label",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("ref.tree_name")}}]);
            Assert.Throws<NotSupportedException>(()=>Metarill.Data.Queries.NonRedundantReads.Compile(catalog,"4",["ref_label"]));return;
        }
        Assert.Throws<InvalidOperationException>(()=>{var model=GenerationModel.Create(catalog,"Example.RejectedTree");var table=model.Tables.Single(table=>table.Source.Id=="2");NonRedundantReadCompilation.ForPage(model,table,Assert.Single(table.Pages));});
    }
    /// <summary>旧冗余目标的声明联接列仍是真实物理条件，不被新非冗余范围门禁误伤。</summary>
    [Theory][InlineData(0)][InlineData(1)]
    public async Task Redundant_target_materialized_scope_remains_a_physical_condition(int sourceStorage)
    {
        var catalog=Catalog("scope_tree");catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=sourceStorage;
        catalog.ConfigColumns.Add(new(){Id="38",ConfigTableId="3",ColumnName="enabled",LogicalType="int32"});catalog.ConfigColumns.Single(column=>column.Id=="37").Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("parent_scope_tree_tree.enabled")};
        var model=GenerationModel.Create(catalog,"Example.PhysicalTreeScope");var table=model.Tables.Single(table=>table.Source.Id=="2");Assert.NotNull(CascadeTreeReadCompilation.Resolve(model,table,Assert.Single(table.Pages),table.Columns.Single(column=>column.Source.Id=="25")).Definition);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-physical-tree-scope");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>延期仅是内部固定图的明确用途；普通编译、查询依赖和嵌套名称不能拿占位代替结果。</summary>
    [Fact]
    public void Deferred_names_require_explicit_graph_projection_and_cannot_be_query_dependencies()
    {
        var catalog=Catalog("nr_tree");
        Assert.Throws<NotSupportedException>(()=>Metarill.Data.Queries.NonRedundantReads.Compile(catalog,"2",["tree_name"]));
        Assert.Equal("string",Metarill.Data.Queries.NonRedundantReads.Compile(catalog,"2",["tree_name"],deferCascadeNames:true).OutputTypes["tree_name"]);
        Assert.Throws<NotSupportedException>(()=>Metarill.Data.Queries.NonRedundantReads.Compile(catalog,"2",["id"],["tree_name"],deferCascadeNames:true));
    }
    /// <summary>元数据仍是唯一来源，自动叶子名只在逻辑模型展开，不新建配置行。</summary>
    internal static MetadataCatalog Catalog(string prefix,string sourceType="string",string parentType="int64",bool automatic=false)
    {
        var catalog=CascadeTreeWriteTests.Catalog(prefix,sourceType,parentType);catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=0;
        if(automatic){catalog.ConfigColumns.RemoveAll(column=>column.Id=="26");catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="26");}
        var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.IsImport=1;page.IsExport=1;catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);return catalog;
    }
}
