using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>正式完整应用包携带表单运行时与生成Service，不借用原仓库Core/Data引用。</summary>
public sealed class FormConditionApplicationPackageTests
{
    /// <summary>真实打包、受管更新、手写保护与包内restore/build覆盖新增表单合同及源类型绑定。</summary>
    [Fact]
    public async Task Complete_package_builds_and_updates_form_conditions_with_its_own_runtime()
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-form-application");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            var catalog=Catalog();var package=new ApplicationPackager().Create(catalog,output,"Example.FormApplication",NonRedundantApplicationFixture.Root());Assert.Equal(ApplicationPackageMode.FullApplication,package.Mode);Assert.True(package.FrontendIncluded);
            Assert.True(File.Exists(Path.Combine(output,"src/Metarill.Core/Contracts/FixedConditionFormValue.cs")));
            var handwritten=Path.Combine(output,"backend/generated/Services/MtFormApplicationOrderService.cs");await File.AppendAllTextAsync(handwritten,"\n// 表单条件完整包的手写扩展保护。\n");var original=await File.ReadAllBytesAsync(handwritten);
            var next=Catalog();next.ConfigColumns.Single(column=>column.Id=="130").Name="更新后的分类";var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(next,candidate,"Example.FormApplication",NonRedundantApplicationFixture.Root());
            var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);Assert.Equal("committed",updater.Apply(plan,plan.PlanSha256).Status);Assert.Equal(original,await File.ReadAllBytesAsync(handwritten));
            var repeated=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(repeated.CanApply);Assert.Equal("unchanged",updater.Apply(repeated,repeated.PlanSha256).Status);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>以已验收的正式宿主模型构建List联动夹具，所有页面的依赖由元数据声明。</summary>
    private static MetadataCatalog Catalog()
    {
        var catalog=NonRedundantApplicationFixture.Catalog("mt_form_application");
        catalog.ConfigColumns.AddRange([new(){Id="130",YzProjectId=catalog.YzProjectId,ConfigTableId="1",ColumnName="allowed",Name="分类",LogicalType="string",DefaultValue="%",Extra=new(){["isAutoPostBack"]=JsonSerializer.SerializeToElement(1)}},new(){Id="131",YzProjectId=catalog.YzProjectId,ConfigTableId="1",ColumnName="minimum",Name="最小ID",LogicalType="int64",DefaultValue="1",Extra=new(){["isAutoPostBack"]=JsonSerializer.SerializeToElement(1)}}]);
        foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))foreach(var id in new[]{"130","131"})catalog.ConfigPageColumns.Add(new(){Id=page.Id+id,YzProjectId=catalog.YzProjectId,ConfigPageId=page.Id,ConfigColumnId=id,IsShow=1,IsEdit=page.Id=="102"?0:1});
        var source=catalog.ConfigColumns.Single(column=>column.Id=="15");source.ControlKind="List";source.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("201");source.ControlDetail=JsonSerializer.Serialize(new{tableId="2",nameField="name",whereSql="name LIKE @formParameters.allowed AND id>=@formParameters.minimum"});
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigColumnId=="19"&&field.ConfigPageId is "104" or "105"))field.Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        return catalog;
    }
}
