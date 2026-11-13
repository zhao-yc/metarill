using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>通用复选固定读取仅签发已声明数组成员／名称用途，不能拿任意字符串列冒充集合。</summary>
public sealed class DatabaseCheckboxReadTests
{
    /// <summary>旧四段／裸目标／JSON均不改输入目录，自动名称数组仍由静态多值计划提供。</summary>
    [Theory][InlineData("头1;头2;;id desc")][InlineData("mt_choice")][InlineData("{\"tableId\":\"2\"}")]
    public void Compiles_generic_names_without_mutating_source_or_guessing_sql(string definition)
    {
        var catalog=Catalog(definition);var saved=JsonSerializer.Serialize(catalog);var plan=NonRedundantReads.Compile(catalog,"1",["id","bill_ids","bill_names"],["bill_ids"]);Assert.Equal("string",plan.OutputTypes["bill_names"]);Assert.Equal(saved,JsonSerializer.Serialize(catalog));
    }
    /// <summary>非法值及非复选字段在连接前失败，不先执行COUNT再报错。
    /// </summary>
    [Theory][InlineData("unknown")][InlineData("ordinary")][InlineData("noncanonical")][InlineData("fraction")][InlineData("object")][InlineData("null")]
    public async Task Member_filter_requires_proven_array_and_exact_id_before_io(string kind)
    {
        var catalog=Catalog("");if(kind=="ordinary")catalog.ConfigColumns.Single(column=>column.Id=="15").ControlKind="";
        var plan=NonRedundantReads.Compile(catalog,"1",["id","bill_ids"]);object? value=kind switch{"noncanonical"=>"01","fraction"=>1.5m,"object"=>new{},"null"=>null,_=>"1"};
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=invalid;Connection Timeout=1"});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new NonRedundantReader(session).PageAsync(plan,[new(kind=="unknown"?"name":"bill_ids",FixedReadComparison.JsonArrayHasId,value)],[],1,10));
    }
    /// <summary>只给通用复选数据类型，不修改其它单值／层级关系目录。
    /// </summary>
    internal static MetadataCatalog Catalog(string definition)
    {
        var catalog=NonRedundantReadTests.Catalog("mt_checkbox","mt_choice");var source=catalog.ConfigColumns.Single(column=>column.Id=="15");source.ColumnName="bill_ids";source.LogicalType="string";source.ControlKind="MultiCheckBoxDatabase";source.ControlDetail=definition;return catalog;
    }
}
