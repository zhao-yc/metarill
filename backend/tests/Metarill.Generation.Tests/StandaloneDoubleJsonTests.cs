using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>执行原宿主模板的 JSON 登记，验证 MVC 的数字和日历输入以及最小接口的数字响应。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class StandaloneDoubleJsonTests
{
    /// <summary>两个宿主档案保留数值合同，并接受原页面日期和显式清空。</summary>
    [Theory]
    [InlineData("Program.cs.tpl")]
    [InlineData("DevelopmentProgram.cs.tpl")]
    public async Task Original_host_json_registration_preserves_adjacent_values_and_rejects_invalid_input(string template)
    {
        var source = NonRedundantApplicationFixture.Root();
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-standalone-double-json");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var lines = await File.ReadAllLinesAsync(Path.Combine(source, "backend/templates/standalone", template));
            var mvc = lines.Single(line => line.StartsWith("builder.Services.AddControllers()", StringComparison.Ordinal));
            var registration = "builder.Services.AddControllers()" + mvc[mvc.IndexOf(".AddJsonOptions(", StringComparison.Ordinal)..];
            var minimal = lines.SingleOrDefault(line => line.StartsWith("builder.Services.ConfigureHttpJsonOptions(", StringComparison.Ordinal)) ?? "";
            // 只截取真实模板的序列化登记，不另写一套预期配置，不连接数据库或注入业务身份。
            var program = """
using Microsoft.AspNetCore.Mvc;
using Metarill.Core.Contracts;
var builder = WebApplication.CreateBuilder(args);
""" + "\n" + registration + "\n" + minimal + "\n" + """
var app = builder.Build();
app.MapControllers();
app.MapGet("/minimal", () => NumericProbe.Value());
app.MapGet("/health", () => "ok");
app.Run();
public sealed record NumericProbe(double Upper, double Lower, long Id)
{
    // 固定位型只作为真实HTTP序列化对照值。
    public static NumericProbe Value() => new(BitConverter.UInt64BitsToDouble(0x0410000000000000), BitConverter.UInt64BitsToDouble(0x040fffffffffffff), 9007199254740993);
}
[ApiController, Route("numeric")]
public sealed class NumericProbeController : ControllerBase
{
    // 读取和提交均经过真实MVC格式化器。
    [HttpGet] public NumericProbe Get() => NumericProbe.Value();
    [HttpPost] public NumericProbe Post([FromBody] NumericProbe value) => value;
    [HttpPost("calendar")] public object Calendar([FromBody] CalendarProbe value) => new
    {
        start=value.LoginRiseTime.Value, last=value.LastLoginTime, stop=value.LoginStopTime.Value,
        stopSpecified=value.LoginStopTime.IsSpecified, validitySpecified=value.ValidityTime.IsSpecified
    };
    // 分页和Count用原生成动作的真实合同；记录ID仍是高位字符串。
    [HttpGet("page")] public R<PageResult<NumericProbe>> Page() => R<PageResult<NumericProbe>>.Success(new(new[]{NumericProbe.Value()},2,1,1,20));
    [HttpGet("count")] public R<long> Count() => R<long>.Success(2);

}
public sealed record CalendarProbe(Optional<DateTime?> LoginRiseTime, DateTime LastLoginTime,
    Optional<DateTime?> LoginStopTime, Optional<DateTime?> ValidityTime);
""";
            await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"), program);
            var project = "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include=\"" + System.Security.SecurityElement.Escape(Path.Combine(source, "backend/src/Metarill.Core/Metarill.Core.csproj")) + "\" /></ItemGroup></Project>";
            await File.WriteAllTextAsync(Path.Combine(directory, "NumericProbe.csproj"), project);
            var sdk = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            var build = NonRedundantApplicationFixture.Start(sdk, directory);
            foreach (var argument in new[] { "build", "NumericProbe.csproj", "-c", "Release", "-warnaserror", "--disable-build-servers", "--verbosity", "quiet" }) build.ArgumentList.Add(argument);
            await NonRedundantApplicationFixture.RunAsync(build);
            var start = NonRedundantApplicationFixture.Start(sdk, directory);
            start.ArgumentList.Add(Path.Combine(directory, "bin/Release/net10.0/NumericProbe.dll"));
            // 纯回环探针不写持久证据，不继承上层执行器的跨目录日志配置。
            start.Environment.Remove("METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY");
            await using var host = await FreshHost.StartAsync(start, "/health");
            foreach (var path in new[] { "/numeric", "/minimal" })
            {
                using var response = await host.Client.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                AssertValues(json.RootElement, path == "/numeric");
            }
            using (var response=await host.Client.GetAsync("/numeric/page"))
            {
                Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var page=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var data=page.RootElement.GetProperty("data");Assert.Equal(2,data.GetProperty("total").GetInt64());Assert.Equal(1,data.GetProperty("pages").GetInt64());AssertValues(data.GetProperty("records")[0]);
            }
            using (var response=await host.Client.GetAsync("/numeric/count"))
            {
                Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var count=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal(2,count.RootElement.GetProperty("data").GetInt64());
            }
            const string body = "{\"upper\":4.1045368012983762E-289,\"lower\":4.1045368012983758E-289,\"id\":\"9007199254740993\"}";
            using var echoed = await host.Client.PostAsync("/numeric", new StringContent(body, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, echoed.StatusCode);
            using var result = JsonDocument.Parse(await echoed.Content.ReadAsStringAsync());
            AssertValues(result.RootElement);
            foreach (var invalid in new[] { "1e309", "1e-999", "\"NaN\"" })
            {
                using var rejected = await host.Client.PostAsync("/numeric", new StringContent(body.Replace("4.1045368012983762E-289", invalid, StringComparison.Ordinal), Encoding.UTF8, "application/json"));
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            }
            // 原详情提交带空格的日历日期，未修改的已读回字段仍可能是带小数秒的 ISO 文本。
            const string calendar = """{"loginRiseTime":"2026-11-14 12:00:00","lastLoginTime":"2026-11-12T07:54:42.946363","loginStopTime":""}""";
            using var dated = await host.Client.PostAsync("/numeric/calendar", new StringContent(calendar, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, dated.StatusCode);
            using var date = JsonDocument.Parse(await dated.Content.ReadAsStringAsync());
            var fields = date.RootElement;
            Assert.Equal("2026-11-14T12:00:00", fields.GetProperty("start").GetString());
            Assert.Equal("2026-11-12T07:54:42.946363", fields.GetProperty("last").GetString());
            Assert.Equal(JsonValueKind.Null, fields.GetProperty("stop").ValueKind);
            Assert.True(fields.GetProperty("stopSpecified").GetBoolean());
            Assert.False(fields.GetProperty("validitySpecified").GetBoolean());
            using var badDate = await host.Client.PostAsync("/numeric/calendar",
                new StringContent(calendar.Replace("2026-11-14", "2026-11-31", StringComparison.Ordinal), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, badDate.StatusCode);
        }
        finally
        {
            if (Directory.Exists(directory)) await TestDirectoryCleanup.DeleteAsync(directory, boundary);
        }
    }

    /// <summary>位型逐位核对；不以近似数值或默认浮点文本序列化掩盖响应错误。</summary>
    private static void AssertValues(JsonElement value, bool stringId = true)
    {
        Assert.Equal(0x0410000000000000UL, BitConverter.DoubleToUInt64Bits(value.GetProperty("upper").GetDouble()));
        Assert.Equal(0x040fffffffffffffUL, BitConverter.DoubleToUInt64Bits(value.GetProperty("lower").GetDouble()));
        if (stringId) Assert.Equal("9007199254740993", value.GetProperty("id").GetString());
        else Assert.Equal(9007199254740993L, value.GetProperty("id").GetInt64());
    }
}
