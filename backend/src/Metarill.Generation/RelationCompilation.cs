using System.Text.Json;
using System.Text.Json.Nodes;

namespace Metarill.Generation;

internal sealed record RelationModel(ColumnModel SourceColumn, TableModel Target, PageModel TargetPage, ColumnModel NameColumn, string Endpoint,
    IReadOnlyList<(ColumnModel Column, string Source)> Scopes, IReadOnlyList<TableModel> SourceParents, IReadOnlyList<TableModel> TargetParents,
    FixedParentRelationResult? ParentBinding = null, IReadOnlyList<(ColumnModel Column, bool Descending)>? OptionsOrder = null, RelationConditionModel? Conditions = null)
{
    /// <summary>原 V8 路由按来源字段去掉 Id/Ids 后缀，不能用目标表名替代业务别名。</summary>
    public string LegacyEndpoint
    {
        get
        {
            var name = SourceColumn.Property;
            if (name.EndsWith("Ids", StringComparison.Ordinal)) name = name[..^3];
            else if (name.EndsWith("Id", StringComparison.Ordinal)) name = name[..^2];
            return "findNameId" + name + SourceColumn.Source.Id;
        }
    }
}

/// <summary>关系选项只能编译已声明表、名称列和目标页面，禁止直接解释外部 SQL。</summary>
internal static class RelationCompilation
{
    /// <summary>把字段与页面覆盖合并后编译一次，三种候选入口共用同一范围。</summary>
    public static IReadOnlyList<RelationModel> ForPage(GenerationModel model, PageModel page, bool forService = false)
    {
        var relations = new List<RelationModel>();
        var owner=model.Tables.Single(table=>table.Source.Id==page.Source.ConfigTableId);
        // 系统项目上下文保留既有List等接线，新缓存能力不额外追加隐藏候选或授权依赖。
        var candidates = forService ? model.Tables.Single(table => table.Source.Id == page.Source.ConfigTableId).Columns.Where(column=>FixedParentRelationCompilation.HasDeclaration(column)||owner.Source.IsRongyu==0&&column.Source.ControlKind=="SelectPage"||!column.IsSystem&&(DerivedRelationNamesCompilation.DeclaredSource(model,column)||(column.Source.ControlKind is "List" or "SelectPage")&&ForeignValidationRenderer.Target(model,column)?.IsGlobal==true)) : page.Readable.Concat(page.Writable).DistinctBy(x => x.Source.Id);
        foreach (var source in candidates.Where(c => (c.Source.ControlKind == "List" || (owner.Source.IsRongyu==0||!c.IsSystem&&(ForeignValidationRenderer.Target(model,c)?.IsGlobal==true||DerivedRelationNamesCompilation.DeclaredSource(model,c)))&&c.Source.ControlKind=="SelectPage") && !FixedOptionCatalogCompilation.Resolve(model, page, c).Declared))
        {
            var field = model.Catalog.ConfigPageColumns.SingleOrDefault(candidate => candidate.ConfigPageId == page.Source.Id && candidate.ConfigColumnId == source.Source.Id);
            var root = Definition(source.Source.ControlDetail);
            var overlay = Definition(field?.Extra.GetValueOrDefault("controlDetail").ToString() ?? "");
            // 仅编译已知映射键，原元数据和未知合法键保持原样；页面非空配置覆盖字段定义。
            string Mapped(string key, string fallback = "")
            {
                var value = Text(overlay, key); if (string.IsNullOrWhiteSpace(value)) value = Text(root, key);
                return string.IsNullOrWhiteSpace(value) ? fallback : value;
            }
            var tableId = Mapped("tableId", source.Source.LinkConfigTableId); var tableName = Mapped("table");
            // 非冗余单选的弹层、导入/导出与辅助接口必须使用同一已证明目标页。
            var selection=source.Source.ControlKind=="SelectPage"?NonRedundantSelectionCompilation.Resolve(model,owner,page,source):null;
            var target = selection?.Target ?? model.Tables.SingleOrDefault(t => !string.IsNullOrEmpty(tableId) ? t.Source.Id == tableId : t.Source.TableName == tableName);
            if (target is null) continue; // 保留尚未移植的外部关系配置，能力清单明确列出未接入字段。
            if (!target.IsGlobal && !target.Columns.Any(column => column.Source.ColumnName == "yz_project_id"))
                throw new InvalidOperationException("关联目标未声明项目归属或明确的全局范围：" + target.Source.TableName);
            var name = selection?.Name ?? target.Columns.SingleOrDefault(c => c.Source.ColumnName == Mapped("nameField", "name"));
            if (name is null || name.Type != "string?") continue;
            if (ManagerCredentialCompilation.IsCredential(target,name)) throw new InvalidOperationException("账号凭据不能作为关系名称、候选或导出读取列。");
            var explicitPage = source.Source.Extra.GetValueOrDefault("linkConfigPageId").ToString();
            var targetPage = selection?.TargetPage ?? (!string.IsNullOrWhiteSpace(explicitPage)
                ? target.Pages.SingleOrDefault(candidate => candidate.Source.Id == explicitPage) ?? throw new InvalidOperationException("关联目标页不存在或不属于目标表。")
                : target.Pages.FirstOrDefault(p => p.Source.YzAdminTypeId == page.Source.YzAdminTypeId && !p.Scopes.Any(s => s.Source == "parent"))
                    ?? target.Pages.FirstOrDefault(p => !p.Scopes.Any(s => s.Source == "parent")));
            if(target.IsGlobal&&(owner.Source.IsRongyu==0||!source.IsSystem))
            {
                var global=owner.Source.IsRongyu==1?Metarill.Core.Metadata.GlobalSingleRelationTarget.RequireCached(model.Catalog,owner.Source,source.Source,target.Source):Metarill.Core.Metadata.GlobalSingleRelationTarget.Require(model.Catalog,owner.Source,source.Source,target.Source);targetPage=target.Pages.Single(candidate=>candidate.Source.Id==global.Page.Id);if(name.Source.Id!=global.Name.Id)throw new InvalidOperationException("全局关系页级名称覆盖与固定显示来源不一致。");
            }
            if (targetPage is null) continue;
            if (!targetPage.Readable.Any(column => column.Source.Id == name.Source.Id)) throw new InvalidOperationException("关联名称列未在目标页开放读取。");
            var (scopes, conditions) = RelationConditionsCompilation.Compile(model, owner, page, target, Mapped("whereSql"));
            if (conditions is not null && ForeignValidationRenderer.Target(model,source)?.Source.Id != target.Source.Id)
                throw new InvalidOperationException("常量候选必须与外键及显示来源的固定目标一致。");
            var sourceParents = page.Scopes.Where(scope => scope.Source == "parent").Select(scope => Parent(model, scope.Column, owner, page)).DistinctBy(parent => parent.Source.Id).ToArray();
            var targetParents = targetPage.Scopes.Concat(scopes).Where(scope => scope.Source == "parent").Select(scope => Parent(model, scope.Column)).DistinctBy(parent => parent.Source.Id).ToArray();
            var binding = FixedParentRelationCompilation.Resolve(model, page, source, sourceParents, targetParents, targetPage.Scopes.Concat(scopes).ToArray());
            IReadOnlyList<(ColumnModel Column, bool Descending)>? ordering = null;
            if (binding.Binding is not null && !string.IsNullOrWhiteSpace(Mapped("orderBy")))
            {
                try { var order = GenerationModel.ValidateOrderBy(Mapped("orderBy"), target.Columns).ToList(); if (!order.Any(item => item.Column.IsId)) order.Add((target.Columns.Single(column => column.IsId), false)); ordering = order; }
                catch (InvalidOperationException) { binding = new(true, null, "固定归属候选排序不能安全编译。"); }
            }
            var relation=new RelationModel(source,target,targetPage,name,"findNameId"+target.Name+source.Source.Id,scopes,sourceParents,targetParents,binding,ordering,conditions);
            if(DerivedRelationNamesCompilation.Declared(model,relation))_=DerivedRelationNamesCompilation.Require(model,owner,page,relation);
            relations.Add(relation);
        }
        return relations;
    }

