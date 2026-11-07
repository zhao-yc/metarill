using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>原type=1单选页使用R0目标的单值派生名称，不改页面、回执或辅助字段。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class DerivedSingleSelectionTests
{
    /// <summary>无目标父与同父模型均从真实元数据生成编译，名称筛选共用固定SQL键。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Derived_select_page_generates_original_name_filter_and_import_export(bool parent)
    {
        var c=Catalog("derived_single",parent);var before=JsonSerializer.Serialize(c);var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-single");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),"Example.DerivedSingle");await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.Equal(before,JsonSerializer.Serialize(c));
            var model=GenerationModel.Create(c,"Example.DerivedSingle");var table=model.Tables.Single(table=>table.Source.Id=="1");var plan=SelectionNameFilterCompilation.ForPage(model,table,table.Pages.Single(page=>page.Source.Id=="101"));Assert.Empty(plan.Gaps);Assert.Equal("customerCompanyName",Assert.Single(plan.Bindings).FilterKey);Assert.Empty(GenerationCapabilityAudit.Collect(model));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>完整包构建、原Vue回执及更新保护共同证明，没有另写单选页面。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Full_derived_selection_keeps_original_vue_and_handwritten_partial(bool parent)
    {
        var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-single-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");var c=Catalog("derived_single",parent);
        try
        {
            Assert.True(new ApplicationPackager().Create(c,output,"Example.DerivedSingleApplication",root).FrontendIncluded);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            foreach(var asset in new[]{"common/yzp_runtime/originalSelectionPageMixin.js","common/yzp_runtime/selectionPageMixin.js"})Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin",asset)),File.ReadAllBytes(Path.Combine(output,"frontend",asset)));
            var start=new System.Diagnostics.ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};foreach(var arg in new[]{Path.Combine(root,"backend/tests/Metarill.Generation.Tests/NonRedundantSingleSelectionProbe.mjs"),output,parent?"parent":"plain","derived_single","customerCompanyName","companyName"})start.ArgumentList.Add(arg);using var process=System.Diagnostics.Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await process.WaitForExitAsync(timeout.Token);}catch(Exception error){await SharedSourceBuildGate.StopOwnProcessAsync(process,stdout,stderr,error);throw;}Assert.True(process.ExitCode==0,await stdout+await stderr);
            using(var capabilities=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")))){var names=capabilities.RootElement.GetProperty("supportedControlSubsets").EnumerateArray().Where(item=>item.GetProperty("controlKind").GetString()=="SelectPage").ToArray();Assert.Equal(parent?3:2,names.Length);Assert.All(names,item=>Assert.True(item.GetProperty("derivedNameSupported").GetBoolean()));}
            var hand=Path.Combine(output,"backend/generated/Services/DerivedSingleOrderService.cs");await File.AppendAllTextAsync(hand,"\n// 原单选派生名称手写保护。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(c,candidate,"Example.DerivedSingleApplication",root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>未证明名称、目标、父／附加条件或终点在写stage之前拒绝，隐藏控制也不能绕过。</summary>
    [Theory][InlineData("global")][InlineData("type2")][InlineData("hiddenName")][InlineData("broken")][InlineData("credential")][InlineData("array")][InlineData("stringKey")][InlineData("scope")][InlineData("form")][InlineData("hiddenBad")][InlineData("nameOverride")]
    public void Invalid_derived_single_selection_is_rejected_before_staging(string kind)
    {
        var c=Catalog("derived_single",true);var name=c.ConfigColumns.Single(column=>column.Id=="28");
        if(kind=="global")c.ConfigTables.Single(table=>table.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind=="type2")c.ConfigPages.Single(page=>page.Id=="201").Extra["type"]=JsonSerializer.SerializeToElement(2);
        if(kind=="hiddenName")c.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="28");
        if(kind is "broken" or "hiddenBad")name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("missing.name");
        if(kind=="hiddenBad")c.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="15");
        if(kind=="credential"){c.ConfigColumns.Single(column=>column.Id=="44").ColumnName="password";name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.password");}
        if(kind=="array")c.ConfigColumns.Single(column=>column.Id=="44").ControlKind="FileLibrary";
        if(kind=="stringKey")c.ConfigColumns.Single(column=>column.Id=="27").LogicalType="string";
        if(kind=="scope")c.ConfigPages.Single(page=>page.Id=="201").WhereSql+=" AND company_name='X'";
        if(kind=="form")c.ConfigColumns.Single(column=>column.Id=="15").ControlDetail="{\"pageId\":\"201\",\"nameField\":\"company_name\",\"whereSql\":\"segment=@formParameters.segment\"}";
        if(kind=="nameOverride")c.ConfigPageColumns.Single(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="15").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"nameField\":\"name\"}");
        var output=Path.Combine(Path.GetTempPath(),"metarill-derived-single-reject",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.InvalidDerivedSingle",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>客户的company_name继续沿既有联接，单选附加条件只使用目标页已经执行的固定条件。</summary>
    internal static MetadataCatalog Catalog(string prefix,bool parent)
    {
        var c=DerivedRelationNamesTests.Catalog(prefix,false);var source=c.ConfigColumns.Single(column=>column.Id=="15");source.ControlKind="SelectPage";source.ControlDetail=JsonSerializer.Serialize(new{pageId="201",tableId="2",table=prefix+"_party",nameField="company_name",whereSql=parent?"status=1 AND segment=1":"status=1",buttonText="选择客户"});
        var target=c.ConfigPages.Single(page=>page.Id=="201");target.Extra["type"]=JsonSerializer.SerializeToElement(1);c.ConfigPageColumns.Single(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="15").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);c.ConfigPageColumns.Single(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="113").Extra["isSearch"]=JsonSerializer.SerializeToElement(0);
        if(parent)
        {
            c.ConfigColumns.Add(new(){Id="29",ConfigTableId="2",ColumnName="group_id",LogicalType="int64",LinkConfigTableId="3"});c.ConfigPageColumns.Add(new(){Id="20129",ConfigPageId="201",ConfigColumnId="29"});target.WhereSql+=" AND group_id=@parent_id AND segment=1";
            foreach(var (id,table) in new[]{("112","1"),("211","2")}){c.ConfigColumns.Add(new(){Id=id,ConfigTableId=table,ColumnName="segment",Name="分组条件",LogicalType="int32",DefaultValue="1"});c.ConfigPageColumns.Add(new(){Id=table+id,ConfigPageId=table=="1"?"101":"201",ConfigColumnId=id});}
        }
        if(parent)
        {
            var original=c.ConfigPages.Single(page=>page.Id=="101");var mapped=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(original))!;mapped.Id="106";mapped.WhereSql="yz_project_id=@current_yz_project_id";c.ConfigPages.Add(mapped);
            foreach(var field in c.ConfigPageColumns.Where(field=>field.ConfigPageId=="101").ToArray()){var clone=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;clone.Id="106"+field.Id;clone.ConfigPageId="106";if(clone.ConfigColumnId=="19"){clone.IsShow=1;clone.IsEdit=1;}c.ConfigPageColumns.Add(clone);}
            c.ConfigColumns.Single(column=>column.Id=="19").Name="目标组";
        }
        c.ConfigColumns.Single(column=>column.Id=="28").Name="公司名称";
        c.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="28").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
                foreach(var record in c.ConfigColumns.Cast<MetadataRecord>().Concat(c.ConfigPageColumns))if(string.IsNullOrWhiteSpace(record.YzProjectId))record.YzProjectId=c.YzProjectId;
        return c;
    }
}
