using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>多选名称可以来自R0目标的单值联接，原ID数组与名称数组合同保持。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class DerivedMultiNamesTests
{
    /// <summary>实际生成编译，同时证明显式与自动名称不落物理列。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Derived_multi_names_compile_in_original_crud_and_excel(bool automatic)
    {
        var catalog=Catalog("derived_multi",automatic);var before=JsonSerializer.Serialize(catalog);var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-multi");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.DerivedMulti");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            Assert.NotNull(assembly.GetType("Example.DerivedMulti.Generated.Services.DerivedMultiSourceService"));
            Assert.Equal(before,JsonSerializer.Serialize(catalog));
            Assert.DoesNotContain("target_names",MetadataStoragePolicy.ForTable(catalog,catalog.ConfigTables.Single(table=>table.Id=="2")).PhysicalColumns.Select(column=>column.ColumnName));
            Assert.DoesNotContain("name",MetadataStoragePolicy.ForTable(catalog,catalog.ConfigTables.Single(table=>table.Id=="3")).PhysicalColumns.Select(column=>column.ColumnName));
            Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,"Example.DerivedMulti")));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>完整包按原源码构建并挂载原Vue，更新不覆盖手写partial或组件。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Full_derived_multi_package_preserves_original_vue_and_handwritten_service(bool automatic)
    {
        var root=NonRedundantApplicationFixture.Root();var catalog=Catalog("relation",automatic);catalog.ConfigMenus.Add(new(){Id="9001",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",Name="来源",Kind="GENERATED_PAGE",ConfigPageId="201"});var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-multi-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            Assert.True(new ApplicationPackager().Create(catalog,output,"Example.DerivedMultiApplication",root).FrontendIncluded);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            foreach(var component in new[]{"common/yzp_runtime/multiSelectFieldMixin.js","common/yzp_runtime/selectionPageMixin.js"})Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin",component)),File.ReadAllBytes(Path.Combine(output,"frontend",component)));
            var start=new System.Diagnostics.ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add(Path.Combine(root,"backend/tests/Metarill.Generation.Tests/GeneratedMultiSelectVueProbe.mjs"));start.ArgumentList.Add(output);using var process=System.Diagnostics.Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await process.WaitForExitAsync(timeout.Token);}catch(Exception error){await SharedSourceBuildGate.StopOwnProcessAsync(process,stdout,stderr,error);throw;}Assert.True(process.ExitCode==0,await stdout+await stderr);
            using(var capabilities=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")))){var subset=Assert.Single(capabilities.RootElement.GetProperty("supportedControlSubsets").EnumerateArray(),item=>item.GetProperty("controlKind").GetString()=="MultiSelectPage");Assert.True(subset.GetProperty("derivedNameSupported").GetBoolean());Assert.False(subset.GetProperty("renamePropagationSupported").GetBoolean());}
            var hand=Path.Combine(output,"backend/generated/Services/RelationSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 派生多选手写保护。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(catalog,candidate,"Example.DerivedMultiApplication",root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>坏名称、隐藏声明、派生范围和未接线缓存仍须在完整包落盘前拒绝。</summary>
    [Theory][InlineData("global")][InlineData("hiddenName")][InlineData("credential")][InlineData("array")][InlineData("broken")][InlineData("cycle")][InlineData("stringKey")][InlineData("derivedScope")][InlineData("redundantSource")][InlineData("hiddenBad")]
    public void Invalid_derived_multi_is_rejected_before_staging(string kind)
    {
        var c=Catalog("derived_multi",false);var name=c.ConfigColumns.Single(column=>column.Id=="32");
        if(kind=="global")c.ConfigTables.Single(table=>table.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind=="hiddenName")c.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="301"&&field.ConfigColumnId=="32");
        if(kind=="credential"){c.ConfigColumns.Single(column=>column.Id=="42").ColumnName="password";name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.password");}
        if(kind=="array")c.ConfigColumns.Single(column=>column.Id=="42").ControlKind="FileLibrary";
        if(kind is "broken" or "hiddenBad")name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("missing.name");
        if(kind=="hiddenBad")c.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="25");
        if(kind=="cycle"){name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("self.name");c.ConfigColumns.Add(new(){Id="38",ConfigTableId="3",ColumnName="self_id",LogicalType="int64",LinkConfigTableId="3",ControlKind="List",ControlDetail="{\"tableId\":\"3\",\"nameField\":\"id\"}"});}
        if(kind=="stringKey")c.ConfigColumns.Single(column=>column.Id=="37").LogicalType="string";
        if(kind=="derivedScope")c.ConfigPages.Single(page=>page.Id=="301").WhereSql+=" AND name='X'";
        if(kind=="redundantSource")c.ConfigTables.Single(table=>table.Id=="2").IsRongyu=1;
        var output=Path.Combine(Path.GetTempPath(),"metarill-derived-multi-reject",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.InvalidDerivedMulti",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>原R1目标物理Join缓存仍需独立证明，不能随R0逻辑名称门禁放开。</summary>
    [Fact]
    public async Task Redundant_target_join_name_retains_old_gate()
    {
        var catalog=Catalog("derived_multi",false);catalog.ConfigTables.Single(table=>table.Id=="3").IsRongyu=1;var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-multi-physical-gate");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(catalog,output,"Example.UnprovedRedundantTarget",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));}
        finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>目标name由company.name派生，其他页面和多选根定义沿原元数据。</summary>
    internal static MetadataCatalog Catalog(string prefix,bool automatic)
    {
        var c=NonRedundantMultiSelectionTests.Catalog(prefix);c.ConfigTables.Single(table=>table.Id=="3").IsRongyu=0;
        var name=c.ConfigColumns.Single(column=>column.Id=="32");name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.name");
        c.ConfigTables.Add(new(){Id="4",TableName=prefix+"_company",IsRongyu=1});
        c.ConfigColumns.AddRange([new(){Id="41",ConfigTableId="4",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="42",ConfigTableId="4",ColumnName="name",LogicalType="string"},new(){Id="43",ConfigTableId="4",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="44",ConfigTableId="4",ColumnName="delete_no",LogicalType="datetime"},new(){Id="37",ConfigTableId="3",ColumnName="company_id",LogicalType="int64",LinkConfigTableId="4",ControlKind="List",ControlDetail="{\"tableId\":\"4\",\"nameField\":\"name\"}"}]);
        c.ConfigPages.Add(new(){Id="401",ConfigTableId="4",WhereSql="yz_project_id=@current_yz_project_id"});
        c.ConfigPageColumns.Add(new(){Id="40142",ConfigPageId="401",ConfigColumnId="42"});c.ConfigPageColumns.Add(new(){Id="30137",ConfigPageId="301",ConfigColumnId="37"});
        if(automatic){c.ConfigColumns.RemoveAll(column=>column.Id=="27");c.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="27");}
        var source=c.ConfigPages.Single(page=>page.Id=="201");source.IsImport=1;source.IsExport=1;source.IsCopy=1;
        c.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        // 隐藏页默认指向外项目候选，复制／默认值仍必须执行内部最终值校验。
        c.ConfigColumns.Single(column=>column.Id=="25").DefaultValue="[\"9007199254750003\"]";
        var hidden=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(source))!;hidden.Id="202";hidden.IsImport=0;hidden.IsExport=0;hidden.IsCopy=0;hidden.Extra.Remove("importMethod");c.ConfigPages.Add(hidden);
        foreach(var field in c.ConfigPageColumns.Where(field=>field.ConfigPageId=="201"&&field.ConfigColumnId!="25").ToArray()){var clone=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;clone.Id="202"+field.Id;clone.ConfigPageId="202";c.ConfigPageColumns.Add(clone);}
        return RedundantMultiNameCacheTests.Published(c);
    }
}