    /// <summary>父范围必须引用真实已声明的表与项目键，不能按请求选择父定义。</summary>
    private static TableModel Parent(GenerationModel model, ColumnModel column, TableModel? owner=null, PageModel? page=null)
    {
        // 隐式跨表父位沿已证明读取链解析，不能靠列名前缀猜目标表。
        var cross=owner is not null&&page is not null?NonRedundantCrossParentCompilation.Find(model,owner,page,column):null;
        var parent = (cross is{} path?path.Read.Levels[path.Index].Target:model.Tables.SingleOrDefault(candidate => !string.IsNullOrWhiteSpace(column.Source.LinkConfigTableId)
            ? candidate.Source.Id == column.Source.LinkConfigTableId : candidate.Source.TableName + "_id" == column.Source.ColumnName))
            ?? throw new InvalidOperationException("关联父范围缺少明确目标表：" + column.Source.ColumnName);
        if (!parent.Columns.Any(candidate => candidate.Source.ColumnName == "yz_project_id")) throw new InvalidOperationException("关联父表未声明项目归属。");
        return parent;
    }

    /// <summary>只接受对象，重复键不得导致不同入口采用不同配置解释。</summary>
    private static JsonObject Definition(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new();
        using var parsed = JsonDocument.Parse(text);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object || parsed.RootElement.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != parsed.RootElement.EnumerateObject().Count())
            throw new InvalidOperationException("关联控件明细必须为无重复键的对象。");
        return JsonNode.Parse(text)!.AsObject();
    }
    /// <summary>映射键只接受标量字符串/数字，不把复杂对象转成 SQL。</summary>
    private static string Text(JsonObject source, string key) => source[key] is null ? "" : source[key]!.GetValueKind() is JsonValueKind.String or JsonValueKind.Number
        ? source[key]!.ToString() : throw new InvalidOperationException("关联控件映射字段类型无效。");
}
