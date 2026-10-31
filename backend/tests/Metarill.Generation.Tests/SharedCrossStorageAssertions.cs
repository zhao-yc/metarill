using System.Globalization;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>正式与固定身份验收共用物理存储合同，避免重复断言对空值的理解不一致。</summary>
internal static class SharedCrossStorageAssertions
{
    /// <summary>R0未选位必须是null且不存名称；R1沿用null或空字符串，已选位与名称精确匹配。</summary>
    internal static void AssertSlots(IReadOnlyDictionary<string,object?> raw,IReadOnlyDictionary<string,long?> expected,bool cached,int group)
    {
        foreach(var (role,id) in expected)
        {
            var value=raw[role+"_id"];
            if(id is{} selected)Assert.Equal(selected.ToString(CultureInfo.InvariantCulture),Convert.ToString(value,CultureInfo.InvariantCulture));
            else if(cached)Assert.True(value is null or "","冗余未选ID必须为空。");
            else Assert.Null(value);
            var name=role+"_name";
            if(!cached){Assert.False(raw.ContainsKey(name));continue;}
            if(id is null){Assert.True(raw[name] is null or "","冗余未选名称必须为空。");continue;}
            Assert.Equal((role switch{"country"=>"国","area"=>"区","place"=>"城","warehouse"=>"仓",_=>throw new ArgumentException("未知共享层位。")})+group,raw[name]);
        }
    }
}
