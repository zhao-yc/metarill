using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>计数核验不能靠放大预期过关，保留全部业务字段／项目范围／墓碑和既有名称语义。</summary>
public sealed class NativeArrayMaintenanceAuditTests
{
    /// <summary>130条修复260格，另两条只规范化JSON时计264，否则计260；均按实际格数验证。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Counts_actual_cells_and_allows_only_equivalent_existing_json(bool canonicalize)
    {
        var (before,after,ids)=Rows(canonicalize);var result=NativeArrayMaintenanceAudit.Verify(before,after,ids,"100",canonicalize?264:260);Assert.Equal(260,result.BatchCells);Assert.Equal(canonicalize?4:0,result.ExistingFormatCells);
    }
    /// <summary>任何多报／少报、业务列改变、范围外／墓碑缓存改变、既有名称真改变及漏修都明确失败。</summary>
    [Theory][InlineData("count")][InlineData("business")][InlineData("foreign")][InlineData("deleted")][InlineData("semantic")][InlineData("missingCell")][InlineData("missingRow")][InlineData("newRow")]
    public void Rejects_false_counts_or_unexpected_changes(string invalid)
    {
        var (before,after,ids)=Rows(true);var count=264;
        if(invalid=="count")count=260;if(invalid=="business")after[0]["name"]="业务列被改";if(invalid=="foreign")after[^2]["global_source_global_target_names"]="[]";if(invalid=="deleted")after[^1]["global_source_global_target_names"]="[]";if(invalid=="semantic")after[130]["global_source_global_target_names"]="[\"错误新名\"]";if(invalid=="missingCell")after[0]["global_source_global_target_names"]=before[0]["global_source_global_target_names"];if(invalid=="missingRow")after.RemoveAt(0);if(invalid=="newRow")after.Add(new(after[0]){["id"]=999L});
        Assert.ThrowsAny<Exception>(()=>NativeArrayMaintenanceAudit.Verify(before,after,ids,"100",count));
    }
    /// <summary>合成固定130条加两条既有记录、另一项目及墓碑；纯内存观察测试，不连接数据库。</summary>
    private static (List<Dictionary<string,object?>> Before,List<Dictionary<string,object?>> After,HashSet<long> Ids) Rows(bool canonicalize)
    {
        var before=Enumerable.Range(1,134).Select(i=>new Dictionary<string,object?>{{"id",(long)i},{"yz_project_id",i==133?101L:100L},{"delete_no",i==134?new DateTime(2026,1,1):null},{"name","原业务列"},{"global_source_global_target_names",i<=130?"[\"旧名\"]":"[\"\\u4E2D\"]"},{"selected_global_source_global_target_names",i<=130?"[\"旧名\"]":"[\"\\u4E2D\"]"}}).ToList();var after=before.Select(row=>new Dictionary<string,object?>(row)).ToList();
        for(var i=0;i<130;i++)foreach(var key in new[]{"global_source_global_target_names","selected_global_source_global_target_names"})after[i][key]="[\"中\"]";
        if(canonicalize)for(var i=130;i<132;i++)foreach(var key in new[]{"global_source_global_target_names","selected_global_source_global_target_names"})after[i][key]="[\"中\"]";
        return(before,after,Enumerable.Range(1,130).Select(id=>(long)id).ToHashSet());
    }
}
