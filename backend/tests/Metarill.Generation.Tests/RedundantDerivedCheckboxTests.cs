using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>R1派生复选必须同时具备原候选／写入、物理数组缓存和完整传播维护。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class RedundantDerivedCheckboxTests
{
    /// <summary>不依赖自树或多选同伴引入查询命名空间，显式／自动缓存分别真实生成编译。</summary>
    [Theory][InlineData(false,"string")][InlineData(false,"int64")][InlineData(true,"string")][InlineData(true,"int64")]
    public async Task Standalone_checkbox_builds_and_publishes_complete_cache(bool automatic,string parent)
    {
        var c=Catalog("relation",automatic,parent);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.R1Checkbox");Assert.Empty(GenerationCapabilityAudit.Collect(model));Assert.Contains("target_names",MixedRelationPropagationCompilation.ForModel(model).MixedSourceColumns["relation_source"]);Assert.Contains("target_names",MetadataStoragePolicy.ForTable(c,c.ConfigTables.Single(t=>t.Id=="2")).PhysicalColumns.Select(x=>x.ColumnName));Assert.DoesNotContain("name",MetadataStoragePolicy.ForTable(c,c.ConfigTables.Single(t=>t.Id=="3")).PhysicalColumns.Select(x=>x.ColumnName));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-r1-derived-checkbox");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var maintenance=(RelationMaintenanceCatalog)assembly.GetType(model.Namespace+".Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null)!;Assert.Contains("target_names",maintenance.MixedSourceColumns["relation_source"]);Assert.Equal(before,JsonSerializer.Serialize(c));}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>完整包挂载原复选、实际构建并验证两轮更新稳定，不用简化前端替代。</summary>
    [Theory][InlineData(false,"string")][InlineData(false,"int64")][InlineData(true,"string")][InlineData(true,"int64")]
    public Task Full_original_checkbox_keeps_layout_and_handwritten_partial(bool automatic,string parent)=>Full(automatic,parent,false);
    /// <summary>多值名称继续经过R0中间节点，不能只证明一层联接或省略反向依赖。</summary>
    [Theory][InlineData(false,"int64")][InlineData(true,"string")]
    public Task Full_nested_checkbox_keeps_original_component_and_cache(bool automatic,string parent)=>Full(automatic,parent,true);
    /// <summary>原完整包和嵌套包共用实际构建、原Vue及重复更新验证。</summary>
    private static async Task Full(bool automatic,string parent,bool nested)
    {
        var root=NonRedundantApplicationFixture.Root();var c=Catalog("relation",automatic,parent,nested);var boundary=Path.Combine(Path.GetTempPath(),"metarill-r1-derived-checkbox-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"app");
        try
        {
            new ApplicationPackager().Create(c,output,"Example.R1CheckboxFull",root);foreach(var command in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,command);const string component="uni_modules/uni-data-checkbox/components/uni-data-checkbox/uni-data-checkbox.vue";Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin",component)),File.ReadAllBytes(Path.Combine(output,"frontend",component)));
            var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"DatabaseCheckboxVueProbe.mjs"));probe.ArgumentList.Add(output);await NonRedundantApplicationFixture.RunAsync(probe);
            using(var json=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")))){var cap=Assert.Single(json.RootElement.GetProperty("supportedControlSubsets").EnumerateArray(),x=>x.GetProperty("controlKind").GetString()=="MultiCheckBoxDatabase");Assert.Equal("redundant",cap.GetProperty("storageMode").GetString());Assert.True(cap.GetProperty("derivedNameSupported").GetBoolean());Assert.True(cap.GetProperty("renamePropagationSupported").GetBoolean());}
            var hand=Path.Combine(output,"backend/generated/Services/RelationSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 复选派生缓存手写保护。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(c,candidate,"Example.R1CheckboxFull",root);var updater=new LocalGenerationUpdater();var actions=new[]{"generate-project-config","sync-runtime","generate-all-page"};var plan=updater.Preview(output,candidate,actions);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));plan=updater.Preview(output,candidate,actions);Assert.Equal("unchanged",updater.Apply(plan,plan.PlanSha256).Status);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>原来源父和候选父按各自物理键处理；只改变存储模式，不重写页面。</summary>
    internal static MetadataCatalog Catalog(string prefix,bool automatic,string parent,bool nested=false)
    {
        var c=DerivedCheckboxNamesTests.Catalog(prefix,automatic);c.ConfigTables.Single(t=>t.Id=="2").IsRongyu=1;foreach(var column in c.ConfigColumns.Where(x=>x.ConfigTableId is "2" or "3"&&x.ColumnName=="parent_id"))column.LogicalType=parent;
        if(nested)
        {
            c.ConfigTables.Single(t=>t.Id=="4").IsRongyu=0;c.ConfigColumns.Single(x=>x.Id=="42").Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("brand.label")};c.ConfigTables.Add(new(){Id="6",YzProjectId=c.YzProjectId,TableName=prefix+"_brand",IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="61",ConfigTableId="6",YzProjectId=c.YzProjectId,ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="62",ConfigTableId="6",YzProjectId=c.YzProjectId,ColumnName="label",LogicalType="string"},new(){Id="63",ConfigTableId="6",YzProjectId=c.YzProjectId,ColumnName="yz_project_id",LogicalType="int64"},new(){Id="64",ConfigTableId="6",YzProjectId=c.YzProjectId,ColumnName="delete_no",LogicalType="datetime"},new(){Id="45",ConfigTableId="4",YzProjectId=c.YzProjectId,ColumnName="brand_id",LogicalType="int64",LinkConfigTableId="6"}]);c.ConfigPages.Add(new(){Id="601",ConfigTableId="6",YzProjectId=c.YzProjectId,YzAdminTypeId="5",WhereSql="yz_project_id=@current_yz_project_id"});c.ConfigPageColumns.AddRange([new(){Id="60162",ConfigPageId="601",ConfigColumnId="62",YzProjectId=c.YzProjectId,IsShow=1,IsEdit=1},new(){Id="40145",ConfigPageId="401",ConfigColumnId="45",YzProjectId=c.YzProjectId,IsShow=1,IsEdit=1}]);
        }
        return c;
    }
}
