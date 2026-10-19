using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>完整实体回显与可写 DTO 分离，未知字段和身份/父范围仍严格拒绝。</summary>
public sealed class LegacyPageWriteContractTests
{
    private static readonly LegacyPageWriteContract Contract = new(["name", "remark"], ["id", "name", "remark", "statusShow", "readonlyCode", "targetNames"], ["parentId"], "yzProjectId");
    /// <summary>保留原始属性顺序与重复键供边界检查。</summary>
    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);

    [Fact]
    public void Full_entity_echo_keeps_only_writable_presence_and_never_accepts_client_names()
    {
        var result = Contract.Normalize(Json("""{"id":"7","name":"新名称","remark":null,"readonlyCode":"伪造","statusShow":"标签","targetNames":["伪名称"],"parentId":"9","yzProjectId":"100"}"""), LegacyPageWriteOperation.Update, "100", "9");
        Assert.Equal(new[] { "id", "name", "remark" }, result.EnumerateObject().Select(field => field.Name));
        Assert.Equal(JsonValueKind.Null, result.GetProperty("remark").ValueKind);
        Assert.False(Contract.Normalize(Json("""{"id":"7","name":"甲"}"""), LegacyPageWriteOperation.Update, "100", "9").TryGetProperty("remark", out _));
        var deleted = Contract.Normalize(Json("""{"id":"7","name":"甲","readonlyCode":"乙","statusShow":"标签"}"""), LegacyPageWriteOperation.Delete, "100", null);
        Assert.Equal("id", Assert.Single(deleted.EnumerateObject()).Name);
    }

    [Theory]
    [InlineData("{\"id\":\"\",\"name\":\"甲\"}")]
    [InlineData("{\"id\":null,\"name\":\"甲\"}")]
    public void New_empty_id_is_ignored(string body) => Assert.False(Contract.Normalize(Json(body), LegacyPageWriteOperation.Create, "100", null).TryGetProperty("id", out _));

    [Theory]
    [InlineData("{\"id\":\"7\"}")]
    [InlineData("{\"name\":\"甲\",\"sql\":\"1=1\"}")]
    [InlineData("{\"parentId\":\"10\"}")]
    [InlineData("{\"name\":\"甲\",\"name\":\"乙\"}")]
    public void New_requests_cannot_set_id_unknown_or_cross_parent(string body) => Assert.Throws<ArgumentException>(() => Contract.Normalize(Json(body), LegacyPageWriteOperation.Create, "100", "9"));

    [Fact]
    public void False_project_and_missing_parent_context_are_rejected()
    {
        Assert.Throws<UnauthorizedAccessException>(() => Contract.Normalize(Json("""{"yzProjectId":"101"}"""), LegacyPageWriteOperation.Update, "100", null));
        Assert.Throws<ArgumentException>(() => Contract.Normalize(Json("""{"parentId":"9"}"""), LegacyPageWriteOperation.Update, "100", null));
    }

    [Fact]
    public void Original_update_flag_survives_roundtrip_despite_new_default_save_flag()
    {
        var page = JsonSerializer.Deserialize<PageMetadata>("""{"id":"1","isUpdate":0}""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(1, page.IsSave); Assert.False(PageUpdatePolicy.CanUpdate(page));
        page = JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(page), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.False(PageUpdatePolicy.CanUpdate(page));
        page.IsSave = 0; page.Extra["isUpdate"] = JsonSerializer.SerializeToElement("1"); Assert.True(PageUpdatePolicy.CanUpdate(page));
        page.Extra.Clear(); Assert.False(PageUpdatePolicy.CanUpdate(page));
        page.Extra["isUpdate"] = JsonSerializer.SerializeToElement(2); Assert.Throws<InvalidOperationException>(() => PageUpdatePolicy.CanUpdate(page));
    }

    [Fact]
    public void Original_readonly_option_id_is_bounded_and_explicit_empty_never_means_all()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Null(JsonSerializer.Deserialize<RelationOptionsLegacyRequest>("{}", options)!.ToSelectedRequest());
        foreach (var text in new[] { "{\"id\":null}", "{\"id\":\"\"}" }) Assert.Empty(JsonSerializer.Deserialize<RelationOptionsLegacyRequest>(text, options)!.ToSelectedRequest()!.SelectedIds);
        Assert.Equal(new[] { "9007199254740993" }, JsonSerializer.Deserialize<RelationOptionsLegacyRequest>("{\"id\":\"9007199254740993\"}", options)!.ToSelectedRequest()!.SelectedIds);
        Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize<RelationOptionsLegacyRequest>("{\"id\":\"1 OR 1=1\"}", options)!.ToSelectedRequest());
    }
}
