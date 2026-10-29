using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Agent.Tests
{
    /// <summary>真实 MVC 目录发现必须把兼容入口还原为严格 DTO，而非开放 JsonElement。</summary>
    public sealed class LegacyPageSchemaTests
    {
        [Fact]
        public void Actual_mvc_capability_keeps_generated_typed_schema_and_optional_fields()
        {
            var directory = Path.Combine(Path.GetTempPath(), "metarill-legacy-schema", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try
            {
                var demo = new MetadataCatalog { YzProjectId = "77" };
                demo.ConfigTables.Add(new() { Id = "1", TableName = "legacy_fixture" });
                demo.ConfigColumns.Add(new() { Id = "11", ConfigTableId = "1", ColumnName = "id", LogicalType = "int64" });
                demo.ConfigColumns.Add(new() { Id = "12", ConfigTableId = "1", ColumnName = "remark", LogicalType = "string" });
                demo.ConfigColumns.Add(new() { Id = "13", ConfigTableId = "1", ColumnName = "flag", LogicalType = "bool", ControlKind = "CheckBox" });
                demo.ConfigColumns.Add(new() { Id = "14", ConfigTableId = "1", ColumnName = "normal", LogicalType = "bool" });
                demo.ConfigPages.Add(new() { Id = "101", ConfigTableId = "1", YzProjectId = "77" });
                var platform = Path.Combine(directory, "platform.json"); var business = Path.Combine(directory, "demo.json");
                File.WriteAllText(platform, JsonSerializer.Serialize(new MetadataCatalog { YzProjectId = "76" })); File.WriteAllText(business, JsonSerializer.Serialize(demo));
                var services = new ServiceCollection(); services.AddLogging(); services.AddControllers().AddApplicationPart(typeof(Generated.Controllers.LegacyFixtureController).Assembly);
                using var provider = services.BuildServiceProvider();
                var index = new GeneratedCapabilityIndex(provider.GetRequiredService<IActionDescriptorCollectionProvider>(), new MetadataRegistry(platform, business));
                var capability = new AgentCapabilityCatalog(index, new Authorization()).Describe("page:101:update");
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(capability.RequestSchema)); var schema = json.RootElement;
                Assert.Equal("object", schema.GetProperty("type").GetString()); Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
                Assert.Equal(new[] { "id", "remark", "flag", "normal" }, schema.GetProperty("properties").EnumerateObject().Select(field => field.Name));
                Assert.Contains("null", schema.GetProperty("properties").GetProperty("remark").GetProperty("type").EnumerateArray().Select(value => value.GetString()));
                var checkbox = schema.GetProperty("properties").GetProperty("flag").GetProperty("anyOf").EnumerateArray().ToArray();
                Assert.Equal(new[] { 0, 1 }, checkbox.Single(value => value.GetProperty("type").GetString() == "integer").GetProperty("enum").EnumerateArray().Select(value => value.GetInt32()));
                Assert.Equal(new[] { "boolean", "null" }, schema.GetProperty("properties").GetProperty("normal").GetProperty("type").EnumerateArray().Select(value => value.GetString()));
                Assert.Equal(new[] { "id" }, schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
            }
            finally { Directory.Delete(directory, true); }
        }
        private sealed class Authorization : IRuntimeAuthorization
        {
            /// <summary>固定目录测试身份，不接收客户端身份。</summary>
            public RuntimeActor RequireActor() => new(1, "只读合同测试", true, "77", "1");
            /// <summary>本例只检查真实 schema 发现，权限过滤由既有真实套件覆盖。</summary>
            public void RequirePagePermission(string pageId, string operation) { }
        }
    }
}

namespace Metarill.Agent.Tests.Generated.Controllers
{
    [ApiController, Route("api/admin/auto/legacy_fixture/101"), GeneratedAgentWriteAccess("77","101","1",typeof(LegacyFixtureEntity),typeof(LegacyFixtureReader),nameof(LegacyFixtureReader.ReadAgentRecord101Async))]
    public sealed class LegacyFixtureController(LegacyFixtureReader reader) : ControllerBase
    {
        /// <summary>测试实际 MVC 对兼容入口的发现，不触发业务写入。</summary>
        [HttpPost("update"), LegacyPageWriteRequest(typeof(LegacyFixtureUpdateRequest))]
        public R<string> Update([FromBody] JsonElement entity) { _=reader;return R<string>.Success(); }
    }
    /// <summary>只供schema合同证明，测试不伪造或执行实际数据scope读取。</summary>
    public sealed class LegacyFixtureReader
    {
        public Task<LegacyFixtureEntity?> ReadAgentRecord101Async(long id,string? parentId,string operation,RuntimeActor actor,IRuntimeAuthorization authorization,CancellationToken token)=>throw new NotSupportedException("schema夹具不执行记录查询。");
    }
    public sealed class LegacyFixtureEntity { [JsonPropertyName("id")]public long Id{get;set;} }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class LegacyFixtureUpdateRequest
    {
        public long Id { get; set; }
        public Optional<string?> Remark { get; set; }
        [JsonConverter(typeof(CheckboxBooleanJsonConverter))] public Optional<bool?> Flag { get; set; }
        public Optional<bool?> Normal { get; set; }
    }
}
