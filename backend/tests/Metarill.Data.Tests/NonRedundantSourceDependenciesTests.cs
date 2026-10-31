using System.Text.Json;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>名称计划的来源依赖只保留物理列名，独立于回填结构预检，不公开SQL或可变元数据。</summary>
public sealed class NonRedundantSourceDependenciesTests
{
    /// <summary>物理、自动名称、多级及同表角色分别记录真实来源列，不能用最终名称替代FK。</summary>
    [Theory][InlineData("name","name")][InlineData("bill_name","bill_id")][InlineData("grand_label","bill_id")][InlineData("parent_name","parent_id")][InlineData("bill_amount","bill_id")]
    public void Dependencies_capture_real_source_fields_and_remain_immutable(string field,string required)
    {
        var c=NonRedundantReadTests.Catalog("mt_source_dependency","mt_target_dependency");var before=JsonSerializer.Serialize(c);var columns=NonRedundantReads.CompileSourceDependencies(c,"1",field);Assert.Contains(required,columns);Assert.DoesNotContain("ship_id",columns);Assert.Equal(columns.Order(StringComparer.Ordinal),columns);Assert.Equal(before,JsonSerializer.Serialize(c));var list=Assert.IsAssignableFrom<IList<string>>(columns);Assert.True(list.IsReadOnly);Assert.Throws<NotSupportedException>(()=>list.Add("伪造列"));c.ConfigColumns.Single(x=>x.ColumnName==required&&x.ConfigTableId=="1").ColumnName="later_changed";Assert.Contains(required,columns);
    }
    /// <summary>终点安全和循环证明继续复用匹配编译，不为依赖推导放开不受支持读取。</summary>
    [Theory][InlineData("password")][InlineData("file")][InlineData("array")][InlineData("json")][InlineData("virtual")][InlineData("cycle")][InlineData("unknown")]
    public void Invalid_names_do_not_produce_usable_dependencies(string kind)
    {
        var c=NonRedundantReadTests.Catalog("mt_source_dependency","mt_target_dependency");var target=c.ConfigColumns.Single(x=>x.Id=="22");var field=c.ConfigColumns.Single(x=>x.Id=="18");
        if(kind=="password"){target.ColumnName="password";field.Extra["columnFullName"]=JsonSerializer.SerializeToElement("bill.password");}
        if(kind=="file")target.ControlKind="FileLibrary";if(kind=="array")target.ControlKind="MultiSelectPage";if(kind=="json")target.LogicalType="json";if(kind=="virtual")target.IsVirtualColumn=1;if(kind=="cycle")field.Extra["columnFullName"]=JsonSerializer.SerializeToElement("parent.grand_label");Assert.ThrowsAny<Exception>(()=>NonRedundantReads.CompileSourceDependencies(c,"1",kind=="unknown"?"missing":"grand_label"));
    }
}
