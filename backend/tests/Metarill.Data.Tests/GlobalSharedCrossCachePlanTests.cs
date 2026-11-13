using System.Text.Json;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>共享去重保留完整控制列的全局页证明，不要求共同缓存重复登记。</summary>
public sealed class GlobalSharedCrossCachePlanTests
{
    /// <summary>仅保留末级缓存时完整映射可证明全部祖先，原目录和编译指纹不受后续映射修改影响。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public void Deduplicated_global_ancestors_keep_complete_pages(string type)
    {
        var c=GlobalCrossCachePlanTests.Catalog(7,type,true);var pages=new Dictionary<string,string>{{"country","country_page"},{"area","area_page"},{"place","place_page"}};
        var d=GlobalCrossCachePlanTests.Definitions(c)[2] with{CrossTableGlobalPages=pages};var before=JsonSerializer.Serialize(c);var plan=RelationMutationPlan.Compile(c,[d]);var fingerprint=plan.Fingerprint;
        Assert.Contains(RelationMaintenanceCatalog.Compile(c,[d]).Tables,t=>t.Id=="source");var copy=JsonSerializer.Deserialize<RelationCacheDefinition>(JsonSerializer.Serialize(d))!;Assert.Equal(pages,copy.CrossTableGlobalPages);Assert.Equal(fingerprint,RelationMutationPlan.Compile(c,[copy]).Fingerprint);
        pages["country"]="missing";Assert.Equal(fingerprint,plan.Fingerprint);Assert.ThrowsAny<Exception>(()=>RelationMutationPlan.Compile(c,[d]));Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>缺层、额外普通层、空映射、错页与直接页冲突都不能利用共享映射绕过原证明。</summary>
    [Theory][InlineData("missing")][InlineData("extra")][InlineData("empty")][InlineData("blank")][InlineData("wrongTable")][InlineData("unpublished")][InlineData("conflict")][InlineData("ordinary")][InlineData("noControl")]
    public void Incomplete_or_conflicting_map_is_rejected(string kind)
    {
        var c=GlobalCrossCachePlanTests.Catalog(kind=="ordinary"?1:7,"string",true);var pages=new Dictionary<string,string>{{"country","country_page"},{"area","area_page"},{"place","place_page"}};
        if(kind=="missing")pages.Remove("country");if(kind=="extra")pages.Add("unknown","country_page");if(kind=="empty")pages.Clear();if(kind=="blank")pages["country"]="";if(kind=="wrongTable")pages["country"]="area_page";if(kind=="unpublished")pages["country"]="unknown";
        var d=GlobalCrossCachePlanTests.Definitions(c)[2] with{CrossTableGlobalPages=pages};if(kind=="conflict")d=d with{CrossTablePageId="area_page"};if(kind=="noControl")d=d with{CrossTableColumnId=null,CrossTableLevelKey=null,CrossTablePageId=null};Assert.ThrowsAny<Exception>(()=>RelationMutationPlan.Compile(c,[d]));
    }
}
