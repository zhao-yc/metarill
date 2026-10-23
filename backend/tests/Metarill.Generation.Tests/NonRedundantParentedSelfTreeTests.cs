using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>非冗余来源的父范围、双自树、普通关系与多选共用原生成链，不从包门禁猜测后端已完成。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class NonRedundantParentedSelfTreeTests
{
    /// <summary>两种来源父／树键及显隐名称均真实编译，名称不落物理列，原主子关系保留。</summary>
    [Theory][InlineData("string",false)][InlineData("string",true)][InlineData("int64",false)][InlineData("int64",true)]
    public async Task Parented_mixed_source_compiles_without_name_storage_or_capability_gaps(string type,bool automatic)
    {
        var metadata=Catalog("nr_parent_mix",type,automatic);var saved=JsonSerializer.Serialize(metadata);var model=GenerationModel.Create(metadata,"Example.ParentedMixedTree");Assert.Empty(GenerationCapabilityAudit.Collect(model));var table=model.Tables.Single(table=>table.Source.Id=="2");foreach(var page in table.Pages){Assert.Equal(2,CascadeTreeViewRenderer.ForPage(model,table,page).Count);Assert.All(CascadeTreeReadCompilation.ForPage(model,table,page),plan=>Assert.True(NonRedundantSelfTreeSubset.Supports(model,table,page,plan.Source)));}Assert.DoesNotContain(MetadataStoragePolicy.ForTable(metadata,table.Source).PhysicalColumns,column=>column.ColumnName is "tree_name" or "secondary_tree_name" or "owner_names");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nr-parent-compile");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(metadata,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType(model.Namespace+".Generated.Controllers.NrParentMixSourcePage201Controller"));Assert.NotNull(assembly.GetType(model.Namespace+".Generated.VOs.NrParentMixSourcePage201Vo")!.GetProperty("SecondaryTreeIdDisplayText"));Assert.Equal(saved,JsonSerializer.Serialize(metadata));}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>完整原宿主含父页主子入口，字符串／整数外键都传原字符串ID，更新保护手写partial。</summary>
    [Theory][InlineData("string",true)][InlineData("int64",false)]
    public async Task Full_package_keeps_original_child_entry_controls_and_handwritten_extensions(string type,bool automatic)
    {
        var metadata=Catalog("nr_parent_mix",type,automatic);var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-nr-parent-package");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            var result=new ApplicationPackager().Create(metadata,output,"Example.ParentedTreeApplication",root);Assert.True(result.FrontendIncluded);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);var parent=await File.ReadAllTextAsync(Path.Combine(output,"frontend/pages/yzp/nr_parent_mix_owner-detail-401.vue"));Assert.Contains("ChildListparented",parent);Assert.Contains(":parent_id=",parent);foreach(var component in new[]{"components/yzp-cascade-tree/yzp-cascade-tree.vue","common/yzp_runtime/multiSelectFieldMixin.js"})Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin",component)),File.ReadAllBytes(Path.Combine(output,"frontend",component)));using var profile=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")));var trees=profile.RootElement.GetProperty("supportedControlSubsets").EnumerateArray().Where(item=>item.GetProperty("controlKind").GetString()=="CascadeTree").ToArray();Assert.Equal(6,trees.Length);Assert.All(trees,item=>Assert.True(item.GetProperty("sourceParentSupported").GetBoolean()));Assert.All(trees,item=>Assert.True(item.GetProperty("mixedRelationsSupported").GetBoolean()));Assert.All(trees,item=>Assert.True(item.GetProperty("multipleSelfTreesSupported").GetBoolean()));var hand=Path.Combine(output,"backend/generated/Services/NrParentMixSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 父范围与双自树混合仍保护手写业务。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(metadata,candidate,"Example.ParentedTreeApplication",root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>未知父、派生项目、缺发布归属及精确权限不能借混合组合发布，失败无半包。</summary>
    [Theory][InlineData("unknownParent")][InlineData("parentProjectCache")][InlineData("parentRealm")][InlineData("exact")]
    public void Invalid_parent_or_unproven_scope_remains_gated(string kind)
    {
        var metadata=Catalog("nr_parent_mix","int64",false);if(kind=="unknownParent")metadata.ConfigColumns.Single(column=>column.Id=="28").LinkConfigTableId="missing";if(kind=="parentProjectCache")metadata.ConfigColumns.Single(column=>column.Id=="43").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);if(kind=="parentRealm")metadata.ConfigTables.Single(table=>table.Id=="4").YzProjectId="";if(kind=="exact")metadata.ConfigPages.Single(page=>page.Id=="201").Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("name");var boundary=Path.Combine(Path.GetTempPath(),"metarill-nr-parent-reject");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(metadata,output,"Example.RejectedParentedTree",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>固定元数据：父页通过原主子配置进入来源页，两个自树共享独立目标，普通List和多选复用父目标。</summary>
    internal static MetadataCatalog Catalog(string prefix,string type,bool automatic)
    {
        var model=CascadeTreeSourceParentTests.Catalog(prefix,type);model.ConfigTables.Single(table=>table.Id=="2").IsRongyu=0;var parent=model.ConfigColumns.Single(column=>column.Id=="28");parent.ControlKind="List";parent.ControlDetail="{\"tableId\":\"4\",\"pageId\":\"401\",\"nameField\":\"name\"}";
        foreach(var (id,selection) in new[]{("401",false),("402",true)}){model.ConfigPages.Add(new(){Id=id,ConfigTableId="4",Name=selection?"父候选":"父记录",WhereSql="yz_project_id=@current_yz_project_id",Extra=selection?new(){{"type",JsonSerializer.SerializeToElement(2)}}:new()});foreach(var field in new[]{"41","42"})model.ConfigPageColumns.Add(new(){Id=id+field,ConfigPageId=id,ConfigColumnId=field});}
        var tree=model.ConfigColumns.Single(column=>column.Id=="25");model.ConfigColumns.Add(new(){Id="80",ConfigTableId="2",ColumnName="secondary_tree_id",Name="另一棵树",LogicalType=type,LinkConfigTableId="3",ControlKind="CascadeTree",ControlDetail=tree.ControlDetail,SortNo=5});model.ConfigColumns.Add(new(){Id="81",ConfigTableId="2",ColumnName="secondary_tree_name",LogicalType="string",Extra=new(){{"isJoinColumn",JsonSerializer.SerializeToElement(1)}}});model.ConfigColumns.Add(new(){Id="83",ConfigTableId="2",ColumnName="related_owner_id",Name="独立关系",LogicalType=type,ControlKind="List",LinkConfigTableId="4",ControlDetail=parent.ControlDetail,SortNo=7});model.ConfigColumns.Add(new(){Id="82",ConfigTableId="2",ColumnName="owner_ids",Name="多个父记录",LogicalType="string",ColumnLength="max",ControlKind="MultiSelectPage",LinkConfigTableId="4",ControlDetail="{\"pageId\":\"402\",\"nameField\":\"name\"}",SortNo=6});
        foreach(var page in model.ConfigPages.Where(page=>page.ConfigTableId=="2"))foreach(var id in new[]{"80","81","82","83"})model.ConfigPageColumns.Add(new(){Id=page.Id+"_"+id,ConfigPageId=page.Id,ConfigColumnId=id,Extra=id is "80" or "82"?new(){{"isSearch",JsonSerializer.SerializeToElement(1)}}:new()});
        // 原201是type=2选择页，保留其已选回读合同；另用原普通子列表203承接父页入口。
        var child=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(model.ConfigPages.Single(page=>page.Id=="201")))!;child.Id="203";child.Extra["type"]=JsonSerializer.SerializeToElement(0);model.ConfigPages.Add(child);foreach(var old in model.ConfigPageColumns.Where(field=>field.ConfigPageId=="201").ToArray()){var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(old))!;copy.Id="203_"+old.Id;copy.ConfigPageId="203";model.ConfigPageColumns.Add(copy);}
        if(automatic){model.ConfigColumns.RemoveAll(column=>column.Id is "26" or "81");model.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId is "26" or "81");}RedundantMultiNameCacheTests.Published(model);model.ConfigPages.Single(page=>page.Id=="401").Extra["childPlace"]=JsonSerializer.SerializeToElement("Tab");model.ConfigChildren.Add(JsonSerializer.SerializeToElement(new{id="parented",parentConfigPageId="401",childConfigPageId="203",childPageOpenType=2,yzProjectId=model.YzProjectId}));model.ConfigMenus.Add(new(){Id="9001",YzProjectId=model.YzProjectId,YzAdminTypeId="5",Name="父记录",Kind="GENERATED_PAGE",ConfigPageId="401"});return model;
    }
}
