using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>同一份页级写入定义供生成页面和旧前端请求适配，后端仍严格绑定。</summary>
internal static class WriteSchemaRenderer
{
    /// <summary>生成静态客户端清单及宿主可直接公开的类型化字典。</summary>
    public static IEnumerable<PlannedFile> Render(GenerationModel model)
    {
        var pages = model.Tables.SelectMany(table => table.Pages.Select(page => new
        {
            page.Source.Id,
            Route = "/admin/auto/" + table.Source.TableName + "/" + page.Source.Id,
            CreateFields = page.Writable.Select(column => column.JsonName).ToArray(),
            UpdateFields = new[] { "id" }.Concat(page.Writable.Select(column => column.JsonName)).ToArray()
        })).OrderBy(page => page.Id, StringComparer.Ordinal).ToArray();
        var json = JsonSerializer.Serialize(new { schemaVersion = 1, pages = pages.ToDictionary(page => page.Id, page => new { route = page.Route, createFields = page.CreateFields, updateFields = page.UpdateFields }) }, new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" });
        yield return new("frontend/common/yzp_runtime/page-write-schema.generated.json", json + "\n");
        var code = new StringBuilder(CSharpGenerator.Header).AppendLine($"namespace {model.Namespace}.Generated;")
            .AppendLine("public sealed record PageWriteSchema(string Route, IReadOnlyList<string> CreateFields, IReadOnlyList<string> UpdateFields);")
            .AppendLine("public static class GeneratedWriteSchema").AppendLine("{")
            .AppendLine("    public const int SchemaVersion = 1;")
            .AppendLine("    public static IReadOnlyDictionary<string, PageWriteSchema> Pages { get; } = new Dictionary<string, PageWriteSchema>").AppendLine("    {");
        foreach (var page in pages)
            code.AppendLine($"        [{JsonSerializer.Serialize(page.Id)}] = new({JsonSerializer.Serialize(page.Route)}, [{string.Join(", ", page.CreateFields.Select(value => JsonSerializer.Serialize(value)))}], [{string.Join(", ", page.UpdateFields.Select(value => JsonSerializer.Serialize(value)))}]),");
        yield return new("GeneratedWriteSchema.g.cs", code.AppendLine("    };").AppendLine("}").ToString());
    }
}
