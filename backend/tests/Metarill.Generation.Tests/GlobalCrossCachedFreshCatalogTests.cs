using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>全局根／中／末和全链的普通R1缓存按同一原级联协议构建完整应用。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalCrossCachedFreshCatalogTests
{
    /// <summary>全局页绑定、三层缓存及原组件必须随Full编译，不能只让低层计划通过。</summary>
    [Theory][InlineData("cross3")][InlineData("cross5")][InlineData("cross6")][InlineData("cross7")]
    public async Task Full_global_cross_cache_has_frozen_pages_and_original_control(string profile)
    {
        var c=Catalog(profile);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.GlobalCrossCached");var bindings=RelationPropagationCompilation.Bindings(model);Assert.Equal(3,bindings.Count);Assert.All(bindings,x=>Assert.Equal(x.Target.IsGlobal?x.Target.Pages.Single().Source.Id:null,x.CrossTablePageId));Assert.Empty(GenerationCapabilityAudit.Collect(model));var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-cross-cached");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{Assert.True(new ApplicationPackager().Create(c,directory,model.Namespace,NonRedundantApplicationFixture.Root()).FrontendIncluded);foreach(var op in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,op);Assert.Equal(File.ReadAllBytes(Path.Combine(NonRedundantApplicationFixture.Root(),"frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),File.ReadAllBytes(Path.Combine(directory,"frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue")));Assert.Equal(before,JsonSerializer.Serialize(c));}finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>R1缓存不能绕过全局发布、页面归属和固定范围的完整包门槛。</summary>
    [Theory][InlineData("variable")][InlineData("unpublished")][InlineData("foreignPage")][InlineData("globalR0")]
    public void Invalid_global_cache_rejects_complete_package(string kind)
    {
        var c=Catalog("cross3");var target=c.ConfigTables.Single(x=>x.Id=="3");var page=c.ConfigPages.Single(x=>x.Id=="301");if(kind=="variable")page.WhereSql="yz_project_id=@current_yz_project_id";if(kind=="unpublished")target.YzProjectId="";if(kind=="foreignPage")page.YzProjectId="999";if(kind=="globalR0")target.IsRongyu=0;
        var output=Path.Combine(Path.GetTempPath(),"metarill-global-cross-cache-reject",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.Reject",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>普通来源模式与四种ID／父键保留，目标全局范围由既定模型分别交叉。</summary>
    internal static MetadataCatalog Catalog(string profile){var c=GlobalCrossFreshCatalogTests.Catalog(profile);c.ConfigTables.Single(x=>x.Id=="2").IsRongyu=1;c.YzProjectName="全局跨表缓存验收";c.ConfigColumns.RemoveAll(x=>x.ConfigTableId=="5"&&x.ColumnName=="delete_no");return c;}
}
