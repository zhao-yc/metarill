using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>R1派生自树沿原页面和同一维护权威，固定派生路径不能只开放读写而遗漏传播。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class RedundantDerivedSelfTreeTests
{
    /// <summary>八种目录逐页证明四键的派生叶子、物理缓存和下游闭包，输入元数据不变。</summary>
    [Theory][InlineData("string","string")][InlineData("string","int64")][InlineData("int64","string")][InlineData("int64","int64")]
    public void All_derived_self_and_mixed_pages_share_complete_cache_authority(string type,string parent)
    {
        foreach(var shape in new[]{"explicit","automatic","caption","nested","double","mixed","shared","globalShared"})
        {
            var c=Catalog("r1_self",type,parent,shape);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.DerivedSelf");var source=model.Tables.Single(t=>t.Source.Id=="2");Assert.Empty(GenerationCapabilityAudit.Collect(model));var derived=RelationPropagationCompilation.DerivedBindings(model);var cache=MixedRelationPropagationCompilation.ForModel(model);Assert.NotEmpty(derived);foreach(var page in source.Pages)foreach(var plan in CascadeTreeWriteCompilation.ForService(model,source,page)){Assert.True(plan.Read.DerivedName);Assert.True(RedundantSelfTreeSubset.Supports(model,source,page,plan.Read.Source));Assert.Contains(derived,b=>b.Foreign.Source.Id==plan.Read.Source.Source.Id&&b.Cache.Source.Id==plan.Name.Source.Id);Assert.Contains(plan.Name.Source.ColumnName,cache.MixedSourceColumns[source.Source.TableName]);Assert.DoesNotContain(page.Writable,x=>x.Source.Id==plan.Name.Source.Id);Assert.True(MetadataStoragePolicy.ForTable(model.Catalog,source.Source).IsPhysical(plan.Name.Source));Assert.False(MetadataStoragePolicy.ForTable(model.Catalog,plan.Read.Target.Source).IsPhysical(plan.Read.Name.Source));}Assert.Equal(before,JsonSerializer.Serialize(c));
        }
    }
    /// <summary>原完整包实际构建、原Vue挂载、重复更新与手写partial保护，不能仅凭能力清单通过。</summary>
    [Theory][InlineData("string","int64","explicit")][InlineData("int64","string","nested")][InlineData("string","string","mixed")][InlineData("int64","int64","shared")][InlineData("int64","string","globalShared")][InlineData("string","int64","double")]
    public async Task Complete_packages_build_original_vue_and_keep_handwritten_business(string type,string parent,string shape)
    {
        var c=Catalog("r1_self",type,parent,shape);var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-r1-derived-self-full");var dir=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(dir,"app");
        try
        {
            new ApplicationPackager().Create(c,output,"Example.DerivedSelfFull",root);var hand=Path.Combine(output,"backend/generated/Services/R1SelfSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 派生自树重复生成保留手写业务。\n");var original=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(dir,"candidate");new ApplicationPackager().Create(c,candidate,"Example.DerivedSelfFull",root);var updater=new LocalGenerationUpdater();var actions=new[]{"generate-project-config","sync-runtime","generate-all-page"};var plan=updater.Preview(output,candidate,actions);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(original,await File.ReadAllBytesAsync(hand));var repeat=updater.Preview(output,candidate,actions);Assert.Equal("unchanged",updater.Apply(repeat,repeat.PlanSha256).Status);foreach(var command in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,command);Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),File.ReadAllBytes(Path.Combine(output,"frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue")));var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"RedundantPhysicalSelfTreeVueProbe.mjs"));probe.ArgumentList.Add(Path.Combine(output,"frontend"));probe.ArgumentList.Add(shape is "explicit" or "automatic" or "caption" or "nested"?"single":shape);await NonRedundantApplicationFixture.RunAsync(probe);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(dir,boundary);}
    }
    /// <summary>仅改变本轮关系存储／名称路径；共享分支和原控件仍取既有目录源头。</summary>
    internal static MetadataCatalog Catalog(string prefix,string type,string parent,string shape)
    {
        MetadataCatalog c;
        if(shape is "mixed" or "shared" or "globalShared")
        {
            c=RedundantPhysicalSelfTreeTests.Catalog(prefix,type,parent,shape);var target=shape=="mixed"?"6":"10";var name=shape=="mixed"?"62":"60102";var page=shape=="mixed"?"601":"1001";c.ConfigTables.Single(t=>t.Id==target).IsRongyu=0;c.ConfigColumns.Single(x=>x.Id==name).Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("company.name")};c.ConfigTables.Add(new(){Id="90",TableName=prefix+"_company",IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="90101",ConfigTableId="90",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="90102",ConfigTableId="90",ColumnName="name",LogicalType="string"},new(){Id="90103",ConfigTableId="90",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="90104",ConfigTableId="90",ColumnName="delete_no",LogicalType="datetime"},new(){Id="90105",ConfigTableId=target,ColumnName="company_id",LogicalType="int64",LinkConfigTableId="90",ControlKind="List",ControlDetail="{\"tableId\":\"90\",\"nameField\":\"name\"}"}]);c.ConfigPages.Add(new(){Id="9001",ConfigTableId="90",WhereSql="yz_project_id=@current_yz_project_id"});c.ConfigPageColumns.AddRange([new(){Id="900190102",ConfigPageId="9001",ConfigColumnId="90102",IsShow=1,IsEdit=1},new(){Id=page+"90105",ConfigPageId=page,ConfigColumnId="90105",IsShow=1,IsEdit=1}]);
        }
        else
        {
            c=DerivedSelfTreeTests.Catalog(prefix,type,parent,shape=="automatic");c.ConfigTables.Single(t=>t.Id=="2").IsRongyu=1;
            if(shape=="caption"){c.ConfigColumns.Single(x=>x.Id=="32").ColumnName="caption";var source=c.ConfigColumns.Single(x=>x.Id=="25");source.ControlDetail=source.ControlDetail.Replace("\"nameField\":\"name\"","\"nameField\":\"caption\"");}
            if(shape=="nested"){c.ConfigTables.Single(t=>t.Id=="4").IsRongyu=0;c.ConfigColumns.Single(x=>x.Id=="42").Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("brand.label")};c.ConfigTables.Add(new(){Id="5",TableName=prefix+"_brand",IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="51",ConfigTableId="5",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="52",ConfigTableId="5",ColumnName="label",LogicalType="string"},new(){Id="53",ConfigTableId="5",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="54",ConfigTableId="5",ColumnName="delete_no",LogicalType="datetime"},new(){Id="45",ConfigTableId="4",ColumnName="brand_id",LogicalType="int64",LinkConfigTableId="5"}]);c.ConfigPages.Add(new(){Id="501",ConfigTableId="5",WhereSql="yz_project_id=@current_yz_project_id"});c.ConfigPageColumns.Add(new(){Id="50152",ConfigPageId="501",ConfigColumnId="52",IsShow=1,IsEdit=1});c.ConfigPageColumns.Add(new(){Id="40145",ConfigPageId="401",ConfigColumnId="45",IsShow=1,IsEdit=1});}
            if(shape=="double"){var other=JsonSerializer.Deserialize<ColumnMetadata>(JsonSerializer.Serialize(c.ConfigColumns.Single(x=>x.Id=="25")))!;other.Id="80";other.ColumnName="alternate_id";c.ConfigColumns.Add(other);c.ConfigPageColumns.Add(new(){Id="20180",ConfigPageId="201",ConfigColumnId="80",IsShow=1,IsEdit=1});}
        }
        foreach(var record in c.ConfigTables.Cast<MetadataRecord>().Concat(c.ConfigColumns).Concat(c.ConfigPages).Concat(c.ConfigPageColumns))record.YzProjectId=c.YzProjectId;foreach(var p in c.ConfigPages)p.YzAdminTypeId="5";if(!c.YzAdminTypes.Any(x=>x.Id=="5"))c.YzAdminTypes.Add(new(){Id="5",YzProjectId=c.YzProjectId});return c;
    }
}
