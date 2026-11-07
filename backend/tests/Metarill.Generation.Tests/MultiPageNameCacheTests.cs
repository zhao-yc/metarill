using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>表级缓存与页级投影分离，只有ID展示页面时仍保持根关系名称，其他名称页不能造成陈旧副本。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class MultiPageNameCacheTests
{
    /// <summary>真实生成并编译所有缓存赋值，ID页不获得隐藏名称VO或写DTO。</summary>
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public async Task Id_page_keeps_table_cache_with_or_without_a_visible_name_page(bool automatic,bool idOnly)
    {
        var catalog=Catalog(automatic,idOnly);var saved=JsonSerializer.Serialize(catalog);var boundary=Path.Combine(Path.GetTempPath(),"metarill-multi-page-cache");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var model=GenerationModel.Create(catalog,"Example.MultiPageCache");var table=model.Tables.Single(table=>table.Source.Id=="2");Assert.Equal(idOnly?1:2,MultiNameWriteCompilation.ForTable(model,table).Count);var idPage=table.Pages.Single(page=>page.Source.Id=="203");Assert.Null(Assert.Single(MultiSelectCompilation.ForPage(model,table,idPage)).Names);Assert.Empty(MultiNameStorageCompilation.ImplicitNames(model,table,idPage));
            var output=Path.Combine(directory,"generated");new CSharpGenerator().Generate(catalog,output,model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);await Probe(output,idOnly);var vo=assembly.GetTypes().Single(type=>type.Name=="RelationSourcePage203Vo");Assert.Null(vo.GetProperty("TargetNames"));Assert.Null(vo.GetProperty("TargetTitles"));var request=assembly.GetTypes().Single(type=>type.Name=="RelationSourcePage203UpdateRequest");Assert.Null(request.GetProperty("TargetNames"));Assert.Null(request.GetProperty("TargetTitles"));
            var entity=assembly.GetTypes().Single(type=>type.Name=="RelationSource");Assert.False(entity.GetProperty("TargetNames")!.GetCustomAttribute<FreeSql.DataAnnotations.ColumnAttribute>()!.IsIgnore);var service=File.ReadAllText(Path.Combine(output,"Services/RelationSourceService.g.cs"));Assert.Contains("ValidateMulti203_25Async",service);Assert.Contains("query=query.ForUpdate();",service);Assert.Equal(saved,JsonSerializer.Serialize(catalog));Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(RedundantMultiNameCacheTests.Published(catalog),model.Namespace)));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>包内原页面和缓存代码一起构建，更新仍保护手写partial。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Full_package_preserves_original_pages_and_handwritten_rules(bool automatic)
    {
        var catalog=RedundantMultiNameCacheTests.Published(Catalog(automatic,false));catalog.ConfigMenus.Add(new(){Id="9001",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",Name="ID展示",Kind="GENERATED_PAGE",ConfigPageId="203"});var boundary=Path.Combine(Path.GetTempPath(),"metarill-multi-page-package");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            var package=new ApplicationPackager().Create(catalog,output,"Example.MultiPageApplication",NonRedundantApplicationFixture.Root());Assert.True(package.FrontendIncluded);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);var component="common/yzp_runtime/multiSelectFieldMixin.js";Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(NonRedundantApplicationFixture.Root(),"frontend/metarill_uni_admin",component)),await File.ReadAllBytesAsync(Path.Combine(output,"frontend",component)));
            var hand=Path.Combine(output,"backend/generated/Services/RelationSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 跨页缓存手写规则保留。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(catalog,candidate,"Example.MultiPageApplication",NonRedundantApplicationFixture.Root());var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>同一IDs不能在不同页面表示另一张表，否则不能证明任何表级缓存。</summary>
    [Fact]
    public void Ambiguous_target_overlays_are_rejected_before_output()
    {
        var catalog=Catalog(false,false);catalog.ConfigColumns.Single(column=>column.Id=="25").LinkConfigTableId="";var target=catalog.ConfigTables.Single(table=>table.Id=="3");var other=JsonSerializer.Deserialize<TableMetadata>(JsonSerializer.Serialize(target))!;other.Id="4";other.TableName="other_target";catalog.ConfigTables.Add(other);foreach(var column in catalog.ConfigColumns.Where(column=>column.ConfigTableId=="3").ToArray()){var copy=JsonSerializer.Deserialize<ColumnMetadata>(JsonSerializer.Serialize(column))!;copy.Id="other_"+column.Id;copy.ConfigTableId="4";catalog.ConfigColumns.Add(copy);}var page=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(catalog.ConfigPages.Single(page=>page.Id=="301")))!;page.Id="401";page.ConfigTableId="4";catalog.ConfigPages.Add(page);foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="301").ToArray()){var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;copy.Id="other_"+field.Id;copy.ConfigPageId="401";copy.ConfigColumnId="other_"+field.ConfigColumnId;catalog.ConfigPageColumns.Add(copy);}catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="203"&&field.ConfigColumnId=="25").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"pageId\":\"401\",\"nameField\":\"id\"}");Assert.Throws<InvalidOperationException>(()=>GenerationModel.Create(catalog,"Example.AmbiguousMultiCache"));
    }
    /// <summary>实际编译并挂载原列表／详情，确认页级ID与标题映射、原弹层确认和取消语义。</summary>
    private static async Task Probe(string output,bool idOnly)
    {
        var start=new System.Diagnostics.ProcessStartInfo("node"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};start.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.Root(),"backend/tests/Metarill.Generation.Tests/MultiPageNameCacheVueProbe.mjs"));start.ArgumentList.Add(output);start.ArgumentList.Add(idOnly?"idOnly":"all");using var process=System.Diagnostics.Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await process.WaitForExitAsync(timeout.Token);}catch(Exception error){await SharedSourceBuildGate.StopOwnProcessAsync(process,stdout,stderr,error);throw;}Assert.True(process.ExitCode==0,await stdout+await stderr);
    }
    /// <summary>根名称、另一字符串名称与ID页面共享同一物理来源；idOnly验证没有可见根名称页的情况。</summary>
    internal static MetadataCatalog Catalog(bool automatic,bool idOnly)
    {
        var catalog=GeneratedMultiSelectContractTests.Catalog();AddIdPage(catalog);if(!idOnly)AddTitlePage(catalog);else{catalog.ConfigPages.RemoveAll(page=>page.Id=="201");catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="201");}if(automatic)RedundantMultiNameCacheTests.RemoveCache(catalog);return catalog;
    }
    /// <summary>保留原页面模型，只覆盖原控件允许的nameField，不创建新交互。</summary>
    private static void AddIdPage(MetadataCatalog catalog)
    {
        var page=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(catalog.ConfigPages.Single(page=>page.Id=="201")))!;page.Id="203";catalog.ConfigPages.Add(page);foreach(var id in new[]{"22","25","26"})catalog.ConfigPageColumns.Add(new(){Id="id_"+id,ConfigPageId="203",ConfigColumnId=id,IsShow=1,IsEdit=1,Extra=id=="25"?new(){["controlDetail"]=JsonSerializer.SerializeToElement("{\"nameField\":\"id\"}")}:[]});
    }
    /// <summary>实际同表第二名称页，名称字段和缓存路径都显式声明。</summary>
    internal static void AddTitlePage(MetadataCatalog catalog)
    {
        catalog.ConfigColumns.Add(new(){Id="38",ConfigTableId="3",ColumnName="title",LogicalType="string",ColumnLength="100"});catalog.ConfigPageColumns.Add(new(){Id="title_target",ConfigPageId="301",ConfigColumnId="38",IsShow=1,IsEdit=1});catalog.ConfigColumns.Add(new(){Id="28",ConfigTableId="2",ColumnName="target_titles",LogicalType="string",ColumnLength="max",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("target.title")}});
        var page=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(catalog.ConfigPages.Single(page=>page.Id=="201")))!;page.Id="204";catalog.ConfigPages.Add(page);foreach(var id in new[]{"22","25","26","28"})catalog.ConfigPageColumns.Add(new(){Id="title_"+id,ConfigPageId="204",ConfigColumnId=id,IsShow=1,IsEdit=id=="28"?0:1,Extra=id=="25"?new(){["controlDetail"]=JsonSerializer.SerializeToElement("{\"nameField\":\"title\"}")}:[]});
    }
}
