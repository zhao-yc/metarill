using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>固定CheckBox字段沿用原数字模型；省略、清空与普通bool均保留各自边界。</summary>
public sealed class CheckboxBooleanContractTests
{
    [Theory]
    [InlineData("0", false)] [InlineData("1", true)] [InlineData("true", true)] [InlineData("false", false)]
    [InlineData("\"0\"", false)] [InlineData("\"1\"", true)] [InlineData("null", null)] [InlineData("\"  \"", null)]
    public void Fixed_checkbox_accepts_only_explicit_values_without_losing_presence(string json, bool? expected)
    {
        var request = JsonSerializer.Deserialize<Request>("{\"flag\":" + json + "}")!;
        Assert.True(request.Flag.IsSpecified); Assert.Equal(expected, request.Flag.Value);
        using var element = JsonDocument.Parse(json); Assert.Equal(expected, CheckboxBooleanJsonConverter.Parse(element.RootElement));
        Assert.False(JsonSerializer.Deserialize<Request>("{}")!.Flag.IsSpecified);
    }

    [Theory]
    [InlineData("2")] [InlineData("-1")] [InlineData("-0")] [InlineData("1.0")] [InlineData("0.0")] [InlineData("1e0")]
    [InlineData("9007199254740993")] [InlineData("1.0000000000000000000000001")]
    [InlineData("{}")] [InlineData("[]")] [InlineData("\"true\"")] [InlineData("\" 1\"")]
    public void Fixed_checkbox_rejects_objects_and_numeric_coercion(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Request>("{\"flag\":" + json + "}"));
        using var element = JsonDocument.Parse(json); Assert.Throws<JsonException>(() => CheckboxBooleanJsonConverter.Parse(element.RootElement));
    }

    [Fact]
    public void Checkbox_output_is_numeric_while_ordinary_bool_stays_boolean()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new Response { Flag = true, Normal = true }));
        Assert.Equal(1, json.RootElement.GetProperty("Flag").GetInt32()); Assert.True(json.RootElement.GetProperty("Normal").GetBoolean());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Request>("{\"normal\":1}"));
        Assert.Null(JsonSerializer.Deserialize<Response>("{\"Flag\":null}")!.Flag);
    }

    [Fact]
    public async Task Actual_generated_dto_vo_and_original_component_preserve_checkbox_numeric_contract()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-checkbox-contract"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(Catalog(), Path.Combine(directory, "generated"), "Example.Checkbox");
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var dto = assembly.GetType("Example.Checkbox.Generated.DTOs.CustomerPage101UpdateRequest", true)!;
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); options.Converters.Add(new StringLongJsonConverter());
            var omitted = JsonSerializer.Deserialize("{\"id\":\"7\"}", dto, options)!;
            Assert.False(ReadFlag(dto, omitted, "Flag").IsSpecified);
            foreach (var (raw, expected) in new[] { ("0", (bool?)false), ("1", true), ("null", null) })
            {
                var value = JsonSerializer.Deserialize("{\"id\":\"7\",\"flag\":" + raw + ",\"overlayFlag\":" + raw + "}", dto, options)!;
                Assert.True(ReadFlag(dto, value, "Flag").IsSpecified); Assert.Equal(expected, ReadFlag(dto, value, "Flag").Value); Assert.Equal(expected, ReadFlag(dto, value, "OverlayFlag").Value);
            }
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"id\":\"7\",\"normalFlag\":1}", dto, options));
            Assert.NotNull(dto.GetProperty("Flag")!.GetCustomAttribute<JsonConverterAttribute>()); Assert.Null(dto.GetProperty("NormalFlag")!.GetCustomAttribute<JsonConverterAttribute>());
            var entityType = assembly.GetType("Example.Checkbox.Generated.Entities.Customer", true)!; var entity = Activator.CreateInstance(entityType)!;
            entityType.GetProperty("Id")!.SetValue(entity, 7L); entityType.GetProperty("Name")!.SetValue(entity, "原复选框");
            entityType.GetProperty("Flag")!.SetValue(entity, true); entityType.GetProperty("OverlayFlag")!.SetValue(entity, false); entityType.GetProperty("NormalFlag")!.SetValue(entity, true);
            var voType = assembly.GetType("Example.Checkbox.Generated.VOs.CustomerPage101Vo", true)!;
            var vo = voType.GetMethod("FromEntity")!.Invoke(null, [entity])!; var response = JsonSerializer.SerializeToElement(vo, voType, options);
            Assert.Equal(1, response.GetProperty("flag").GetInt32()); Assert.Equal(0, response.GetProperty("overlayFlag").GetInt32()); Assert.True(response.GetProperty("normalFlag").GetBoolean());
            var repository = Repository();
            var payload = Path.Combine(directory, "response.json"); await File.WriteAllTextAsync(payload, response.GetRawText());
            var frontend = Directory.GetFiles(Path.Combine(directory, "generated/frontend/pages/yzp"), "*-detail-*.vue", SearchOption.AllDirectories).Single();
            var start = new ProcessStartInfo("node") { WorkingDirectory = repository, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { Path.Combine(repository, "backend/tests/Metarill.Generation.Tests/GeneratedCheckboxVueProbe.mjs"), frontend, payload }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; }
            Assert.True(process.ExitCode == 0, await stdout + await stderr);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>同表两个固定CheckBox共用数字边界，普通bool不附转换器。</summary>
    internal static MetadataCatalog Catalog()
    {
        var catalog = GeneratorTests.Catalog();
        foreach (var (id, name, kind) in new[] { ("16", "flag", "CheckBox"), ("17", "normal_flag", ""), ("18", "overlay_flag", "CheckBox") })
        {
            catalog.ConfigColumns.Add(new() { Id = id, ConfigTableId = "1", ColumnName = name, LogicalType = "bool", Name = name, ControlKind = kind });
            var field = new PageColumnMetadata { Id = "1" + id, ConfigPageId = "101", ConfigColumnId = id, IsEdit = 1, IsShow = 1, Extra = new() { ["isSearch"] = JsonSerializer.SerializeToElement(1) } };
            catalog.ConfigPageColumns.Add(field);
        }
        return catalog;
    }
    /// <summary>反射读取的是实际生成的Optional，而非镜像DTO。</summary>
    private static Optional<bool?> ReadFlag(Type type, object value, string property) => (Optional<bool?>)type.GetProperty(property)!.GetValue(value)!;
    /// <summary>定位本测试所在的源码克隆，避免混用其他版本的运行时。</summary>
    private static string Repository([CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../.."));
    private sealed class Request
    {
        [JsonPropertyName("flag"), JsonConverter(typeof(CheckboxBooleanJsonConverter))] public Optional<bool?> Flag { get; set; }
        [JsonPropertyName("normal")] public Optional<bool?> Normal { get; set; }
    }
    private sealed class Response
    {
        [JsonConverter(typeof(CheckboxBooleanJsonConverter))] public bool? Flag { get; set; }
        public bool? Normal { get; set; }
    }
}
