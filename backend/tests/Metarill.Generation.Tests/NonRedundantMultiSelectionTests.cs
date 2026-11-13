using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>非冗余多选以物理ID数组存储，生成VO和名称筛选沿原接口；不修改页面或名称缓存。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class NonRedundantMultiSelectionTests
{
    /// <summary>真实生成并编译，名称不进入请求／物理列，固定筛选在SQL分页之前处理。</summary>
    [Theory]
    [InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public async Task Generates_nonredundant_multi_without_persisting_names(bool searchable,bool automatic)
    {
        var catalog=Catalog();if(automatic){catalog.ConfigColumns.RemoveAll(column=>column.Id=="27");catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="27");}if(searchable)catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nonred-multi");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.NonRedMulti");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var entity=assembly.GetType("Example.NonRedMulti.Generated.Entities.RelationSource",true)!;
            var names=entity.GetProperty("TargetNames");if(automatic)Assert.Null(names);else{var column=Assert.IsType<FreeSql.DataAnnotations.ColumnAttribute>(names!.GetCustomAttribute<FreeSql.DataAnnotations.ColumnAttribute>());Assert.True(column.IsIgnore);}
            var vo=assembly.GetType("Example.NonRedMulti.Generated.VOs.RelationSourcePage201Vo")??assembly.GetTypes().Single(type=>type.Name=="RelationSourcePage201Vo");
            Assert.Equal(typeof(JsonElement?),vo.GetProperty("TargetNames")!.PropertyType);Assert.Equal(typeof(JsonElement?),vo.GetProperty("TargetIds")!.PropertyType);
            var request=assembly.GetTypes().Single(type=>type.Name=="RelationSourcePage201CreateRequest");Assert.Null(request.GetProperty("TargetNames"));
            var source=File.ReadAllText(Path.Combine(directory,"generated/Services/RelationSourceService.g.cs"));
            Assert.DoesNotContain("entity.TargetNames =",source);Assert.Contains("query=query.ForUpdate()",source);
            Assert.DoesNotContain("target_names",MetadataStoragePolicy.ForTable(catalog,catalog.ConfigTables.Single(table=>table.Id=="2")).PhysicalColumns.Select(field=>field.ColumnName));
            await RunOriginalVueProbe(Path.Combine(directory,"generated"));
            if(searchable)
            {
                var service=assembly.GetTypes().Single(type=>type.Name=="RelationSourceService");
                var filters=(IReadOnlyList<FixedReadFilter>)service.GetMethod("ReadFilters201",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[new PageRequest{ParentId="71",Filters=new(){{"targetNames",JsonSerializer.SerializeToElement("中文_%!引号'")}}},new RuntimeActor(1,"多选合同",false,"54","5")])!;
                var filter=Assert.Single(filters,item=>item.Field=="target_names");Assert.Equal(FixedReadComparison.JsonArrayAnyContains,filter.Comparison);Assert.Equal("中文_%!引号'",filter.Value);
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>实际编译挂载原Vue模板，验证确认/取消/长ID和只读迟到；不是浏览器验收。</summary>
    private static async Task RunOriginalVueProbe(string output,[System.Runtime.CompilerServices.CallerFilePath]string source="")
    {
        var start=new System.Diagnostics.ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(source)!,"GeneratedMultiSelectVueProbe.mjs"));start.ArgumentList.Add(output);
        using var process=System.Diagnostics.Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await process.WaitForExitAsync(timeout.Token);}catch(Exception error){await SharedSourceBuildGate.StopOwnProcessAsync(process,stdout,stderr,error);throw;}Assert.True(process.ExitCode==0,await stdout+await stderr);
    }
    /// <summary>正式FullApplication装配并从包内基础源码构建，不能只让仓库引用构建通过。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Full_multi_application_builds_with_explicit_or_automatic_names(bool automatic)
    {
        var catalog=Catalog();if(automatic){catalog.ConfigColumns.RemoveAll(column=>column.Id=="27");catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="27");}
        foreach(var record in catalog.ConfigTables.Cast<MetadataRecord>().Concat(catalog.ConfigColumns).Concat(catalog.ConfigPages).Concat(catalog.ConfigPageColumns))record.YzProjectId=catalog.YzProjectId;
        catalog.YzAdminTypes.Add(new(){Id="5",Name="多选后台",YzProjectId=catalog.YzProjectId});foreach(var page in catalog.ConfigPages)page.YzAdminTypeId="5";
        catalog.ConfigMenus.Add(new(){Id="9001",Name="来源",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",Kind="GENERATED_PAGE",ConfigPageId="201"});
        var pageSource=catalog.ConfigPages.Single(page=>page.Id=="201");pageSource.IsImport=1;pageSource.IsExport=1;
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nonred-multi-package");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,"Example.MultiApplication")));
            var package=new ApplicationPackager().Create(catalog,output,"Example.MultiApplication",NonRedundantApplicationFixture.Root());Assert.True(package.FrontendIncluded);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(output,"src/Metarill.Data/Queries/MySqlNonRedundantReadPlan.cs")),await File.ReadAllBytesAsync(Path.Combine(NonRedundantApplicationFixture.Root(),"backend/src/Metarill.Data/Queries/MySqlNonRedundantReadPlan.cs")));
            using var capabilities=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")));var subset=Assert.Single(capabilities.RootElement.GetProperty("supportedControlSubsets").EnumerateArray());Assert.Equal("nonRedundant",subset.GetProperty("storageMode").GetString());Assert.True(subset.GetProperty("nonRedundantSupported").GetBoolean());Assert.False(subset.GetProperty("renamePropagationSupported").GetBoolean());
            var handwritten=Path.Combine(output,"backend/generated/Services/RelationSourceService.cs");await File.AppendAllTextAsync(handwritten,"\n// 非冗余多选包手写保护标记。\n");var bytes=await File.ReadAllBytesAsync(handwritten);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(catalog,candidate,"Example.MultiApplication",NonRedundantApplicationFixture.Root());
            var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(handwritten));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>借用其它关系路径／真实物理名称列必须拒绝，不能将旧名称继续持久化或串目标。</summary>
    [Theory]
    [InlineData("physical")][InlineData("wrongPath")][InlineData("credential")][InlineData("nameOverride")][InlineData("targetOverride")]
    public void Rejects_unproven_names_before_writing(string scenario)
    {
        var catalog=Catalog();var names=catalog.ConfigColumns.Single(column=>column.Id=="27");
        if(scenario=="physical")names.Extra.Clear();
        if(scenario=="wrongPath")names.Extra["columnFullName"]=JsonSerializer.SerializeToElement("parent.name");
        if(scenario=="credential")catalog.ConfigColumns.Single(column=>column.Id=="32").ColumnName="password";
        if(scenario=="nameOverride")catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"nameField\":\"alias\"}");
        if(scenario=="targetOverride"){catalog.ConfigColumns.Single(column=>column.Id=="25").LinkConfigTableId="";catalog.ConfigPages.Single(page=>page.Id=="101").Extra["type"]=JsonSerializer.SerializeToElement(2);catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"pageId\":\"101\"}");}
        var output=Path.Combine(Path.GetTempPath(),"metarill-nonred-multi-reject",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new CSharpGenerator().Generate(catalog,output,"Example.NonRedMulti"));Assert.False(Directory.Exists(output));
    }
    /// <summary>沿既有多选元数据，仅将名称改成明确只读联接，保持原页与范围。</summary>
    internal static MetadataCatalog Catalog(string prefix="relation")
    {
        var catalog=GeneratedMultiSelectContractTests.Catalog(prefix);catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=0;
        var name=catalog.ConfigColumns.Single(column=>column.Id=="27");name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("target.name");
        return catalog;
    }
}
