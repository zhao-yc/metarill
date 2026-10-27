using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>通用复选沿原四段定义、精确ID及只读回显，不把非法配置降级为宽候选。</summary>
public sealed class DatabaseCheckboxContractTests
{
    /// <summary>旧四段只按第三、四段设置查询，原元数据文本不被重写。</summary>
    [Fact]
    public void Legacy_four_parts_and_json_keep_where_and_order_without_executing_header_text()
    {
        const string legacy="原第一项;原第二项; status=1 AND name LIKE '甲%' ; name desc, id asc ";var definition=DatabaseCheckboxDefinition.Parse(legacy);Assert.Equal("status=1 AND name LIKE '甲%'",definition.WhereSql);Assert.Equal("name desc, id asc",definition.OrderBy);Assert.Equal("",definition.Table);
        var json=DatabaseCheckboxDefinition.Parse("{\"tableId\":42,\"table\":\"choices\",\"whereSql\":\"status=1\",\"orderBy\":\"id desc\"}");Assert.Equal("42",json.TableId);Assert.Equal("choices",json.Table);Assert.Equal("status=1",json.WhereSql);
        Assert.Equal("",DatabaseCheckboxDefinition.Parse("choices").ReadDefinition(new(){LinkConfigTableId="42"}).GetProperty("table").GetString());Assert.Equal("choices",DatabaseCheckboxDefinition.Parse("choices").ReadDefinition(new()).GetProperty("table").GetString());
    }
    /// <summary>原目标页优先于旧提示，生成侧和运行时不能解释为两张表。</summary>
    [Fact]public void Explicit_page_binding_does_not_reintroduce_legacy_table_hint()
    {
        var source=new ColumnMetadata{Extra=new(){["linkConfigPageId"]=JsonSerializer.SerializeToElement("201")}};var before=JsonSerializer.Serialize(source);Assert.Equal("",DatabaseCheckboxDefinition.Parse("old_hint").ReadDefinition(source).GetProperty("table").GetString());Assert.Equal(before,JsonSerializer.Serialize(source));Assert.Equal("",new DatabaseCheckboxDefinition("42","","201","","","old_hint").ReadDefinition(new()).GetProperty("table").GetString());Assert.Equal("old_hint",DatabaseCheckboxDefinition.Parse("old_hint").ReadDefinition(new()).GetProperty("table").GetString());
    }
    /// <summary>长ID从数字原文精确转成字符串，已有字符串数组文本的空白和顺序保持。</summary>
    [Fact]
    public void Arrays_keep_null_empty_text_order_and_exact_long_values_without_float_conversion()
    {
        const string text="[ \"9007199254740993\", \"2\" ]";Assert.Equal(text,DatabaseCheckboxValue.ReadWrite(JsonSerializer.SerializeToElement(text)));Assert.Equal("[\"9007199254740993\",\"2\"]",DatabaseCheckboxValue.ReadWrite(JsonSerializer.Deserialize<JsonElement>("[9007199254740993,2]")));Assert.Null(DatabaseCheckboxValue.ReadWrite(null));Assert.Equal("[]",DatabaseCheckboxValue.ReadWrite(JsonSerializer.Deserialize<JsonElement>("[]")));Assert.Equal(JsonValueKind.Array,DatabaseCheckboxValue.ProjectIds("[]")!.Value.ValueKind);
        var all=JsonSerializer.Serialize(Enumerable.Range(1,5000).Select(id=>id.ToString()));Assert.Equal(5000,DatabaseCheckboxValue.Parse(all).Length);Assert.Throws<ArgumentException>(()=>DatabaseCheckboxValue.Parse(JsonSerializer.Serialize(Enumerable.Range(1,5001).Select(id=>id.ToString()))));
    }
    /// <summary>坏ID、重复项及结构输入都不能进入数据库或变成部分保存。</summary>
    [Theory][InlineData("[1.0]")][InlineData("[1e2]")][InlineData("[true]")][InlineData("[null]")][InlineData("[{}]")][InlineData("[\"01\"]")][InlineData("[\"-1\"]")][InlineData("[\"9223372036854775808\"]")][InlineData("[\"1\",1]")][InlineData("{}")][InlineData("null")][InlineData("")]
    public void Invalid_array_values_are_rejected_before_io(string value)=>Assert.Throws<ArgumentException>(()=>DatabaseCheckboxValue.Parse(value));
    /// <summary>原只读数组与空候选有明确区别；客户端不能替换目标或追加SQL。
    /// </summary>
    [Fact]
    public void Candidate_request_preserves_omission_empty_array_and_declared_form_slot()
    {
        Assert.Null(DatabaseCheckboxOptionsRequest.Read(JsonSerializer.Deserialize<JsonElement>("{}")).Selected);Assert.Empty(DatabaseCheckboxOptionsRequest.Read(JsonSerializer.Deserialize<JsonElement>("{\"id\":[]}")).Selected!);Assert.Equal(new long[]{9007199254740993},DatabaseCheckboxOptionsRequest.Read(JsonSerializer.Deserialize<JsonElement>("{\"id\":[\"9007199254740993\"],\"parentId\":\"42\",\"formParameters\":{\"source\":{\"groupId\":\"7\"}}}")).Selected);
        foreach(var json in new[]{"{\"table\":\"other\"}","{\"sql\":\"select 1\"}","{\"id\":[],\"id\":[\"1\"]}","[]","{\"parentId\":{}}"})Assert.Throws<ArgumentException>(()=>DatabaseCheckboxOptionsRequest.Read(JsonSerializer.Deserialize<JsonElement>(json)));
    }
    /// <summary>重复键、未知查询、错拼大小写及不完整分段不得被静默忽略。
    /// </summary>
    [Theory][InlineData("a;b;c")][InlineData("a;b;c;d;e")][InlineData("{\"whereSql\":\"status=0\",\"whereSql\":\"status=1\"}")][InlineData("{\"WhereSql\":\"status=1\"}")][InlineData("{\"sql\":\"select 1\"}")][InlineData("{\"nameField\":\"password\"}")][InlineData("{\"whereSql\":[]}")]
    [InlineData("{\"filter\":\"status=1\"}")][InlineData("{\"targetParentField\":\"groupId\"}")]
    public void Unproven_definitions_are_rejected(string value)=>Assert.ThrowsAny<Exception>(()=>DatabaseCheckboxDefinition.Parse(value));
}
