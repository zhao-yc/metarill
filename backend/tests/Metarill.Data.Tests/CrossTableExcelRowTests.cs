using System.Text.Json;
using Metarill.Core.Cascades;
using Metarill.Core.Contracts;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>整行聚合只测试ID冲突合同，事务/生成写入另由实际Service验证。</summary>
public sealed class CrossTableExcelRowTests
{
    /// <summary>构造独立ID输入，保留数值与文本的原始JSON形状。</summary>
    private static Dictionary<string,JsonElement> Values(string json)=>JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(json)!;
    /// <summary>相同共享节点与显式大ID不受单元格排列顺序影响。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Shared_paths_and_explicit_IDs_are_order_independent(bool reverse)
    {
        var row=new CrossTableExcelRow();var paths=new[]{("城市","{\"countryId\":\"9007199254740993\",\"areaId\":\"22\",\"cityId\":\"3\"}"),("仓库","{\"countryId\":\"9007199254740993\",\"areaId\":\"22\",\"warehouseId\":\"4\"}")};
        foreach(var item in reverse?paths.Reverse():paths)row.Add(item.Item1,Values(item.Item2),false);
        var result=row.Merge(Values("{\"countryId\":9007199254740993,\"name\":\"保留\"}"));
        Assert.Equal("9007199254740993",result["countryId"].GetString());Assert.Equal("22",result["areaId"].GetString());Assert.Equal("保留",result["name"].GetString());
    }
    /// <summary>共享路径或显式祖先冲突时拒绝，并保护调用者原始字典。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Shared_conflict_or_explicit_parent_conflict_never_changes_input(bool shared)
    {
        var row=new CrossTableExcelRow();row.Add("城市",Values("{\"countryId\":\"1\",\"cityId\":\"3\"}"),false);
        if(shared)row.Add("仓库",Values("{\"countryId\":\"2\",\"warehouseId\":\"4\"}"),false);
        var declared=Values(shared?"{\"name\":\"保留\"}":"{\"countryId\":\"2\"}");var before=JsonSerializer.Serialize(declared);
        Assert.True(Assert.Throws<PageImportException>(()=>row.Merge(declared)).IsInputError);Assert.Equal(before,JsonSerializer.Serialize(declared));
    }
    /// <summary>空值保留显式祖先，另一个明确非空分支提供共享槽。</summary>
    [Fact]
    public void Clear_keeps_declared_parent_and_yields_to_another_nonempty_path()
    {
        var row=new CrossTableExcelRow();row.Add("城市",Values("{\"countryId\":null,\"areaId\":null,\"cityId\":null}"),true);
        Assert.Equal("1",row.Merge(Values("{\"countryId\":\"1\"}"))["countryId"].GetString());
        row.Add("仓库",Values("{\"countryId\":\"2\",\"areaId\":\"22\",\"warehouseId\":\"4\"}"),false);
        var result=row.Merge(Values("{}"));Assert.Equal("2",result["countryId"].GetString());Assert.Equal(JsonValueKind.Null,result["cityId"].ValueKind);
    }
    /// <summary>部分选择的尾空层不能被另一主动路径静默加深。</summary>
    [Fact]
    public void Explicit_partial_path_does_not_silently_become_a_deeper_selection()
    {
        var row=new CrossTableExcelRow();row.Add("城市",Values("{\"countryId\":\"1\",\"areaId\":null,\"cityId\":null}"),false);row.Add("仓库",Values("{\"countryId\":\"1\",\"areaId\":\"22\",\"warehouseId\":\"4\"}"),false);
        Assert.Throws<PageImportException>(()=>row.Merge(Values("{}")));
    }
    /// <summary>非规范ID不得进入级联合并状态。</summary>
    [Theory][InlineData("\"01\"")][InlineData("1.0")][InlineData("{}")][InlineData("true")]
    public void Malformed_generated_or_explicit_ID_is_rejected(string value)
    {
        var row=new CrossTableExcelRow();Assert.Throws<PageImportException>(()=>row.Add("城市",Values("{\"cityId\":"+value+"}"),false));
    }
}
