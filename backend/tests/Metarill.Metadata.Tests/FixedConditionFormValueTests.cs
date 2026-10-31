using System.Text.Json;
using Metarill.Core.Contracts;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>原表单槽只提供已声明的叶值，不把对象、重复键或缺失与null混为一谈。</summary>
public sealed class FixedConditionFormValueTests
{
    /// <summary>单层与原表名嵌套路径保留标量的原JSON表示。</summary>
    [Theory]
    [InlineData("{\"allowed\":9007199254740993}","allowed","9007199254740993")]
    [InlineData("{\"allowed\":null}","allowed","null")]
    [InlineData("{\"allowed\":false}","allowed","false")]
    [InlineData("{\"allowed\":\"A AND B, C\"}","allowed","\"A AND B, C\"")]
    [InlineData("{\"row\":{\"allowed\":\"值\"},\"current_yz_project_id\":\"伪造\"}","row.allowed","\"值\"")]
    public void Reads_only_the_published_scalar_and_detaches_document_lifetime(string json,string path,string expected)
    {
        JsonElement value;using(var document=JsonDocument.Parse(json))value=FixedConditionFormValue.Read(document.RootElement,path.Split('.'));
        Assert.Equal(expected,value.GetRawText());
    }
    /// <summary>只检查所需路径；未知无关字段不能变成SQL或身份，但路径重复必须整体拒绝。</summary>
    [Theory]
    [InlineData("{}","allowed")]
    [InlineData("null","allowed")]
    [InlineData("[]","allowed")]
    [InlineData("{\"allowed\":[]}","allowed")]
    [InlineData("{\"allowed\":{}}","allowed")]
    [InlineData("{\"allowed\":1,\"allowed\":2}","allowed")]
    [InlineData("{\"row\":{},\"row\":{\"allowed\":1}}","row.allowed")]
    [InlineData("{\"row\":null}","row.allowed")]
    [InlineData("{\"row\":{\"Allowed\":1}}","row.allowed")]
    public void Invalid_context_never_drops_the_condition(string json,string path)
    {using var document=JsonDocument.Parse(json);Assert.Throws<ArgumentException>(()=>FixedConditionFormValue.Read(document.RootElement,path.Split('.')));}
    /// <summary>旧数组到分页和只读回读保留相同表单槽，即使原请求文档已释放。</summary>
    [Theory]
    [InlineData("null")]
    [InlineData("\"9007199254740993\"")]
    public void Legacy_request_preserves_form_values_in_both_routes(string id)
    {
        PageRequest page;RelationOptionsSelectedRequest selected;
        using(var document=JsonDocument.Parse("{\"allowed\":\"原值\"}"))
        {var request=new RelationOptionsLegacyRequest{FormParameters=document.RootElement,Id=JsonSerializer.Deserialize<Optional<JsonElement?>>(id)};page=request.ToPageRequest();selected=request.ToSelectedRequest()!;}
        Assert.Equal("原值",FixedConditionFormValue.Read(page.FormParameters,["allowed"]).GetString());Assert.Equal("原值",FixedConditionFormValue.Read(selected.FormParameters,["allowed"]).GetString());
    }
    /// <summary>原嵌套缓存与旧平铺调用都能绑定；明确null和重复主键不会退回另一值。</summary>
    [Theory]
    [InlineData("{\"row\":{\"allowed\":1}}","allowed","row.allowed","1")]
    [InlineData("{\"allowed\":1}","row.allowed","allowed","1")]
    [InlineData("{\"allowed\":null,\"row\":{\"allowed\":1}}","allowed","row.allowed","null")]
    public void Only_missing_published_paths_use_the_same_field_alias(string json,string path,string fallback,string expected)
    {using var document=JsonDocument.Parse(json);Assert.Equal(expected,FixedConditionFormValue.Read(document.RootElement,path.Split('.'),fallback.Split('.')).GetRawText());}
    /// <summary>重复、容器和显式父对象null不能被别名变成另一份可接受值。</summary>
    [Theory]
    [InlineData("{\"allowed\":1,\"allowed\":2,\"row\":{\"allowed\":3}}","allowed","row.allowed")]
    [InlineData("{\"allowed\":[],\"row\":{\"allowed\":3}}","allowed","row.allowed")]
    [InlineData("{\"row\":null,\"allowed\":3}","row.allowed","allowed")]
    public void Invalid_primary_context_cannot_be_replaced_by_alias(string json,string path,string fallback)
    {using var document=JsonDocument.Parse(json);Assert.Throws<ArgumentException>(()=>FixedConditionFormValue.Read(document.RootElement,path.Split('.'),fallback.Split('.')));}
    /// <summary>空值可绑定，但缺失的私有窄变量不应扩大范围。</summary>
    [Fact]
    public void Explicit_null_is_available_while_missing_variables_reject()
    {Assert.Null(FixedConditionFormValue.Require(new Dictionary<string,object?>{{"formParameters.allowed",null}},"formParameters.allowed"));Assert.Throws<ArgumentException>(()=>FixedConditionFormValue.Require(null,"formParameters.allowed"));Assert.Throws<ArgumentException>(()=>FixedConditionFormValue.Require(new Dictionary<string,object?>(),"formParameters.allowed"));}
}
