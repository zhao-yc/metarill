using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>明确联接缓存仍是冗余物理列，只有同一多值来源路径被证明后才交给服务端管理。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class RedundantMultiNameCacheTests
{
    /// <summary>两类控件、原短别名和完整_ids别名都编译真实服务，缓存只读而持久化保持。</summary>
    [Theory][InlineData(false,false,false)][InlineData(false,true,false)][InlineData(true,false,false)][InlineData(true,true,false)][InlineData(false,false,true)][InlineData(true,false,true)]
    public async Task Joined_cache_is_physical_readonly_and_compiles_shared_write_and_name_filter(bool checkbox,bool fullAlias,bool automatic)
    {
        var catalog=Catalog(checkbox);catalog.ConfigColumns.Single(column=>column.Id=="27").Extra["columnFullName"]=JsonSerializer.SerializeToElement(fullAlias?"target_ids.name":"target.name");if(automatic)RemoveCache(catalog);catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);if(checkbox&&!automatic)catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="27").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);var saved=JsonSerializer.Serialize(catalog);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-joined-multi-cache");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var model=GenerationModel.Create(catalog,"Example.JoinedMulti");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);var names=table.Columns.Single(column=>column.Source.ColumnName=="target_names");Assert.True(names.IsManagedMultiNames);Assert.True(names.IsJsonArray);Assert.DoesNotContain(page.Writable,column=>column.Source.Id==names.Source.Id);Assert.Contains(MetadataStoragePolicy.ForTable(catalog,table.Source).PhysicalColumns,column=>column.Id==names.Source.Id);Assert.Equal(automatic,MultiNameStorageCompilation.IsImplicit(model,names));
            var output=Path.Combine(directory,"generated");new CSharpGenerator().Generate(catalog,output,model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var probe=Path.Combine(NonRedundantApplicationFixture.Root(),"backend/tests/Metarill.Generation.Tests/"+(checkbox?"DatabaseCheckboxVueProbe.mjs":"GeneratedMultiSelectVueProbe.mjs"));var start=new System.Diagnostics.ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};start.ArgumentList.Add(probe);start.ArgumentList.Add(output);using(var process=System.Diagnostics.Process.Start(start)!){var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await process.WaitForExitAsync(timeout.Token);}catch(Exception error){await SharedSourceBuildGate.StopOwnProcessAsync(process,stdout,stderr,error);throw;}Assert.True(process.ExitCode==0,await stdout+await stderr);}
            var property=assembly.GetTypes().Single(type=>type.Name=="RelationSource").GetProperty("TargetNames")!;Assert.False(property.GetCustomAttribute<FreeSql.DataAnnotations.ColumnAttribute>()!.IsIgnore);
            var request=assembly.GetTypes().Single(type=>type.Name=="RelationSourcePage201CreateRequest");Assert.Null(request.GetProperty("TargetNames"));var vo=assembly.GetTypes().Single(type=>type.Name=="RelationSourcePage201Vo");Assert.Equal(typeof(JsonElement?),vo.GetProperty("TargetNames")!.PropertyType);
            var source=File.ReadAllText(Path.Combine(output,"Services/RelationSourceService.g.cs"));Assert.DoesNotContain("if (creating ? entity.TargetNames is not null",source);Assert.Contains("originalCandidateValues",source);if(!checkbox||!automatic)Assert.Contains("JsonArrayNameQuery.ApplyJsonArrayAnyLike",source);Assert.Contains("entity.TargetNames",source);Assert.Contains("submitted.Contains(\"target_names\")",source);
            Assert.Equal(saved,JsonSerializer.Serialize(catalog));Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(Published(catalog),model.Namespace)));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>原完整宿主从包内源码构建，缓存字段仍只读，原组件及手写文件经更新保持。</summary>
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public async Task Full_joined_cache_package_builds_and_preserves_original_runtime_and_handwritten_partial(bool checkbox,bool automatic)
    {
        var catalog=Catalog(checkbox);if(automatic)RemoveCache(catalog);Published(catalog);catalog.ConfigMenus.Add(new(){Id="9001",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",Name="多值缓存",Kind="GENERATED_PAGE",ConfigPageId="201"});
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-joined-cache-package");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            var package=new ApplicationPackager().Create(catalog,output,"Example.JoinedCacheApplication",NonRedundantApplicationFixture.Root());Assert.True(package.FrontendIncluded);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            var component=checkbox?"uni_modules/uni-data-checkbox/components/uni-data-checkbox/uni-data-checkbox.vue":"common/yzp_runtime/multiSelectFieldMixin.js";Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(NonRedundantApplicationFixture.Root(),"frontend/metarill_uni_admin",component)),await File.ReadAllBytesAsync(Path.Combine(output,"frontend",component)));
            var hand=Path.Combine(output,"backend/generated/Services/RelationSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 联接名称缓存的手写扩展保留。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(catalog,candidate,"Example.JoinedCacheApplication",NonRedundantApplicationFixture.Root());var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>错误或其他用途的联接标记不能因名称后缀相同而绕过隐藏字段的Full门禁。</summary>
    [Theory][InlineData(false,"missing")][InlineData(false,"alien")][InlineData(false,"deep")][InlineData(false,"virtual")][InlineData(false,"relation")][InlineData(true,"missing")][InlineData(true,"alien")][InlineData(true,"deep")][InlineData(true,"virtual")][InlineData(true,"relation")]
    public void Unproven_cache_is_rejected_even_when_source_control_is_hidden(bool checkbox,string kind)
    {
        var catalog=Catalog(checkbox);var names=catalog.ConfigColumns.Single(column=>column.Id=="27");
        if(kind=="missing")names.Extra.Remove("columnFullName");if(kind=="alien")names.Extra["columnFullName"]=JsonSerializer.SerializeToElement("other.name");if(kind=="deep")names.Extra["columnFullName"]=JsonSerializer.SerializeToElement("target.group.name");if(kind=="virtual")names.IsVirtualColumn=1;if(kind=="relation")names.LinkConfigTableId="1";
        catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="25");var output=Path.Combine(Path.GetTempPath(),"metarill-joined-cache-reject-"+Guid.NewGuid().ToString("N"));
        Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(Published(catalog),output,"Example.RejectedJoinedCache",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>仅将已有物理名称缓存标记为同一原关系路径，不改页面布局、目标范围或存储模式。</summary>
    internal static MetadataCatalog Catalog(bool checkbox,string prefix="relation")
    {
        var catalog=checkbox?DatabaseCheckboxGenerationTests.Catalog(false,false,prefix):GeneratedMultiSelectContractTests.Catalog(prefix);var names=catalog.ConfigColumns.Single(column=>column.Id=="27");names.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);names.Extra["columnFullName"]=JsonSerializer.SerializeToElement("target.name");return catalog;
    }
    /// <summary>模拟仅声明原IDs的配置，自动缓存不得回写配置或增加可见页面字段。</summary>
    internal static void RemoveCache(MetadataCatalog catalog){catalog.ConfigColumns.RemoveAll(column=>column.Id=="27");catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="27");}
    /// <summary>隐藏来源不授予隐式名称读取；显式隐藏缓存也不能借自动规则取得额外投影。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Implicit_cache_does_not_expose_names_when_source_is_hidden(bool checkbox)
    {
        var catalog=Catalog(checkbox);RemoveCache(catalog);catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="25");var model=GenerationModel.Create(catalog,"Example.HiddenAutomaticCache");var table=model.Tables.Single(table=>table.Source.Id=="2");Assert.Empty(MultiNameStorageCompilation.ImplicitNames(model,table,Assert.Single(table.Pages)));var files=BackendRenderer.Render(model).ToArray();var vo=Assert.Single(files,file=>file.Path=="VOs/RelationSourcePage201Vo.g.cs").Content;Assert.DoesNotContain("TargetNames",vo);var controller=Assert.Single(files,file=>file.Path=="Controllers/RelationSourcePage201Controller.g.cs").Content;Assert.DoesNotContain("targetNames",controller);Assert.DoesNotContain("findNameIdTarget25",controller);
    }
    /// <summary>明确包内发布项目与后台，使Full门禁检验的是真实关系证明。</summary>
    internal static MetadataCatalog Published(MetadataCatalog catalog)
    {
        foreach(var record in catalog.ConfigTables.Cast<MetadataRecord>().Concat(catalog.ConfigColumns).Concat(catalog.ConfigPages).Concat(catalog.ConfigPageColumns))record.YzProjectId=catalog.YzProjectId;catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId=catalog.YzProjectId});foreach(var page in catalog.ConfigPages)page.YzAdminTypeId="5";return catalog;
    }
}
