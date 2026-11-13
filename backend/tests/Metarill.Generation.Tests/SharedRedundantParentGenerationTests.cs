using System.Text.Json;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>R1父键是实际存储；全局目标声明及共享同伴证明不能改变其页面归属语义。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class SharedRedundantParentGenerationTests
{
    /// <summary>四键、长短分支及所有有效普通／全局组合共192目录，逐个父页证明实际字段。</summary>
    [Theory][InlineData("string","string",false)][InlineData("string","int64",false)][InlineData("int64","string",false)][InlineData("int64","int64",false)]
    [InlineData("string","string",true)][InlineData("string","int64",true)][InlineData("int64","string",true)][InlineData("int64","int64",true)]
    public void All_parent_pages_preserve_physical_r1_identity(string type,string parent,bool shorter)
    {
        for(var mask=0;mask<16;mask++){if(shorter&&(mask&4)!=0)continue;foreach(var noProject in new[]{false,true})
        {
            var catalog=Catalog("r1_parent",type,parent,shorter,mask,noProject);var original=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.SharedR1Parent");var table=model.Tables.Single(table=>table.Source.Id=="2");foreach(var page in table.Pages.Where(page=>page.Source.Id!="201")){SharedRedundantParentCompilation.Require(model,table,page);Assert.Null(RedundantCrossTableSubset.Reason(model,table,page));foreach(var scope in page.Scopes.Where(scope=>scope.Source=="parent"))Assert.True(Metarill.Core.Metadata.MetadataStoragePolicy.ForTable(catalog,table.Source).IsPhysical(scope.Column.Source));}Assert.Empty(GenerationCapabilityAudit.Collect(model));Assert.Equal(original,JsonSerializer.Serialize(catalog));
        }}
    }
    /// <summary>实际完整包保留原Vue与手写partial，普通和全局父页均由同一生成器产生。</summary>
    [Theory][InlineData("string","string",false,0,false)][InlineData("string","int64",true,2,true)][InlineData("int64","string",false,1,true)][InlineData("int64","int64",false,15,true)]
    public async Task Full_original_packages_build_and_repeat_without_overwriting_partial(string type,string parent,bool shorter,int mask,bool noProject)
    {
        var catalog=Catalog("shared_parent",type,parent,shorter,mask,noProject);var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-shared-r1-parent");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");try
        {
            new ApplicationPackager().Create(catalog,output,"Example.SharedR1Parent",root);var hand=Path.Combine(output,"backend/generated/Services/SharedParentSourceService.cs");await File.AppendAllTextAsync(hand,"\n// R1父范围手写业务。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(catalog,candidate,"Example.SharedR1Parent",root);var updater=new LocalGenerationUpdater();var operations=new[]{"generate-project-config","sync-runtime","generate-all-page"};var plan=updater.Preview(output,candidate,operations);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));Assert.Equal("unchanged",updater.Apply(updater.Preview(output,candidate,operations),updater.Preview(output,candidate,operations).PlanSha256).Status);foreach(var command in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,command);
            Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),File.ReadAllBytes(Path.Combine(output,"frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue")));var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"SharedCrossParentVueProbe.mjs"));probe.ArgumentList.Add(Path.Combine(output,"frontend"));probe.ArgumentList.Add(shorter.ToString().ToLowerInvariant());await NonRedundantApplicationFixture.RunAsync(probe);
        }finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>缺同伴、全局R0目标、变化目标上下文、未发布来源、独立控件、物理父错页／覆盖及精确页不能发布。</summary>
    [Theory][InlineData("badPeer")][InlineData("globalR0")][InlineData("variable")][InlineData("unpublished")][InlineData("single")][InlineData("physicalPage")][InlineData("physicalOverlay")][InlineData("exact")][InlineData("cacheParent")][InlineData("selfParent")]
    public void Unproven_r1_parent_never_publishes_full_package(string kind)
    {
        var catalog=Catalog("r1_parent","string","int64",false,kind is "cacheParent" or "selfParent"?0:1,true);if(kind=="cacheParent"){catalog.ConfigPages.Single(page=>page.Id=="202").WhereSql=catalog.ConfigPages.Single(page=>page.Id=="202").WhereSql.Replace("country_id=@parent_id","country_name=@parent_id",StringComparison.Ordinal);}if(kind=="selfParent"){var field=catalog.ConfigColumns.Single(column=>column.Id=="91");field.LinkConfigTableId="2";field.ControlDetail="{\"tableId\":\"2\",\"nameField\":\"name\"}";field.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("201");}if(kind=="badPeer")catalog.ConfigColumns.Single(column=>column.Id=="80").ControlDetail="{}";if(kind=="globalR0")catalog.ConfigTables.Single(table=>table.Id=="3").IsRongyu=0;if(kind=="variable"){catalog.ConfigColumns.Add(new(){Id="399",YzProjectId=catalog.YzProjectId,ConfigTableId="3",ColumnName="yz_project_id",LogicalType="int64"});catalog.ConfigPages.Single(page=>page.Id=="301").WhereSql="yz_project_id=@current_yz_project_id";}if(kind=="unpublished")catalog.ConfigTables.Single(table=>table.Id=="2").YzProjectId="";if(kind=="single"){catalog.ConfigColumns.RemoveAll(column=>column.Id=="80");catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="80"||field.ConfigPageId=="205");catalog.ConfigPages.RemoveAll(page=>page.Id=="205");catalog.ConfigMenus.RemoveAll(menu=>menu.ConfigPageId=="205");Metarill.Core.Metadata.MetadataValidation.Validate(catalog);}if(kind=="physicalPage")catalog.ConfigColumns.Single(column=>column.Id=="91").Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("401");if(kind=="physicalOverlay")catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="206"&&field.ConfigColumnId=="91").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{}");if(kind=="exact")catalog.ConfigPages.Single(page=>page.Id=="202").Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("name");var output=Path.Combine(Path.GetTempPath(),"metarill-shared-r1-parent-reject",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(catalog,output,"Example.RejectedR1Parent",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>带显式关联的缓存名称也不是父身份，局部证明必须独立拒绝，不能等后续缓存传播门槛拦截。</summary>
    [Fact]public void Managed_name_with_explicit_link_is_rejected_by_parent_proof()
    {
        var catalog=Catalog("r1_name_parent","string","int64",false,0,false);var name=catalog.ConfigColumns.SingleOrDefault(column=>column.ConfigTableId=="2"&&column.ColumnName=="country_name");if(name is null){name=new(){Id="1099",YzProjectId=catalog.YzProjectId,ConfigTableId="2",ColumnName="country_name",LogicalType="string"};catalog.ConfigColumns.Add(name);}name.LinkConfigTableId="3";var source=catalog.ConfigPages.Single(page=>page.Id=="202");source.WhereSql=source.WhereSql.Replace("country_id=@parent_id","country_name=@parent_id",StringComparison.Ordinal);var model=GenerationModel.Create(catalog,"Example.R1NameParent");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=table.Pages.Single(page=>page.Source.Id=="202");Assert.True(table.Columns.Single(column=>column.Source.Id==name.Id).IsManagedCascadeName);Assert.Throws<NotSupportedException>(()=>SharedRedundantParentCompilation.Require(model,table,page));
    }
    /// <summary>文件完整用途必须同时通过生成能力与公共运行时固定元数据证明。</summary>
    [Fact]public void File_parent_full_use_requires_frozen_common_plan()
    {
        var catalog=Catalog("shared_parent","string","int64",false,1,true,true);var model=GenerationModel.Create(catalog,"Example.SharedR1ParentFiles");Assert.Empty(GenerationCapabilityAudit.Collect(model));Assert.Single(new Metarill.Runtime.Files.FileMetadataContext([catalog]).Catalogs);
    }
    /// <summary>同一原目录只改变来源存储声明，来源页、字段、菜单及目标链全部沿用原工厂。</summary>
    internal static Metarill.Core.Metadata.MetadataCatalog Catalog(string prefix,string type,string parent,bool shorter,int mask,bool noProject,bool files=false)
    {
        var catalog=GlobalSharedParentGenerationTests.Catalog(prefix,type,parent,shorter,mask,noProject,files);catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=1;return catalog;
    }
}
