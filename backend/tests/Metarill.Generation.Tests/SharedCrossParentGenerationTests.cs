using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>共享R0来源的原主子页面先证明完整逻辑父范围，再生成实际服务和源码包。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class SharedCrossParentGenerationTests
{
    /// <summary>四键、短控件和派生目标都实际编译根／中／独立叶子及混合物理父范围。</summary>
    [Theory][InlineData("string","string",false,false)][InlineData("string","int64",false,false)][InlineData("int64","string",false,false)][InlineData("int64","int64",false,false)][InlineData("string","int64",true,false)][InlineData("int64","string",true,false)][InlineData("string","int64",false,true)]
    public async Task Shared_logical_parent_pages_compile_without_repacking_omitted_groups(string type,string parent,bool shorter,bool derived)
    {
        var c=Catalog("shared_parent",type,parent,shorter,derived);var before=JsonSerializer.Serialize(c);var m=GenerationModel.Create(c,"Example.SharedParent");var t=m.Tables.Single(t=>t.Source.Id=="2");foreach(var page in t.Pages){Assert.Single(SharedCrossTableCompilation.ForPage(m,t,page).Groups);Assert.Equal(2,CrossTableWriteCompilation.ForService(m,t,page).Count);if(page.Source.Id!="201")Assert.True(NonRedundantCrossParentCompilation.Enabled(m,t,page));}Assert.Empty(GenerationCapabilityAudit.Collect(m));var boundary=Path.Combine(Path.GetTempPath(),"metarill-shared-parent-compile");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),m.Namespace);await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var service=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/SharedParentSourceService.g.cs"));foreach(var page in new[]{"202","203","204","205","206"}){Assert.Contains("LogicalParentScope"+page,service);Assert.Contains("VerifyLogicalParent"+page+"Async",service);Assert.Contains("PackSharedCross"+page+"_25",service);Assert.Contains("VerifySharedCross"+page+"_25Async",service);}Assert.Equal(before,JsonSerializer.Serialize(c));}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>三种原Full含全部父页面，实际更新、手写保护和原Vue的parentId传输分别验证。</summary>
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)]
    public async Task Full_shared_parent_packages_keep_original_vue_and_partial(bool shorter,bool derived)
    {
        var c=Catalog("shared_parent","string","int64",shorter,derived);var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-shared-parent-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"app");
        try{new ApplicationPackager().Create(c,output,"Example.SharedParentFull",root);var hand=Path.Combine(output,"backend/generated/Services/SharedParentSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 共享逻辑父范围手写扩展。\n");var bytes=await File.ReadAllBytesAsync(hand);c.ConfigColumns.Single(x=>x.Id=="22").Name="主子记录名";var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(c,candidate,"Example.SharedParentFull",root);var updater=new LocalGenerationUpdater();var actions=new[]{"generate-project-config","sync-runtime","generate-all-page"};var plan=updater.Preview(output,candidate,actions);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));var repeated=updater.Preview(output,candidate,actions);Assert.Equal("unchanged",updater.Apply(repeated,repeated.PlanSha256).Status);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),File.ReadAllBytes(Path.Combine(output,"frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue")));var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"SharedCrossParentVueProbe.mjs"));probe.ArgumentList.Add(Path.Combine(output,"frontend"));probe.ArgumentList.Add(shorter.ToString().ToLowerInvariant());await NonRedundantApplicationFixture.RunAsync(probe);}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>名称／来源主键不是父外键，虚拟项目身份、未知父表与精确权限仍在创建stage前拒绝。</summary>
    [Theory][InlineData("name")][InlineData("id")][InlineData("project")][InlineData("missing")][InlineData("exact")]
    public void Unproven_parent_identity_and_permissions_stay_closed(string kind)
    {
        var c=Catalog("shared_parent","string","int64");var page=c.ConfigPages.Single(p=>p.Id=="202");if(kind=="name")page.WhereSql="yz_project_id=@current_yz_project_id AND country_name=@parent_id";if(kind=="id")page.WhereSql="yz_project_id=@current_yz_project_id AND id=@parent_id";if(kind=="project")c.ConfigColumns.Single(x=>x.Id=="33").IsVirtualColumn=1;if(kind=="missing")c.ConfigColumns.Single(x=>x.Id=="91").LinkConfigTableId="999999";if(kind=="exact")page.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("name");var boundary=Path.Combine(Path.GetTempPath(),"metarill-bad-shared-parent");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.BadSharedParent",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>原页面元数据只增加父条件，普通物理外键可与共享逻辑祖先共同约束。</summary>
    internal static MetadataCatalog Catalog(string prefix,string type,string parent,bool shorter=false,bool derived=false)
    {
        var c=SharedCrossTableSparseGenerationTests.Catalog(type,parent,shorter,derived,prefix);c.ConfigColumns.Add(new(){Id="91",ConfigTableId="2",ColumnName="owner_country_id",LogicalType=type,LinkConfigTableId="3",ControlKind="List",ControlDetail="{\"tableId\":\"3\",\"nameField\":\"name\"}",Extra=new(){["linkConfigPageId"]=JsonSerializer.SerializeToElement("301")}});c.ConfigPageColumns.Add(new(){Id="20191",ConfigPageId="201",ConfigColumnId="91"});var original=c.ConfigPages.Single(p=>p.Id=="201");original.IsNew=1;original.IsSave=1;original.IsCopy=1;original.IsDelete=1;
        foreach(var (id,condition) in new[]{("202","country_id=@parent_id"),("203","area_id=@parent_id"),("204",(shorter?"area":"place")+"_id=@parent_id"),("205","warehouse_id=@parent_id"),("206","country_id=@parent_id AND owner_country_id=@parent_id")})
        {
            var page=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(original))!;page.Id=id;page.WhereSql+=" AND "+condition;c.ConfigPages.Add(page);foreach(var field in c.ConfigPageColumns.Where(f=>f.ConfigPageId=="201").ToArray()){var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;copy.Id=id+"_"+field.Id;copy.ConfigPageId=id;c.ConfigPageColumns.Add(copy);}c.ConfigMenus.Add(new(){Id="700"+id,YzProjectId=c.YzProjectId,YzAdminTypeId="5",Name="共享主子页"+id,Kind="GENERATED_PAGE",ConfigPageId=id});
        }
        foreach(var row in c.ConfigColumns.Cast<MetadataRecord>().Concat(c.ConfigPageColumns))row.YzProjectId=c.YzProjectId;return c;
    }
}
