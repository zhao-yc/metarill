using System.Text.Json;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>规则实体物理输出与逻辑筛选必须独立；普通页面保持原逻辑投影。</summary>
public sealed class NonRedundantRuleRecordPlanTests
{
    /// <summary>七种共享跨表计划保持完全相同逻辑条件，同时选择真实根ID存储，复制与输入变化不丢失证明。</summary>
    [Theory][InlineData("string","string",false,false)][InlineData("string","int64",false,false)]
    [InlineData("int64","string",false,false)][InlineData("int64","int64",false,false)]
    [InlineData("string","int64",true,false)][InlineData("int64","string",true,false)][InlineData("string","int64",false,true)]
    public void Rule_output_retains_physical_storage_without_changing_logical_scope(string type,string parent,bool shorter,bool derived)
    {
        var catalog=SharedCrossTableReadTests.Catalog("rule_storage",type,parent,shorter,derived);var before=JsonSerializer.Serialize(catalog);var outputs=SharedCrossTableReadTests.Outputs(shorter);var bindings=SharedCrossTableReadTests.Scopes(catalog);
        var fields=new List<string>{"id","country_id","area_id","warehouse_id"};if(!shorter)fields.Add("place_id");
        var normal=NonRedundantReads.CompileWithCrossTables(catalog,"1",outputs,bindings).MySql;
        var handle=NonRedundantReads.CompileRuleRecord(catalog,"1",outputs,fields,bindings,logicalOutputs:new[]{"country_id"});var record=handle.MySql;
        Assert.Empty(normal.StorageOutputs);Assert.Empty(normal.RuleOutputAliases);Assert.Equal(normal.From,record.From);Assert.Equal(normal.Scope,record.Scope);Assert.Equal(normal.Constants,record.Constants);
        foreach(var name in outputs){Assert.Equal(normal.Expression(name),record.Expression(name));Assert.Equal(normal.Types[name],record.Types[name]);}
        Assert.NotEqual(record.Expression("country_id"),record.OutputExpression("country_id"));Assert.Equal("b.`country_id`",record.OutputExpression("country_id"));Assert.Equal(record.Expression("country_name"),record.OutputExpression("country_name"));Assert.Equal(normal.OutputExpression("country_id"),normal.Expression("country_id"));
        var copied=record.WithPhysicalReferences(record.PhysicalReferences);Assert.Equal(record.StorageOutputs,copied.StorageOutputs);Assert.Equal(record.RuleOutputAliases,copied.RuleOutputAliases);Assert.Equal(record.Expression("country_id"),copied.Expression("country_id"));
        var row=new Dictionary<string,object?>{{"country_id",null},{record.RuleOutputAliases["country_id"],type=="string"?"9007199254740993":(object)9007199254740993L}};Assert.Null(NonRedundantReads.Value<string>(row,"country_id"));Assert.Equal("9007199254740993",NonRedundantReads.RuleValue<string>(handle,row,"country_id"));Assert.Throws<InvalidOperationException>(()=>NonRedundantReads.RuleValue<string>(handle,row,"not_published"));
        Assert.Equal(before,JsonSerializer.Serialize(catalog));fields.Clear();catalog.ConfigColumns.Single(column=>column.ConfigTableId=="1"&&column.ColumnName=="warehouse_id").ColumnName="changed";Assert.Equal("b.`warehouse_id`",record.OutputExpression("warehouse_id"));
    }
    /// <summary>不能把虚拟名称、未发布物理列、重复或任意表达式注册成根存储输出。</summary>
    [Theory][InlineData("derived")][InlineData("hidden")][InlineData("duplicate")][InlineData("empty")][InlineData("unknown")][InlineData("sql")]
    public void Unproven_storage_outputs_are_rejected(string kind)
    {
        var catalog=SharedCrossTableReadTests.Catalog("rule_storage","string","int64");var names=kind switch{"derived"=>new[]{"country_name"},"hidden"=>new[]{"delete_no"},"duplicate"=>new[]{"id","ID"},"empty"=>Array.Empty<string>(),"unknown"=>new[]{"missing"},_=>new[]{"id;DROP TABLE x"}};
        Assert.Throws<InvalidOperationException>(()=>NonRedundantReads.CompileRuleRecord(catalog,"1",SharedCrossTableReadTests.Outputs(false),names,SharedCrossTableReadTests.Scopes(catalog)));
    }
}
