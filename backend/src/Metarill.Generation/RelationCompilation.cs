using System.Text.Json;

namespace Metarill.Generation;

internal sealed record RelationModel(ColumnModel SourceColumn, TableModel Target, PageModel TargetPage, ColumnModel NameColumn, string Endpoint, IReadOnlyList<(ColumnModel Column, string Source)> Scopes);

/// <summary>关系选项只能编译已声明表、名称列和目标页面，禁止直接解释外部 SQL。</summary>
internal static class RelationCompilation
{
    public static IReadOnlyList<RelationModel> ForPage(GenerationModel model, PageModel page)
    {
        var relations = new List<RelationModel>();
        foreach (var source in page.Readable.Concat(page.Writable).DistinctBy(x => x.Source.Id).Where(c => c.Source.ControlKind == "List"))
        {
            if (string.IsNullOrWhiteSpace(source.Source.ControlDetail)) continue;
            using var detail = JsonDocument.Parse(source.Source.ControlDetail);
            var root = detail.RootElement;
            var tableId = root.TryGetProperty("tableId", out var tableValue) ? tableValue.ToString() : source.Source.LinkConfigTableId;
            var tableName = root.TryGetProperty("table", out var nameValue) ? nameValue.ToString() : "";
            var target = model.Tables.SingleOrDefault(t => !string.IsNullOrEmpty(tableId) ? t.Source.Id == tableId : t.Source.TableName == tableName);
            if (target is null) continue; // 保留尚未移植的外部关系配置，能力清单明确列出未接入字段。
            var nameField = root.TryGetProperty("nameField", out var fieldValue) ? fieldValue.ToString() : "name";
            var name = target.Columns.SingleOrDefault(c => c.Source.ColumnName == nameField);
            if (name is null || name.Type != "string?") continue;
            var targetPage = target.Pages.FirstOrDefault(p => p.Source.YzAdminTypeId == page.Source.YzAdminTypeId && !p.Scopes.Any(s => s.Source == "parent"))
                ?? target.Pages.FirstOrDefault(p => !p.Scopes.Any(s => s.Source == "parent"));
            if (targetPage is null) continue;
            var sql = root.TryGetProperty("whereSql", out var sqlValue) ? sqlValue.ToString() : "";
            var scopes = GenerationModel.CompileScopes(sql, target.Columns);
            relations.Add(new RelationModel(source, target, targetPage, name, "findNameId" + target.Name + source.Source.Id, scopes));
        }
        return relations;
    }
}
