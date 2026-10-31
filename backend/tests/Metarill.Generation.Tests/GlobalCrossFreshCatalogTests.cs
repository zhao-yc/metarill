using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>全局目标与项目目标在相同原控件中发布，完整包继续验证原前端与全部用途。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalCrossFreshCatalogTests
{
    /// <summary>每层单独全局及整链全局，四种键均生成实际完整包；输入目录保持不变。</summary>
    [Theory][InlineData("cross3")][InlineData("cross5")][InlineData("cross6")][InlineData("cross7")]
    public async Task Global_targets_package_with_original_pages(string profile)
    {
        var c=Catalog(profile);var before=JsonSerializer.Serialize(c);var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-cross-catalog");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{var model=GenerationModel.Create(c,"Example.GlobalCrossFresh");Assert.Empty(GenerationCapabilityAudit.Collect(model));Assert.True(new ApplicationPackager().Create(c,output,model.Namespace,NonRedundantApplicationFixture.Root()).FrontendIncluded);var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"GlobalCrossVueProbe.mjs"));probe.ArgumentList.Add(Path.Combine(output,"frontend"));probe.ArgumentList.Add("independent");probe.ArgumentList.Add("false");await NonRedundantApplicationFixture.RunAsync(probe);Assert.Equal(before,JsonSerializer.Serialize(c));foreach(var path in new[]{"components/yzp-cascade-tree/yzp-cascade-tree.vue","common/yzp_runtime/detailLayoutRuntime.js"})Assert.Equal(File.ReadAllBytes(Path.Combine(NonRedundantApplicationFixture.Root(),"frontend/metarill_uni_admin",path)),File.ReadAllBytes(Path.Combine(output,"frontend",path)));}finally{if(Directory.Exists(output))await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>全局条件不能带请求项目，来源和其余目标的项目范围仍保留。</summary>
    [Theory][InlineData("variable")][InlineData("missingScope")][InlineData("globalR0")][InlineData("sourceGlobal")][InlineData("unpublished")][InlineData("foreignPage")][InlineData("view")][InlineData("derivedName")]
    public async Task Unproven_global_package_rejects_atomically(string kind)
    {
        var c=Catalog("cross3");var target=c.ConfigTables.Single(x=>x.Id=="3");var page=c.ConfigPages.Single(x=>x.Id=="301");if(kind=="variable")page.WhereSql="yz_project_id=@current_yz_project_id AND status=1";if(kind=="missingScope"){target.Extra.Remove("scopeKind");c.ConfigColumns.RemoveAll(x=>x.ConfigTableId=="3"&&x.ColumnName=="yz_project_id");}if(kind=="globalR0")target.IsRongyu=0;if(kind=="sourceGlobal")c.ConfigTables.Single(x=>x.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind=="unpublished")target.YzProjectId="";if(kind=="foreignPage")page.YzProjectId="999";if(kind=="view")target.IsView=1;if(kind=="derivedName")c.ConfigColumns.Single(x=>x.Id=="32").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-cross-reject");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.GlobalCrossReject",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));}finally{if(Directory.Exists(output))await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>四个正式模型分别改变根／中／末及全部目标的范围，项目列存在与缺省均有明确声明。</summary>
    internal static (string Type,string ParentType,int Mask,bool NoProject) Definition(string profile)=>profile switch
    {
        "cross3"=>("string","string",1,false),"cross5"=>("string","int64",2,true),"cross6"=>("int64","string",4,false),"cross7"=>("int64","int64",7,true),_=>throw new ArgumentOutOfRangeException(nameof(profile))
    };
    /// <summary>只增加本轮验收文件和菜单；表、字段、页面都来源于真实元数据生成。</summary>
    internal static MetadataCatalog Catalog(string profile)
    {
        var d=Definition(profile);var c=RedundantMultiNameCacheTests.Published(NonRedundantCrossTableGenerationTests.Catalog("nr_global",d.Type,d.ParentType));for(var role=0;role<3;role++)if((d.Mask&(1<<role))!=0){var id=(role+3).ToString(CultureInfo.InvariantCulture);c.ConfigTables.Single(x=>x.Id==id).Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");c.ConfigPages.Single(x=>x.ConfigTableId==id).WhereSql="status=1";if(d.NoProject)c.ConfigColumns.RemoveAll(x=>x.ConfigTableId==id&&x.ColumnName=="yz_project_id");}
        c.ConfigColumns.Add(new(){Id="9001",YzProjectId=c.YzProjectId,ConfigTableId="2",ColumnName="attachments",Name="附件",LogicalType="string",ColumnLength="max",ControlKind="FileLibrary",ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}"});c.ConfigPageColumns.Add(new(){Id="2019001",YzProjectId=c.YzProjectId,ConfigPageId="201",ConfigColumnId="9001",IsShow=1,IsEdit=1,SortNo=10000});c.ConfigMenus.Clear();foreach(var page in c.ConfigPages){page.IsSave=1;c.ConfigMenus.Add(new(){Id=(700000+int.Parse(page.Id,CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture),YzProjectId=c.YzProjectId,YzAdminTypeId="5",Name="全局跨表验收"+page.Id,Kind="GENERATED_PAGE",ConfigPageId=page.Id});}return c;
    }
}
