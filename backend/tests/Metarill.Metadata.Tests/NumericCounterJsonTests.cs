using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;
using Metarill.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>分页与Count沿用原数字协议，字符串ID及宿主原有JSON描述器必须同时保留。</summary>
public sealed class NumericCounterJsonTests
{
    /// <summary>超过int32乃至JS安全整数的计数仍以完整int64数字输出，记录ID继续用字符串。</summary>
    [Theory]
    [InlineData(0L)][InlineData(2L)][InlineData(2147483648L)][InlineData(9007199254740993L)][InlineData(long.MaxValue)]
    public void Page_and_scalar_count_are_numbers_but_identifiers_remain_strings(long count)
    {
        foreach(var options in Options())
        {
            using var page=JsonDocument.Parse(JsonSerializer.Serialize(R<PageResult<CounterRow>>.Success(new([new(9007199254740993L,"原记录")],count,count,1,20)),options));
            var data=page.RootElement.GetProperty("data");
            Assert.Equal(count,data.GetProperty("total").GetInt64());Assert.Equal(count,data.GetProperty("pages").GetInt64());
            Assert.Equal("9007199254740993",data.GetProperty("records")[0].GetProperty("id").GetString());
            var response=R<long>.Success(count);response.Map["id"]=9007199254740993L;
            using var scalar=JsonDocument.Parse(JsonSerializer.Serialize(response,options));Assert.Equal(count,scalar.RootElement.GetProperty("data").GetInt64());Assert.Equal("9007199254740993",scalar.RootElement.GetProperty("map").GetProperty("id").GetString());
            Assert.Equal("\"9007199254740993\"",JsonSerializer.Serialize(9007199254740993L,options));
        }
    }
    /// <summary>旧数字字符串仍可读；省略、null及嵌套标识不会随计数修正改变。</summary>
    [Fact]
    public void Legacy_reading_optional_fields_and_nested_identifiers_stay_compatible()
    {
        foreach(var options in Options())
        {
            var page=JsonSerializer.Deserialize<PageResult<CounterRow>>("{\"records\":[],\"total\":\"2\",\"pages\":\"1\",\"current\":1,\"size\":20}",options)!;
            Assert.Equal(2,page.Total);Assert.Equal(1,page.Pages);
            Assert.Equal(2,JsonSerializer.Deserialize<R<long>>("{\"code\":200,\"data\":\"2\",\"map\":{}}",options)!.Data);
            Assert.False(JsonSerializer.Deserialize<CounterBody>("{}",options)!.Id.IsSpecified);
            var clear=JsonSerializer.Deserialize<CounterBody>("{\"id\":null}",options)!.Id;Assert.True(clear.IsSpecified);Assert.Null(clear.Value);
            using var row=JsonDocument.Parse(JsonSerializer.Serialize(R<CounterRow>.Success(new(9007199254740993L,"原记录")),options));Assert.Equal("9007199254740993",row.RootElement.GetProperty("data").GetProperty("id").GetString());
        }
    }
    /// <summary>追加计数合同不能覆盖宿主既有解析器及属性定制。</summary>
    [Fact]
    public void Copy_preserves_existing_type_resolver()
    {
        var resolver=new DefaultJsonTypeInfoResolver();resolver.Modifiers.Add(info=>{if(info.Type==typeof(CounterRow))info.Properties.Single(p=>p.Name=="name").Name="retainedName";});
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web){TypeInfoResolver=resolver};JsonDefaults.CopyTo(options);
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(new CounterRow(9007199254740993L,"原记录"),options));Assert.Equal("原记录",json.RootElement.GetProperty("retainedName").GetString());
        using var count=JsonDocument.Parse(JsonSerializer.Serialize(R<long>.Success(2),options));Assert.Equal(2,count.RootElement.GetProperty("data").GetInt64());
    }
    /// <summary>独立宿主使用源生成描述器时，计数规则也不能依赖反射属性身份。</summary>
    [Fact]
    public void Source_generated_resolver_keeps_numeric_count()
    {
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web){TypeInfoResolver=NumericCounterJsonContext.Default};JsonDefaults.CopyTo(options);
        using var count=JsonDocument.Parse(JsonSerializer.Serialize(R<long>.Success(long.MaxValue),options));Assert.Equal(long.MaxValue,count.RootElement.GetProperty("data").GetInt64());
    }
    /// <summary>计数读取拒绝溢出、小数及任意非数字类型，不能悄悄截断或清零。</summary>
    [Theory][InlineData("9223372036854775808")][InlineData("1.5")][InlineData("true")][InlineData("null")][InlineData("\"abc\"")]
    public void Invalid_count_tokens_are_rejected(string token)=>Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<R<long>>("{\"code\":200,\"data\":"+token+",\"map\":{}}",JsonDefaults.Create()));
    /// <summary>Agent/MCP的实际响应描述区分数字计数与字符串标识。</summary>
    [Fact]
    public void Response_schema_matches_numeric_counters_and_string_identifiers()
    {
        var page=Properties(RequestJsonSchema.Create(typeof(PageResult<CounterRow>)));Assert.Equal("integer",((Dictionary<string,object?>)page["total"]!)["type"]);Assert.Equal("integer",((Dictionary<string,object?>)page["pages"]!)["type"]);
        var scalar=Properties(RequestJsonSchema.Create(typeof(R<long>)));Assert.Equal("integer",((Dictionary<string,object?>)scalar["data"]!)["type"]);
        var row=Properties(RequestJsonSchema.Create(typeof(CounterRow)));Assert.Equal(new[]{"string","integer"},Assert.IsType<string[]>(((Dictionary<string,object?>)row["id"]!)["type"]));
    }
    /// <summary>真实MVC/Kestrel使用CopyTo，与独立宿主的Create选项产生同一原接口响应。</summary>
    [Fact]
    public async Task Actual_http_page_and_count_keep_original_numeric_contract()
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Testing"});builder.Logging.ClearProviders();builder.WebHost.ConfigureKestrel(server=>server.Listen(IPAddress.Loopback,0));
        builder.Services.AddControllers().AddApplicationPart(typeof(NumericCounterProbeController).Assembly).AddJsonOptions(json=>JsonDefaults.CopyTo(json.JsonSerializerOptions));
        await using var app=builder.Build();app.MapControllers();await app.StartAsync();
        try
        {
            var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();using var client=new HttpClient{BaseAddress=new Uri(address)};
            using var page=await client.GetFromJsonAsync<JsonDocument>("/__counter_contract/page");var data=page!.RootElement.GetProperty("data");Assert.Equal(2,data.GetProperty("total").GetInt64());Assert.Equal(1,data.GetProperty("pages").GetInt64());Assert.Equal("9007199254740993",data.GetProperty("records")[0].GetProperty("id").GetString());
            using var count=await client.GetFromJsonAsync<JsonDocument>("/__counter_contract/count");Assert.Equal(2,count!.RootElement.GetProperty("data").GetInt64());
        }
        finally{await app.StopAsync();}
    }
    /// <summary>覆盖平台新建选项及独立宿主复制选项两条真实入口。</summary>
    private static IEnumerable<JsonSerializerOptions> Options(){yield return JsonDefaults.Create();var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);JsonDefaults.CopyTo(options);yield return options;}
    /// <summary>读取共用schema的实际属性表，不重建预期描述。</summary>
    private static Dictionary<string,object?> Properties(Dictionary<string,object?> schema)=>(Dictionary<string,object?>)schema["properties"]!;
    public sealed record CounterRow(long Id,string Name);
    private sealed class CounterBody{public Optional<long?> Id{get;set;}}
}

/// <summary>只用于无数据库的真实HTTP合同验收，不进入产品宿主。</summary>
[ApiController][Route("__counter_contract")]
public sealed class NumericCounterProbeController:ControllerBase
{
    /// <summary>模拟原生成Service返回的同一分页类型。</summary>
    [HttpGet("page")]
    public R<PageResult<NumericCounterJsonTests.CounterRow>> Page()=>R<PageResult<NumericCounterJsonTests.CounterRow>>.Success(new([new(9007199254740993L,"原记录")],2,1,1,20));
    /// <summary>沿用原生成Count动作的实际返回类型。</summary>
    [HttpGet("count")]
    public R<long> Count()=>R<long>.Success(2);
}

/// <summary>真实System.Text.Json源生成上下文，只参与合同测试。</summary>
[JsonSerializable(typeof(R<long>))]
internal partial class NumericCounterJsonContext : JsonSerializerContext { }
