using System.Text.Json;
using System.Text.Json.Nodes;

namespace Metarill.Generation;

internal sealed record MultiSelectModel(ColumnModel Source, TableModel Target, PageModel TargetPage, ColumnModel Name, ColumnModel? Names,
    bool SameParent, string? TargetParentField, IReadOnlyList<TableModel> TargetParents, string ButtonText);
internal sealed record MultiSelectResult(MultiSelectModel? Definition, string? Reason);

/// <summary>编译可证明的冗余与非冗余多选；未覆盖的范围和派生名称不能静默标为支持。</summary>
internal static class MultiSelectCompilation
{
    /// <summary>辅助名称只读且使用HTTP数组，实体仍保持原JSON文本存储。</summary>
    internal static GenerationModel Prepare(GenerationModel model)
    {
        var managed = model.Tables.ToDictionary(table => table.Source.Id, table => table.Pages.SelectMany(page => ForService(model, table, page)).Where(def => def.Names is not null).Select(def => def.Names!.Source.Id).ToHashSet(StringComparer.Ordinal));
        return model with { Tables = model.Tables.Select(table =>
        {
            var names = managed[table.Source.Id];names.UnionWith(MultiNameWriteCompilation.ForTable(model,table).Select(binding=>binding.Cache.Source.Id));
            ColumnModel Fix(ColumnModel column) => names.Contains(column.Source.Id) ? column with { IsManagedMultiNames = true } : column;
            IReadOnlyList<ColumnModel> FixAll(IReadOnlyList<ColumnModel> columns) => columns.Select(Fix).ToArray();
            return table with { Columns = FixAll(table.Columns), Pages = table.Pages.Select(page => page with { Readable = FixAll(page.Readable), Writable = FixAll(page.Writable.Where(column => !names.Contains(column.Source.Id)).ToArray()), Searchable = FixAll(page.Searchable), Sortable = FixAll(page.Sortable), ListColumns = FixAll(page.ListColumns), DetailColumns = FixAll(page.DetailColumns) }).ToArray() };
        }).ToArray() };
    }

    /// <summary>来源和目标均绑定稳定元数据ID，客户端不能替换表或页面。</summary>
    internal static IReadOnlyList<MultiSelectModel> ForPage(GenerationModel model, TableModel table, PageModel page) => page.Readable.Concat(page.Writable).DistinctBy(column => column.Source.Id)
        .Where(column => column.Source.ControlKind == "MultiSelectPage").Select(column => Resolve(model, table, page, column).Definition).OfType<MultiSelectModel>().ToArray();

    /// <summary>服务规则覆盖表级关系，隐藏字段仍需在复制或父依赖变化时验证；不会因此下发额外VO字段。</summary>
    internal static IReadOnlyList<MultiSelectModel> ForService(GenerationModel model, TableModel table, PageModel page) => table.Columns.Where(column => column.Source.ControlKind == "MultiSelectPage")
        .Select(column => Resolve(model, table, page, column, forService: true).Definition).OfType<MultiSelectModel>().ToArray();

