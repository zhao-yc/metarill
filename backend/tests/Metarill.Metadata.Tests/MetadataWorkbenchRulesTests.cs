using System.Text.Json;
using Metarill.Runtime.Metadata;
using Xunit;

namespace Metarill.Metadata.Tests;

public sealed class MetadataWorkbenchRulesTests
{
    private static JsonElement Json(string value) => JsonSerializer.Deserialize<JsonElement>(value);
    /// <summary>验证严格布局协议拒绝省略、null、重复标识与失效归属。</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"version\":1,\"mode\":\"tabs\",\"groups\":null}")]
    [InlineData("{\"version\":1,\"mode\":\"tabs\"}")]
    [InlineData("{\"version\":1,\"mode\":\"tabs\",\"groups\":[{\"key\":\"a\",\"name\":\"甲\"},{\"key\":\"a\",\"name\":\"乙\"}]}")]
    [InlineData("{\"version\":1,\"mode\":\"tabs\",\"groups\":[{\"key\":\"a\",\"name\":\"甲\"},{\"key\":\"b\",\"name\":\" 甲 \"}]}")]
    [InlineData("{\"version\":1,\"mode\":\"tabs\",\"groups\":[],\"sql\":\"DELETE\"}")]
    public void Layout_rejects_missing_null_duplicate_and_unknown(string body) => Assert.Throws<InvalidOperationException>(() => MetadataWorkbenchRules.Layout(Json(body)));

    /// <summary>停用布局保留定义，显式空归属表示常显，不隐式创建标签。</summary>
    [Fact]
    public void Layout_save_preserves_none_groups_and_requires_explicit_assignments()
    {
        var body = "{\"pageId\":\"42\",\"revision\":\"" + new string('a',64) + "\",\"detailLayout\":{\"version\":1,\"mode\":\"none\",\"groups\":[{\"key\":\"base_1\",\"name\":\" 基础 \"}]},\"fields\":[{\"pageColumnId\":\"51\",\"detailGroupKey\":\"\"}]}";
        var saved = MetadataWorkbenchRules.LayoutSave(Json(body));
        Assert.Equal("none",saved.Layout.Mode); Assert.Equal("基础",saved.Layout.Groups.Single().Name); Assert.Equal("",saved.Fields[51]);
        Assert.Throws<InvalidOperationException>(()=>MetadataWorkbenchRules.LayoutSave(Json(body.Replace("\"detailGroupKey\":\"\"","\"detailGroupKey\":null"))));
        Assert.Throws<InvalidOperationException>(()=>MetadataWorkbenchRules.LayoutSave(Json(body.Replace("\"detailGroupKey\":\"\"","\"detailGroupKey\":\"missing\""))));
        Assert.Throws<InvalidOperationException>(()=>MetadataWorkbenchRules.LayoutSave(Json(body.Replace("\"fields\":[{\"pageColumnId\":\"51\",\"detailGroupKey\":\"\"}]","\"fields\":null"))));
    }

    /// <summary>批量标志仅写明确提交字段；null不能变成默认关闭，未知系统字段不能穿透。</summary>
    [Fact]
    public void Field_patch_preserves_omission_and_rejects_null_system_fields()
    {
        var patch=MetadataWorkbenchRules.PageFieldPatches(Json("[{\"id\":\"51\",\"isInEdit\":false,\"isMust\":\"1\"}]")).Single();
        Assert.Equal(0,patch.Values["is_in_edit"]); Assert.Equal(1,patch.Values["is_must"]); Assert.False(patch.Values.ContainsKey("is_in_grid"));
        foreach(var body in new[]{"[{\"id\":51,\"isInEdit\":null}]","[{\"id\":51,\"insertTime\":\"2026-01-01\"}]","[{\"id\":51,\"configPageId\":52}]","[{\"id\":51,\"isInGrid\":2}]","[{\"id\":51},{\"id\":51}]","[{\"id\":51,\"detailGroupKey\":null}]"})
            Assert.Throws<InvalidOperationException>(()=>MetadataWorkbenchRules.PageFieldPatches(Json(body)));
    }

    /// <summary>排序兼容输入框数字字符串，拒绝小数、越界、null及额外属性。</summary>
    [Theory]
    [InlineData("[{\"id\":51,\"sortNo\":null}]")]
    [InlineData("[{\"id\":51}]")]
    [InlineData("[{\"id\":51,\"sortNo\":1.5}]")]
    [InlineData("[{\"id\":51,\"sortNo\":2147483648}]")]
    [InlineData("[{\"id\":51,\"sortNo\":1,\"deleteNo\":null}]")]
    public void Sort_rejects_invalid_values(string body) => Assert.Throws<InvalidOperationException>(()=>MetadataWorkbenchRules.ColumnOrderPatches(Json(body)));

    /// <summary>显式顺序解析不执行表达式，并要求有效快照版本。</summary>
    [Fact]
    public void Sort_accepts_integer_text_and_revision_is_explicit()
    {
        Assert.Equal(-3,MetadataWorkbenchRules.ColumnOrderPatches(Json("[{\"id\":\"51\",\"sortNo\":\"-3\"}]")).Single().Values["sort_no"]);
        Assert.Throws<InvalidOperationException>(()=>MetadataWorkbenchRules.ColumnOrderPatches(Json("[{\"id\":51,\"sortNo\":\"1+1\"}]")));
        Assert.Throws<InvalidOperationException>(()=>MetadataWorkbenchRules.Revision(null));
        Assert.Throws<InvalidOperationException>(()=>MetadataWorkbenchRules.Revision("过期"));
    }
}
