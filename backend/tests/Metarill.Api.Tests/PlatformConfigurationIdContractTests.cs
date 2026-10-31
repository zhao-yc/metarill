using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Platform.Generated.Entities;
using Metarill.Platform.Generated.DTOs;
using Metarill.Runtime;
using Xunit;
namespace Metarill.Api.Tests;

/// <summary>直接验证当前平台实际生成类型，不以另一份手写实体模拟ID优化。</summary>
public sealed class PlatformConfigurationIdContractTests
{
    private const long Value=9007199254740993L;
    /// <summary>六个真实实体均为可空long；HTTP公共JSON仍用无损字符串ID，null保留。</summary>
    [Theory]
    [InlineData(typeof(ConfigChild),"ChildConfigPageId","childConfigPageId")]
    [InlineData(typeof(ConfigColumn),"LinkConfigTableId","linkConfigTableId")]
    [InlineData(typeof(ConfigColumn),"LinkConfigPageId","linkConfigPageId")]
    [InlineData(typeof(ConfigPageColumn),"LinkConfigTableId","linkConfigTableId")]
    [InlineData(typeof(ConfigPageColumn),"LinkConfigPageId","linkConfigPageId")]
    [InlineData(typeof(ConfigPageColumn),"CombineSuperColumnId","combineSuperColumnId")]
    public void Actual_entities_keep_string_id_wire_format(Type type,string property,string jsonName)
    {
        var member=type.GetProperty(property)!;Assert.Equal(typeof(long?),member.PropertyType);
        var entity=JsonSerializer.Deserialize("{\""+jsonName+"\":\""+Value+"\"}",type,JsonDefaults.Options)!;Assert.Equal(Value,member.GetValue(entity));
        Assert.Equal(Value.ToString(),JsonSerializer.SerializeToElement(entity,type,JsonDefaults.Options).GetProperty(jsonName).GetString());
        member.SetValue(entity,null);Assert.Equal(JsonValueKind.Null,JsonSerializer.SerializeToElement(entity,type,JsonDefaults.Options).GetProperty(jsonName).ValueKind);
    }
    /// <summary>原可写字段保持省略/null/空选择差异，数字和字符串ID均不经过浮点转换。</summary>
    [Theory]
    [InlineData(typeof(ConfigChildPage1679390851298631682CreateRequest),"ChildConfigPageId","childConfigPageId")]
    [InlineData(typeof(ConfigChildPage1679390851298631682UpdateRequest),"ChildConfigPageId","childConfigPageId")]
    [InlineData(typeof(ConfigPageColumnPage6218036784398336CreateRequest),"LinkConfigTableId","linkConfigTableId")]
    [InlineData(typeof(ConfigPageColumnPage6218036784398336UpdateRequest),"LinkConfigTableId","linkConfigTableId")]
    [InlineData(typeof(ConfigPageColumnPage6218036784398336CreateRequest),"LinkConfigPageId","linkConfigPageId")]
    [InlineData(typeof(ConfigPageColumnPage6218036784398336UpdateRequest),"LinkConfigPageId","linkConfigPageId")]
    [InlineData(typeof(ConfigPageColumnPage6218036784398336CreateRequest),"CombineSuperColumnId","combineSuperColumnId")]
    [InlineData(typeof(ConfigPageColumnPage6218036784398336UpdateRequest),"CombineSuperColumnId","combineSuperColumnId")]
    public void Actual_dtos_keep_presence_and_exact_nullable_id(Type type,string property,string jsonName)
    {
        var member=type.GetProperty(property)!;Assert.Equal(typeof(Optional<long?>),member.PropertyType);
        Optional<long?> Read(string json)=>(Optional<long?>)member.GetValue(JsonSerializer.Deserialize(json,type,JsonDefaults.Options))!;
        Assert.False(Read("{}").IsSpecified);
        foreach(var text in new[]{"\""+Value+"\"",Value.ToString()}){var input=Read("{\""+jsonName+"\":"+text+"}");Assert.True(input.IsSpecified);Assert.Equal(Value,input.Value);}
        foreach(var text in new[]{"null","\"\""}){var input=Read("{\""+jsonName+"\":"+text+"}");Assert.True(input.IsSpecified);Assert.Null(input.Value);}
        Assert.ThrowsAny<Exception>(()=>Read("{\""+jsonName+"\":\"9223372036854775808\"}"));
    }
}