    /// <summary>额外SQL或未知父映射返回明确缺口，保留原配置而不扩大候选范围。</summary>
    internal static MultiSelectResult Resolve(GenerationModel model, TableModel table, PageModel page, ColumnModel source, bool forService = false, bool rootDefinition = false)
    {
        MultiSelectResult Gap(string reason) => new(null, "MultiSelectPage尚未支持：" + reason);
        if (table.Source.IsRongyu is not (0 or 1)) return Gap("未识别的多选存储模式。");
        if (source.Type != "string?" || !source.Source.ColumnName.EndsWith("_ids", StringComparison.Ordinal)) return Gap("字段必须为已声明的字符串_ids存储。");
        if (!forService && !page.Readable.Any(column => column.Source.Id == source.Source.Id)) return Gap("来源页未开放ID字段，无法形成完整字段合同。");
        try
        {
            var root = Read(source.Source.ControlDetail);
            var field = rootDefinition ? null : model.Catalog.ConfigPageColumns.SingleOrDefault(candidate => candidate.ConfigPageId == page.Source.Id && candidate.ConfigColumnId == source.Source.Id);
            var overlay = Read(field?.Extra.GetValueOrDefault("controlDetail").ToString() ?? "");
            string Value(string key, string fallback = "") { var value = Scalar(overlay, key); if (string.IsNullOrWhiteSpace(value)) value = Scalar(root, key); return string.IsNullOrWhiteSpace(value) ? fallback : value; }
            var pageId = Value("pageId", source.Source.Extra.GetValueOrDefault("linkConfigPageId").ToString());
            var target = model.Tables.SingleOrDefault(candidate => candidate.Pages.Any(targetPage => targetPage.Source.Id == pageId));
            var targetPage = target?.Pages.Single(candidate => candidate.Source.Id == pageId);
            if (target is null || targetPage is null || !SelectionPageRenderer.Enabled(targetPage)) return Gap("目标必须是已声明的type=2多选页面。");
            if (!string.IsNullOrWhiteSpace(source.Source.LinkConfigTableId) && source.Source.LinkConfigTableId != target.Source.Id) return Gap("目标页与声明关联表不一致。");
            if (!target.IsGlobal && !target.Columns.Any(column => column.Source.ColumnName == "yz_project_id")) return Gap("目标未声明项目归属或明确全局范围。");
            var name = target.Columns.SingleOrDefault(column => column.Source.ColumnName == Value("nameField", "name"));
            // 只为非物理R0名称接新证明，R1物理Join缓存保留原门禁。
            if (name is null || GenerationModel.Enabled(name.Source.Extra,"isJoinColumn")&&!DerivedMultiNamesCompilation.Declared(model,target,name) || name.IsJsonArray || name.Type != "string?" && !name.IsId || !targetPage.Readable.Any(column => column.Source.Id == name.Source.Id)) return Gap("名称字段不在目标页可读投影或类型未支持。");
            if (ManagerCredentialCompilation.IsCredential(target,name)) return Gap("账号凭据不能作为多选名称读取列。");
            if(DerivedMultiNamesCompilation.Declared(model,target,name))DerivedMultiNamesCompilation.Require(model,table,target,targetPage,name);
            if(!FixedPageQueryCompilation.TryProveSelection(FixedPageQueryCompilation.For(target,targetPage),new(pageId,name.Source.ColumnName,Value("buttonText"),Value("whereSql"),""),out _))return Gap("额外控件范围需要固定来源字段读取协议，不能使用宽范围目标页代替。");
            var ordering = Value("orderBy");
            if (!string.IsNullOrWhiteSpace(ordering) && !GenerationModel.ValidateOrderBy(ordering, target.Columns).Select(item => (item.Column.Source.Id, item.Descending)).SequenceEqual(GenerationModel.ValidateOrderBy(targetPage.Source.OrderBy, target.Columns).Select(item => (item.Column.Source.Id, item.Descending)))) return Gap("控件排序与目标页不一致。");
            TableModel Parent(ColumnModel column) => model.Tables.Single(candidate => !string.IsNullOrWhiteSpace(column.Source.LinkConfigTableId) ? candidate.Source.Id == column.Source.LinkConfigTableId : candidate.Source.TableName + "_id" == column.Source.ColumnName);
            var sourceParents = page.Scopes.Where(scope => scope.Source == "parent").Select(scope => Parent(scope.Column).Source.Id).Distinct().Order().ToArray();
            var targetParents = targetPage.Scopes.Where(scope => scope.Source == "parent").Select(scope => Parent(scope.Column)).DistinctBy(parent => parent.Source.Id).ToArray();
            if (targetParents.Any(parent => !parent.Columns.Any(column => column.Source.ColumnName == "yz_project_id"))) return Gap("目标父表未声明项目归属。");
            var same = targetParents.Length > 0 && sourceParents.SequenceEqual(targetParents.Select(parent => parent.Source.Id).Order());
            string? targetField = null;
            if (targetParents.Length > 1) return Gap("多个目标父定义未形成唯一合同。");
            if (targetParents.Length == 1 && !same)
            {
                var fields = table.Columns.Where(column => column.Source.Id != source.Source.Id && column.Source.LinkConfigTableId == targetParents[0].Source.Id && !column.IsJsonArray && column.Type is "string?" or "long?").Take(2).ToArray();
                if (fields.Length != 1) return Gap("目标父缺少唯一可读的声明外键。");
                targetField = fields[0].JsonName;
                if (!forService && !page.Readable.Any(column => column.Source.Id == fields[0].Source.Id)) return Gap("来源页未开放目标父字段，前端不能猜测隐藏父值。");
            }
            ColumnModel? names = null;
            if (!name.IsId)
            {
                var nameField = name.Source.ColumnName; var auxiliary = source.Source.ColumnName[..^4] + "_" + (nameField.EndsWith('s') ? nameField : nameField + "s");
                names = table.Source.IsRongyu==0 ? SelectionDisplayFieldCompilation.Resolve(table,source) : table.Columns.SingleOrDefault(column => column.Source.ColumnName == auxiliary && column.Type == "string?" && !column.IsSystem);
                if(names is not null&&table.Source.IsRongyu==0&&(names.Source.Id==source.Source.Id||names.Type!="string?"||!GenerationModel.Enabled(names.Source.Extra,"isJoinColumn")))return Gap("非冗余名称必须为只读联接或自动派生属性。");
                if (names is null) return Gap((table.Source.IsRongyu==0?"非冗余名称联接列尚未明确声明：":"冗余名称辅助列尚未明确声明：") + auxiliary);
                if(table.Source.IsRongyu==1&&!MultiNameStorageCompilation.Supports(model,table,source,name,names))return Gap("冗余名称缓存没有绑定同一来源ID和目标标量路径。");
                if (!forService && table.Source.IsRongyu==1 && !MultiNameStorageCompilation.IsImplicit(model,names) && !page.Readable.Any(column => column.Source.Id == names.Source.Id)) return Gap("冗余名称辅助列未在来源页开放读取。");
            }
            if(table.Source.IsRongyu==0)
            {
                var baselineName=Scalar(root,"nameField");if(string.IsNullOrWhiteSpace(baselineName))baselineName="name";
                if(ForeignValidationRenderer.Target(model,source)?.Source.Id!=target.Source.Id||baselineName!=name.Source.ColumnName)return Gap("页级目标或名称覆盖尚未绑定同一非冗余读取计划。");
                if(!NonRedundantReadCompilation.Physical(model,table,source)||target.IsGlobal)return Gap("非冗余多选要求同项目物理ID和标量名称。");
                if(targetField is not null&&!NonRedundantReadCompilation.Physical(model,table,table.Columns.Single(column=>column.JsonName==targetField)))return Gap("非冗余目标父必须为物理外键。");
                if(names is not null)Metarill.Data.Queries.NonRedundantReads.Compile(model.Catalog,table.Source.Id,[names.Source.ColumnName]);
            }
            return new(new(source, target, targetPage, name, names, same, targetField, targetParents, Value("buttonText", "选择")), null);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException) { return Gap("配置或范围无法安全编译：" + error.Message); }
    }

    /// <summary>重复键、数组配置或复杂映射值必须拒绝，未知合法键保持在原元数据。</summary>
    private static JsonObject Read(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new();
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || doc.RootElement.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != doc.RootElement.EnumerateObject().Count()) throw new InvalidOperationException("控件明细必须为无重复键对象。");
        return JsonNode.Parse(text)!.AsObject();
    }
    /// <summary>只解释原协议中的标量配置，不执行脚本或SQL。</summary>
    private static string Scalar(JsonObject source, string key) => source[key] is null ? "" : source[key]!.GetValueKind() is JsonValueKind.String or JsonValueKind.Number ? source[key]!.ToString() : throw new InvalidOperationException("控件映射字段必须为标量。");
}
