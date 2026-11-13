using System.Globalization;
using System.Text.Json;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>正式数组维护按实际物理字段变化核对计数，仍严格拒绝范围外、业务列及既有名称语义改变。</summary>
internal static class NativeArrayMaintenanceAudit
{
    internal sealed record Evidence(int BatchCells,int ExistingFormatCells,int TotalCells,int Rows);
    private static readonly HashSet<string> Caches=new(["global_source_global_target_names","selected_global_source_global_target_names"],StringComparer.Ordinal);

    /// <summary>全表前后逐格比较；新增130条须各修复两格，其余活动同项目只容许等价JSON格式变化。</summary>
    internal static Evidence Verify(IReadOnlyList<Dictionary<string,object?>> before,IReadOnlyList<Dictionary<string,object?>> after,IReadOnlySet<long> batchIds,string project,int reported)
    {
        Assert.NotEmpty(batchIds);var prior=before.ToDictionary(row=>SqlRowStore.Id(row));var current=after.ToDictionary(row=>SqlRowStore.Id(row));Assert.Equal(prior.Keys.Order(),current.Keys.Order());Assert.All(batchIds,id=>Assert.True(prior.ContainsKey(id)));
        var batch=0;var formatting=0;
        foreach(var (id,row) in prior)
        {
            var next=current[id];Assert.Equal(row.Keys.Order(StringComparer.Ordinal),next.Keys.Order(StringComparer.Ordinal));
            var owned=Convert.ToString(row.GetValueOrDefault("yz_project_id"),CultureInfo.InvariantCulture)==project&&row.GetValueOrDefault("delete_no") is null;
            if(batchIds.Contains(id)){Assert.True(owned);Assert.All(Caches,key=>Assert.True(row.ContainsKey(key)));}
            foreach(var (key,value) in row)
            {
                var changed=!Equals(value,next[key]);
                if(!Caches.Contains(key)||!owned){Assert.False(changed,"维护改变了非缓存字段或范围外记录："+key);continue;}
                if(batchIds.Contains(id)){Assert.True(changed,"本轮故意旧名没有实际修复："+key);batch++;continue;}
                if(!changed)continue;
                using var left=JsonDocument.Parse(value is null?"null":Assert.IsType<string>(value));using var right=JsonDocument.Parse(next[key] is null?"null":Assert.IsType<string>(next[key]));
                Assert.True(JsonElement.DeepEquals(left.RootElement,right.RootElement),"维护改变了既有非故障记录的名称语义："+key);formatting++;
            }
        }
        Assert.Equal(batchIds.Count*Caches.Count,batch);Assert.Equal(batch+formatting,reported);return new(batch,formatting,batch+formatting,prior.Count);
    }
}
