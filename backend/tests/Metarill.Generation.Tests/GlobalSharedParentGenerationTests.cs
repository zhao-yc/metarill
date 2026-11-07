using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>全局共享R0的五种来源父范围由同一目录证明，不借用未经验证的独立全局父表。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalSharedParentGenerationTests
{
    /// <summary>全部有效位置与四键、短控件、有无项目列共176目录，逐页校验逻辑与混合物理父字段。</summary>
    [Theory][InlineData("string","string",false)][InlineData("string","int64",false)][InlineData("int64","string",false)][InlineData("int64","int64",false)]
    [InlineData("string","string",true)][InlineData("string","int64",true)][InlineData("int64","string",true)][InlineData("int64","int64",true)]
    public void Global_shared_parent_catalogs_prove_all_five_parent_pages(string type,string parent,bool shorter)
    {
        for(var mask=1;mask<=15;mask++){if(shorter&&(mask&4)!=0)continue;foreach(var noProject in new[]{false,true})
        {
            var c=Catalog("global_parent",type,parent,shorter,mask,noProject);var before=JsonSerializer.Serialize(c);var m=GenerationModel.Create(c,"Example.GlobalSharedParent");var t=m.Tables.Single(table=>table.Source.Id=="2");foreach(var page in t.Pages){var group=SharedCrossTableCompilation.ForPage(m,t,page);Assert.Empty(group.Gaps);Assert.Equal(2,Assert.Single(group.Groups).Plans.Count);if(page.Source.Id=="201")continue;Assert.True(NonRedundantCrossParentCompilation.Enabled(m,t,page));Assert.NotEmpty(NonRedundantSelfTreeSubset.ParentTables(m,t,page));}
            Assert.Empty(GenerationCapabilityAudit.Collect(m));Assert.Equal(before,JsonSerializer.Serialize(c));
        }}
    }
    /// <summary>四种实际完整包保留原主子页、文件控件、partial及原parentId传输与共享清理。</summary>
    [Theory][InlineData("string","string",false,1,false,false)][InlineData("string","int64",true,2,true,true)][InlineData("int64","string",false,8,true,false)][InlineData("int64","int64",false,15,true,true)]
    public async Task Full_global_parent_packages_build_with_original_vue(string type,string parent,bool shorter,int mask,bool noProject,bool physicalDelete)
    {
        var c=Catalog("shared_parent",type,parent,shorter,mask,noProject,true,physicalDelete);var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-shared-parent");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"app");try
        {
            new ApplicationPackager().Create(c,output,"Example.GlobalParentFull",root);var hand=Path.Combine(output,"backend/generated/Services/SharedParentSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 全局共享主子父范围手写业务。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(c,candidate,"Example.GlobalParentFull",root);var updater=new LocalGenerationUpdater();var operations=new[]{"generate-project-config","sync-runtime","generate-all-page"};var plan=updater.Preview(output,candidate,operations);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));var repeated=updater.Preview(output,candidate,operations);Assert.Equal("unchanged",updater.Apply(repeated,repeated.PlanSha256).Status);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);foreach(var name in new[]{"yzp-cascade-tree","yzp-file-library"})Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components/"+name+"/"+name+".vue")),File.ReadAllBytes(Path.Combine(output,"frontend/components/"+name+"/"+name+".vue")));
            foreach(var script in new[]{"SharedCrossParentVueProbe.mjs","SharedCrossFilesVueProbe.mjs"}){var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),script));probe.ArgumentList.Add(Path.Combine(output,"frontend"));if(script=="SharedCrossParentVueProbe.mjs")probe.ArgumentList.Add(shorter.ToString().ToLowerInvariant());await NonRedundantApplicationFixture.RunAsync(probe);}
        }finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>独立全局父、目标上下文、未发布、坏同伴、错页、物理父覆盖与精确来源继续拒绝。</summary>
    [Theory][InlineData("single")][InlineData("globalR0")][InlineData("variable")][InlineData("unpublished")][InlineData("badPeer")][InlineData("physicalPage")][InlineData("physicalOverlay")][InlineData("exact")]
    public void Unproven_global_parent_never_publishes_complete_package(string kind)
    {
        var c=Catalog("global_parent","string","int64",false,1,true);if(kind=="single"){c.ConfigColumns.RemoveAll(column=>column.Id=="80");c.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="80");c.ConfigPages.RemoveAll(page=>page.Id=="205");c.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="205");c.ConfigMenus.RemoveAll(menu=>menu.ConfigPageId=="205");MetadataValidation.Validate(c);_=GenerationModel.Create(c,"Example.ValidSingleGlobalParent");}if(kind=="globalR0")c.ConfigTables.Single(table=>table.Id=="3").IsRongyu=0;if(kind=="variable"){c.ConfigColumns.Add(new(){Id="399",ConfigTableId="3",YzProjectId=c.YzProjectId,ColumnName="yz_project_id",LogicalType="int64"});c.ConfigPages.Single(page=>page.Id=="301").WhereSql="yz_project_id=@current_yz_project_id";}if(kind=="unpublished")c.ConfigTables.Single(table=>table.Id=="3").YzProjectId="";if(kind=="badPeer")c.ConfigColumns.Single(column=>column.Id=="80").ControlDetail="{}";if(kind=="physicalPage")c.ConfigColumns.Single(column=>column.Id=="91").Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("401");if(kind=="physicalOverlay")c.ConfigPageColumns.Single(field=>field.ConfigPageId=="206"&&field.ConfigColumnId=="91").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{}");if(kind=="exact")c.ConfigPages.Single(page=>page.Id=="202").Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("name");var output=Path.Combine(Path.GetTempPath(),"metarill-global-parent-reject",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.Reject",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>全部页面与字段都来源于原元数据工厂，只增加已发布全局声明。</summary>
    internal static MetadataCatalog Catalog(string prefix,string type,string parent,bool shorter,int mask,bool noProject,bool files=false,bool physicalDelete=false)
    {
        var c=files?SharedCrossFilesGenerationTests.Catalog(prefix,type,parent,shorter,false,physicalDelete):SharedCrossParentGenerationTests.Catalog(prefix,type,parent,shorter);GlobalSharedCrossGenerationTests.ApplyGlobals(c,mask,noProject);return c;
    }
}
