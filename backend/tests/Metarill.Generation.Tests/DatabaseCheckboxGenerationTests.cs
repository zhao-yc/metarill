using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>真实生成并编译通用复选，原下拉使用ID成员筛选，名称从物理缓存或固定联接读取。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class DatabaseCheckboxGenerationTests
{
    /// <summary>冗余及显式／自动非冗余名称均生成同一原候选路由，不写手工业务或新弹层。</summary>
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(true,true)]
    public async Task Generates_checkbox_contracts_and_protects_handwritten_partial(bool nonRedundant,bool automatic)
    {
        var catalog=Catalog(nonRedundant,automatic);var page=catalog.ConfigPages.Single(item=>item.Id=="201");page.IsImport=1;page.IsExport=1;var boundary=Path.Combine(Path.GetTempPath(),"metarill-checkbox-generation");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var output=Path.Combine(directory,"generated");new CSharpGenerator().Generate(catalog,output,"Example.Checkbox");
            var source=File.ReadAllText(Path.Combine(output,"Services/RelationSourceService.g.cs"));
            Assert.Contains("CheckboxScope201_25Async",source);Assert.Contains("DatabaseCheckboxValue.Parse",source);Assert.Contains("query.ForUpdate()",source);Assert.DoesNotContain("RequirePagePermission(\"301\", \"list\")",source);
            var controller=File.ReadAllText(Path.Combine(output,"Controllers/RelationSourcePage201Controller.g.cs"));Assert.Contains("findNameIdTarget25",controller);Assert.Contains("RelationOptionAuthorization.Require",controller);
            var start=new System.Diagnostics.ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};start.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.Root(),"backend/tests/Metarill.Generation.Tests/DatabaseCheckboxVueProbe.mjs"));start.ArgumentList.Add(output);
            using(var process=System.Diagnostics.Process.Start(start)!){var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await process.WaitForExitAsync(timeout.Token);}catch(Exception error){await SharedSourceBuildGate.StopOwnProcessAsync(process,stdout,stderr,error);throw;}Assert.True(process.ExitCode==0,await stdout+await stderr);}
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var request=assembly.GetTypes().Single(type=>type.Name=="RelationSourcePage201CreateRequest");Assert.Null(request.GetProperty("TargetNames"));
            var vo=assembly.GetTypes().Single(type=>type.Name=="RelationSourcePage201Vo");Assert.Equal(typeof(JsonElement?),vo.GetProperty("TargetIds")!.PropertyType);Assert.Equal(typeof(JsonElement?),vo.GetProperty("TargetNames")!.PropertyType);
            if(nonRedundant)
            {
                var service=assembly.GetTypes().Single(type=>type.Name=="RelationSourceService");var filters=(IReadOnlyList<FixedReadFilter>)service.GetMethod("ReadFilters201",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[new PageRequest{ParentId="71",Filters=new(){{"targetIds",JsonSerializer.SerializeToElement("9007199254740993")}}},new RuntimeActor(1,"复选合同",false,"54","5")])!;
                Assert.Equal(FixedReadComparison.JsonArrayHasId,Assert.Single(filters,item=>item.Field=="target_ids").Comparison);
                Assert.DoesNotContain("target_names",MetadataStoragePolicy.ForTable(catalog,catalog.ConfigTables.Single(table=>table.Id=="2")).PhysicalColumns.Select(column=>column.ColumnName));
            }
            else Assert.Contains("JsonArrayNameQuery.ApplyJsonArrayHasId",source);
            var hand=Path.Combine(output,"Services/RelationSourceService.cs");File.AppendAllText(hand,"\n// 手写复选规则保留\n");var original=File.ReadAllBytes(hand);new CSharpGenerator().Generate(catalog,output,"Example.Checkbox");Assert.Equal(original,File.ReadAllBytes(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>使用包内基础源码构建正式宿主，重复更新只替换归属产物，原复选组件与手写代码保持。</summary>
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(true,true)]
    public async Task Full_checkbox_package_builds_and_updates_original_frontend_without_replacing_handwritten_code(bool nonRedundant,bool automatic)
    {
        var catalog=Published(Catalog(nonRedundant,automatic));var boundary=Path.Combine(Path.GetTempPath(),"metarill-checkbox-package");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            var model=GenerationModel.Create(catalog,"Example.CheckboxApplication");Assert.Empty(GenerationCapabilityAudit.Collect(model));var package=new ApplicationPackager().Create(catalog,output,model.Namespace,NonRedundantApplicationFixture.Root());Assert.True(package.FrontendIncluded);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            const string component="uni_modules/uni-data-checkbox/components/uni-data-checkbox/uni-data-checkbox.vue";Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(NonRedundantApplicationFixture.Root(),"frontend/metarill_uni_admin",component)),await File.ReadAllBytesAsync(Path.Combine(output,"frontend",component)));
            using var profile=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")));var subset=Assert.Single(profile.RootElement.GetProperty("supportedControlSubsets").EnumerateArray());Assert.Equal("MultiCheckBoxDatabase",subset.GetProperty("controlKind").GetString());Assert.Equal(5000,subset.GetProperty("maximumItems").GetInt32());Assert.False(subset.GetProperty("renamePropagationSupported").GetBoolean());
            var hand=Path.Combine(output,"backend/generated/Services/RelationSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 通用复选包手写业务保留。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(catalog,candidate,model.Namespace,NonRedundantApplicationFixture.Root());var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>未证明目标、隐藏坏条件及跨项目发布不能借原UI外壳通过Full门禁。</summary>
    [Theory][InlineData("global")][InlineData("derivedName")][InlineData("targetRealm")][InlineData("hiddenInvalid")][InlineData("provider")][InlineData("kindOverlay")]
    public async Task Full_checkbox_rejects_unproven_or_unpublished_declarations_before_output(string kind)
    {
        var catalog=Published(Catalog());var source=catalog.ConfigColumns.Single(column=>column.Id=="25");
        if(kind=="global")catalog.ConfigTables.Single(table=>table.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind=="derivedName")catalog.ConfigColumns.Single(column=>column.Id=="32").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        if(kind=="targetRealm")catalog.ConfigTables.Single(table=>table.Id=="3").YzProjectId="";
        if(kind=="hiddenInvalid"){source.ControlDetail="头;头;password=@actor;id desc";catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="25");}
        if(kind=="provider")catalog.DatabaseProvider="postgresql";
        if(kind=="kindOverlay")catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["controlKind"]=JsonSerializer.SerializeToElement("List");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-checkbox-reject");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(catalog,output,"Example.RejectedCheckbox",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));}finally{if(Directory.Exists(output))await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>发布测试使用明确的同项目表、字段、页面及菜单后台，绝不借空归属模拟正式包。</summary>
    private static MetadataCatalog Published(MetadataCatalog catalog)
    {
        foreach(var record in catalog.ConfigTables.Cast<MetadataRecord>().Concat(catalog.ConfigColumns).Concat(catalog.ConfigPages).Concat(catalog.ConfigPageColumns))record.YzProjectId=catalog.YzProjectId;
        catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId=catalog.YzProjectId,Name="复选后台"});foreach(var page in catalog.ConfigPages)page.YzAdminTypeId="5";
        catalog.ConfigMenus.Add(new(){Id="9001",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",Name="复选关系",Kind="GENERATED_PAGE",ConfigPageId="201"});return catalog;
    }
    /// <summary>缺少真实项目列或错误范围类型在静态模型阶段拒绝，不产出会宽读或无法编译的候选。</summary>
    [Theory][InlineData("sourceProject")][InlineData("targetProjectCache")][InlineData("parentProjectCache")][InlineData("boolParentScope")]
    public void Checkbox_requires_real_record_identity_and_typed_scope_before_rendering(string kind)
    {
        var catalog=Catalog(false);var source=catalog.ConfigColumns.Single(column=>column.Id=="25");
        if(kind=="sourceProject"){catalog.ConfigColumns.Single(column=>column.Id=="23").ColumnName="project_unknown";catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql="parent_id=@parentId";}
        if(kind=="targetProjectCache")catalog.ConfigColumns.Single(column=>column.Id=="33").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        if(kind=="parentProjectCache")catalog.ConfigColumns.Single(column=>column.Id=="13").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        if(kind=="boolParentScope"){catalog.ConfigColumns.Single(column=>column.Id=="36").LogicalType="bool";catalog.ConfigPages.Single(page=>page.Id=="301").WhereSql="yz_project_id=@current_yz_project_id";}
        var model=GenerationModel.Create(catalog,"Example.CheckboxIdentity");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);var result=DatabaseCheckboxCompilation.Resolve(model,table,page,table.Columns.Single(column=>column.Source.Id==source.Id));Assert.Null(result.Definition);Assert.NotNull(result.Reason);
    }
    /// <summary>原多选模型仅换回数据库复选定义，页面布局及既有父范围保持。</summary>
    internal static MetadataCatalog Catalog(bool nonRedundant=true,bool automatic=false,string prefix="relation")
    {
        var catalog=GeneratedMultiSelectContractTests.Catalog(prefix);var source=catalog.ConfigColumns.Single(column=>column.Id=="25");source.ControlKind="MultiCheckBoxDatabase";source.ControlDetail="旧第一段;旧第二段;parent_id=@parentId;name desc";
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        if(nonRedundant){catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=0;var name=catalog.ConfigColumns.Single(column=>column.Id=="27");name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("target.name");}
        if(automatic){catalog.ConfigColumns.RemoveAll(column=>column.Id=="27");catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="27");}
        return catalog;
    }
}
