using System.Text.Json;

namespace Metarill.Generation;

internal enum ReadProjectionPurpose { List, Detail, Selection }
internal enum ReadProjectionOutputKind { Physical, ConstantShow, CatalogName, RoleNames, RoleAdminName, CredentialEcho, CascadeDisplayText }
internal sealed record ReadProjectionOutput(string JsonName, string Property, ColumnModel Source, ReadProjectionOutputKind Kind, string? ControlDetail = null);
internal sealed record ReadProjectionDependency(string ColumnId, IReadOnlyList<string> Reasons);
internal sealed record ReadProjectionGap(string ColumnId, string Reason);
internal sealed record ReadProjectionPlan(ReadProjectionPurpose Purpose, IReadOnlyList<ColumnModel> PhysicalColumns,
    IReadOnlyList<ReadProjectionOutput> OutputFields, IReadOnlyList<ReadProjectionDependency> Dependencies, IReadOnlyList<ReadProjectionGap> Gaps)
{
    public bool CanCompile => Gaps.Count == 0;
}
internal sealed record PageReadProjections(ReadProjectionPlan List, ReadProjectionPlan Detail, ReadProjectionPlan Selection);

/// <summary>按原界面用途编译固定读取依赖；此IR不生成SQL，也不改变现有Readable或写入合同。</summary>
internal static class ReadProjectionCompilation
{
    /// <summary>列表、详情和选择确认独立规划；查询范围列不是响应授权，不自动加入输出。</summary>
    internal static PageReadProjections ForPage(GenerationModel model, TableModel table, PageModel page)
    {
        if (!model.Tables.Any(candidate => ReferenceEquals(candidate, table)) || !table.Pages.Any(candidate => ReferenceEquals(candidate, page)))
            throw new ArgumentException("用途投影必须属于同一生成模型的真实表和页面。");
        var list = new Builder(model, table, page, ReadProjectionPurpose.List);
        var detail = new Builder(model, table, page, ReadProjectionPurpose.Detail);
        var selection = new Builder(model, table, page, ReadProjectionPurpose.Selection);
        list.Base(page.ListColumns); detail.Base(page.DetailColumns); selection.Base(page.ListColumns); selection.FixedSelectionNames();
        list.Buttons(false); detail.Buttons(true); selection.Buttons(false);
        detail.DetailConditions();
        selection.InboundSelections();
        return new(list.Finish(), detail.Finish(), selection.Finish());
    }

    /// <summary>按稳定字段ID收集去重依赖，未知来源只留缺口，不能扩成全表或全Readable。</summary>
    private sealed class Builder(GenerationModel model, TableModel table, PageModel page, ReadProjectionPurpose purpose)
    {
        private readonly Dictionary<string, ColumnModel> physical = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ReadProjectionOutput> output = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> dependencies = new(StringComparer.Ordinal);
        private readonly List<ReadProjectionGap> gaps = [];
        private readonly HashSet<string> expanded = new(StringComparer.Ordinal);

        /// <summary>基础列来自用途专属集合，主键始终保留；未完成的虚拟/非冗余读计划明确拒绝。</summary>
        internal void Base(IReadOnlyList<ColumnModel> columns)
        {
            if (table.Source.IsRongyu != 1) Gap("", "非冗余用途读取计划尚未编译。");
            Add(table.Columns.Single(column => column.IsId), "记录标识、原导航和控件上下文", false);
            foreach (var column in columns) Add(column, purpose == ReadProjectionPurpose.Detail ? "详情字段" : "列表字段");
            foreach (var settings in model.Catalog.ConfigPageColumns.Where(field => field.ConfigPageId == page.Source.Id))
            {
                var source = model.Catalog.ConfigColumns.Single(column => column.Id == settings.ConfigColumnId);
                var key = purpose == ReadProjectionPurpose.Detail ? "isInEdit" : "isInGrid";
                var enabled = settings.Extra.ContainsKey(key) ? GenerationModel.Enabled(settings.Extra, key) : purpose == ReadProjectionPurpose.Detail ? settings.IsEdit == 1 : settings.IsShow == 1;
                if (enabled && source.IsVirtualColumn != 0) Gap(source.Id, "虚拟字段需要明确读取计划，不能作为物理列选择。");
            }
        }

        /// <summary>type=2仍回读原公共VO；固定名称只补本页Readable中已证明字段，不借其它页扩大读取。</summary>
        internal void FixedSelectionNames()
        {
            if (!SelectionPageRenderer.Enabled(page)) return;
            if (FixedRoleCheckboxCompilation.ForPage(model,table,page) is {} role) Add(role.Source,"原选择回执的固定角色名称");
            if (FixedRoleAdminNameCompilation.ForPage(model,table,page) is {} admin) Add(admin.Source,"原选择回执的固定后台名称");
        }

        /// <summary>物理属性和派生展示字段分别登记；每个物理列都必须在当前页可读上限内。</summary>
        private void Add(ColumnModel column, string reason, bool expand = true)
        {
            if (!table.Columns.Any(candidate => candidate.Source.Id == column.Source.Id) || !page.Readable.Any(candidate => candidate.Source.Id == column.Source.Id))
            { Gap(column.Source.Id, "读取依赖不属于本页已声明可读字段：" + column.JsonName); return; }
            if (column.Source.IsVirtualColumn != 0 || (GenerationModel.Enabled(column.Source.Extra, "isJoinColumn")&&!column.IsManagedCascadeName))
            { Gap(column.Source.Id, "联接或虚拟字段缺少固定读取计划：" + column.JsonName); return; }
            if(Kind(column)=="CascadeTree")
            {
                var resolved=CascadeTreeReadCompilation.Resolve(model,table,page,column);var name=CascadeTreeViewRenderer.Name(table,column);
                if(column.Source.ControlKind!="CascadeTree"||resolved.Definition is null||name is null||column.Type is not("string?" or "long?"))
                {Gap(column.Source.Id,resolved.Reason??"级联隐式名称缺少固定物理读取合同。");return;}
                // 用途IR复用实际VO别名检查，不能把会被正式生成拒绝的图标成可编译。
                try{_ = CascadeTreeViewRenderer.ForPage(model,table,page);}
                catch(InvalidOperationException error){Gap(column.Source.Id,error.Message);return;}
                // 原级联控件明确依赖此名称，只有同前缀托管列可绕过独立页面字段要求。
                physical.TryAdd(column.Source.Id,column);physical.TryAdd(name.Source.Id,name);
                Reason(column.Source.Id,reason);Reason(name.Source.Id,"原级联叶子隐式名称："+column.JsonName);
                Output(new(column.JsonName,column.Property,column,ReadProjectionOutputKind.Physical));
                Output(new(name.JsonName,name.Property,name,ReadProjectionOutputKind.Physical));
                Output(new(column.JsonName+"DisplayText",column.Property+"DisplayText",column,ReadProjectionOutputKind.CascadeDisplayText));
                return;
            }
            FixedCustomControlReadContract? custom = null;
            if (Kind(column) == "CustomControl")
            {
                if (purpose != ReadProjectionPurpose.Detail)
                { Gap(column.Source.Id, "控件读取依赖尚未证明：CustomControl列表覆盖"); return; }
                var fixedRead = FixedCustomControlReadCompilation.Resolve(model, table, page, column);
                if (fixedRead.Contract is null) { Gap(column.Source.Id, fixedRead.Reason!); return; }
                custom = fixedRead.Contract;
            }
            FixedRoleCheckboxModel? fixedRole = null;
            if (Kind(column) == "MultiCheckBoxDatabase")
            {
                var role = FixedRoleCheckboxCompilation.Resolve(model,table,page,column);
                if (role.Definition is null) { Gap(column.Source.Id,role.Reason!); return; }
                fixedRole = role.Definition;
            }
            if (Kind(column) is "Title" or "Tab" ||
                purpose == ReadProjectionPurpose.Detail && Kind(column) is not ("" or "date_time" or "date" or "month" or "time" or
                    "ConstList" or "ConstDownList" or "CheckBox" or "List" or "TextArea" or "FileLibrary" or "IntenetImage" or
                    "Base64Image" or "Image" or "MultiImage" or "Upload" or "Video" or "SelectPage" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "RTF" or "CustomControl"))
            { Gap(column.Source.Id, "控件读取依赖尚未证明：" + Kind(column)); return; }
            if (ManagerCredentialCompilation.IsCredential(table,column))
            {
                if (!ManagerCredentialCompilation.IsOriginal(table,page)) Gap(column.Source.Id,"额外账号页凭据用途尚未证明。");
                else if (purpose == ReadProjectionPurpose.Detail) Output(new(column.JsonName,column.Property,column,ReadProjectionOutputKind.CredentialEcho));
                return;
            }
            physical.TryAdd(column.Source.Id, column);
            Reason(column.Source.Id, reason);
            Output(new(column.JsonName, column.Property, column, ReadProjectionOutputKind.Physical));
            if (fixedRole is not null)
            {
                Output(new(FixedRoleCheckboxModel.Alias,"YzRoleNames",column,ReadProjectionOutputKind.RoleNames));
                Reason(column.Source.Id,"角色名称由真实后台所属当前项目的固定ID/name查询按原顺序计算，不写物理列");
                return;
            }
            var roleAdmin=FixedRoleAdminNameCompilation.Resolve(model,table,page,column);
            if(roleAdmin.Declared)
            {
                if(roleAdmin.Definition is null){Gap(column.Source.Id,roleAdmin.Reason!);return;}
                Output(new(FixedRoleAdminNameModel.Alias,FixedRoleAdminNameModel.Property,column,ReadProjectionOutputKind.RoleAdminName));
                Reason(column.Source.Id,"原后台名称由当前项目未删除后台的固定ID/name串行批量计算，不写物理列");
                return;
            }
            if (!expand || !expanded.Add(column.Source.Id)) return;
            if (custom is not null)
            {
                Reason(column.Source.Id, "固定控件读取：" + custom.Tag);
                foreach (var dependency in custom.Columns.Where(item => item.Source.Id != column.Source.Id))
                    Add(dependency, "固定控件依赖：" + custom.Tag + "/" + column.JsonName, false);
                return;
            }
            var kind = Kind(column);
            var fixedCatalog = FixedOptionCatalogCompilation.Resolve(model, page, column);
            if (fixedCatalog.Declared)
            {
                if (fixedCatalog.Definition is not { } catalog) { Gap(column.Source.Id, fixedCatalog.Reason!); return; }
                if (kind != "List") { Gap(column.Source.Id, "固定目录控件覆盖不匹配。"); return; }
                Output(new(catalog.JsonName, catalog.Property, column, ReadProjectionOutputKind.CatalogName));
                Reason(column.Source.Id, "名称仅由固定C#模板目录及本用途版本ID计算");
                return;
            }
            // 原列表的后缀展示分支先于常量分支；页覆盖只影响显示/筛选，不创建详情选择弹层。
            if (purpose != ReadProjectionPurpose.Detail &&
                (column.Source.ColumnName.EndsWith("_ids", StringComparison.Ordinal) || column.Source.ColumnName.EndsWith("_id", StringComparison.Ordinal) && kind.Length > 0) && kind != "FileLibrary")
            { Auxiliary(column, SelectionDisplayFieldCompilation.Resolve(table, column)); return; }
            if (kind is "ConstList" or "ConstDownList")
            {
                if (page.Readable.Any(candidate => candidate.Source.Id != column.Source.Id && candidate.JsonName.Equals(column.JsonName + "Show", StringComparison.OrdinalIgnoreCase)))
                    Gap(column.Source.Id, "常量展示名与已声明字段冲突：" + column.JsonName + "Show");
                else Output(new(column.JsonName + "Show", column.Property + "Show", column, ReadProjectionOutputKind.ConstantShow, Detail(column)));
                Reason(column.Source.Id, "常量Show仅由本用途原值派生");
            }
            else if (kind == "List")
            {
                if (purpose == ReadProjectionPurpose.Detail)
                {
                    try
                    {
                        var probe = page with { Readable = page.Readable.Where(candidate => candidate.Source.Id == column.Source.Id).ToArray(), Writable = page.Writable.Where(candidate => candidate.Source.Id == column.Source.Id).ToArray() };
                        var relation = RelationCompilation.ForPage(model, probe).SingleOrDefault(candidate => candidate.SourceColumn.Source.Id == column.Source.Id);
                        if (relation is null) { Gap(column.Source.Id, "关联目标未形成固定读取合同。"); return; }
                        var context = ControlRenderer.RelationContext(page, relation, false);
                        TargetParent(column, context);
                        var display = new OriginalDetailContext(model, table, page, []).DisplayField(column, relation.NameColumn.Source.ColumnName);
                        Auxiliary(column, table.Columns.SingleOrDefault(candidate => candidate.JsonName == display) ?? column);
                    }
                    catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException) { Gap(column.Source.Id, "关联读取依赖无法编译：" + error.Message); }
                }
                else Auxiliary(column, SelectionDisplayFieldCompilation.Resolve(table, column));
            }
            else if (kind is "SelectPage" or "MultiSelectPage" && purpose == ReadProjectionPurpose.Detail) Selection(column);
        }

        /// <summary>原详情按表控件分支，列表才取页级非空覆盖；两种用途不能互相推断。</summary>
        private string Kind(ColumnModel column) => purpose == ReadProjectionPurpose.Detail ? column.Source.ControlKind : Setting(column, "controlKind", column.Source.ControlKind);
        /// <summary>控件明细沿用原页级非空覆盖，既不改原字段也不合成未知定义。</summary>
        private string Detail(ColumnModel column) => Setting(column, "controlDetail", column.Source.ControlDetail);
        /// <summary>仅在当前页精确字段设置中读取固定键。</summary>
        private string Setting(ColumnModel column, string key, string fallback)
        {
            var settings = model.Catalog.ConfigPageColumns.SingleOrDefault(field => field.ConfigPageId == page.Source.Id && field.ConfigColumnId == column.Source.Id);
            var value = settings is null ? "" : OriginalDetailContext.Text(settings.Extra, key);
            return value.Length == 0 ? fallback : value;
        }

        /// <summary>缺辅助名时沿原UI保留ID；存在但未开放或无法证明的列不会偷偷暴露。</summary>
        private void Auxiliary(ColumnModel source, ColumnModel auxiliary)
        {
            if (auxiliary.Source.Id == source.Source.Id) { Reason(source.Source.Id, "原UI明确使用ID回退，不伪造辅助名称"); return; }
            Add(auxiliary, "关系显示辅助名称：" + source.JsonName, false);
        }

        /// <summary>独立目标父只接受现有编译器证明的唯一可读标量外键，同父由props提供。</summary>
        private void TargetParent(ColumnModel source, RelationUiContext context)
        {
            if (context.UnresolvedTarget) { Gap(source.Source.Id, "关系目标父没有唯一可读声明外键。"); return; }
            if (context.TargetParentField is not {} field) return;
            var parent = page.Readable.Single(candidate => candidate.JsonName == field);
            if (parent.IsJsonArray || parent.Type is not ("string?" or "long?" or "long" or "int?")) { Gap(source.Source.Id, "目标父外键不是已支持的标量标识。"); return; }
            Add(parent, "独立目标父上下文：" + source.JsonName, false);
        }

        /// <summary>选择控件复用稳定配置及Multi证明，不按猜测生成名称或联接。</summary>
        private void Selection(ColumnModel source)
        {
            try
            {
                var definition = SelectionControlDefinition.Resolve(source, model.Catalog.ConfigPageColumns.SingleOrDefault(field => field.ConfigPageId == page.Source.Id && field.ConfigColumnId == source.Source.Id));
                var target = model.Tables.SingleOrDefault(candidate => candidate.Pages.Any(targetPage => targetPage.Source.Id == definition.PageId));
                var targetPage = target?.Pages.Single(targetPage => targetPage.Source.Id == definition.PageId);
                var name = target?.Columns.SingleOrDefault(candidate => candidate.Source.ColumnName == definition.NameField || candidate.JsonName == definition.NameField);
                if (target is null || targetPage is null || name is null || !targetPage.Readable.Any(candidate => candidate.Source.Id == name.Source.Id) || name.IsJsonArray || GenerationModel.Enabled(name.Source.Extra, "isJoinColumn"))
                { Gap(source.Source.Id, "选择目标名称不在精确目标页可读投影内。"); return; }
                if (!target.IsGlobal && !target.Columns.Any(candidate => candidate.Source.ColumnName == "yz_project_id"))
                { Gap(source.Source.Id, "选择目标没有声明项目归属。"); return; }
                if (!string.IsNullOrWhiteSpace(source.Source.LinkConfigTableId) && source.Source.LinkConfigTableId != target.Source.Id)
                { Gap(source.Source.Id, "选择目标页与声明关联表不一致。"); return; }
                if (target.Source.IsRongyu != 1 || name.Type != "string?" && !name.IsId)
                { Gap(source.Source.Id, "选择目标名称类型或非冗余读取计划尚未证明。"); return; }
                if (source.Source.ControlKind == "MultiSelectPage" && !SelectionPageRenderer.Enabled(targetPage))
                { Gap(source.Source.Id, "多选来源与目标type=2确认合同不一致。"); return; }
                if (SelectionPageRenderer.Enabled(targetPage))
                {
                    var multi = MultiSelectCompilation.Resolve(model, table, page, source);
                    if (multi.Definition is not {} known) { Gap(source.Source.Id, multi.Reason ?? "多选读取合同尚未证明。"); return; }
                    if (known.Names is {} names) Auxiliary(source, names); else Auxiliary(source, source);
                    if (purpose == ReadProjectionPurpose.Detail && known.TargetParentField is {} field) Add(page.Readable.Single(candidate => candidate.JsonName == field), "多选独立目标父上下文：" + source.JsonName, false);
                }
                else
                {
                    if (!FixedPageQueryCompilation.TryProveSelection(FixedPageQueryCompilation.For(target, targetPage), definition, out var reason))
                    { Gap(source.Source.Id, reason!); return; }
                    if (purpose == ReadProjectionPurpose.Detail)
                    {
                        var parents = Parents(targetPage);
                        if (parents.Any(parent => !parent.Columns.Any(column => column.Source.ColumnName == "yz_project_id")))
                        { Gap(source.Source.Id, "选择目标父未声明项目归属。"); return; }
                        var relation = new RelationModel(source, target, targetPage, name, "", [], Parents(page), parents);
                        TargetParent(source, ControlRenderer.RelationContext(page, relation, false));
                        var display = new OriginalDetailContext(model, table, page, []).DisplayField(source, name.Source.ColumnName);
                        Auxiliary(source, name.IsId ? source : table.Columns.SingleOrDefault(candidate => candidate.JsonName == display) ?? source);
                    }
                    else Auxiliary(source, SelectionDisplayFieldCompilation.Resolve(table, source));
                }
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException) { Gap(source.Source.Id, "选择读取依赖无法编译：" + error.Message); }
        }

        /// <summary>只从已编译等值范围寻找实际父表，不接受客户端父表/列名。</summary>
        private IReadOnlyList<TableModel> Parents(PageModel targetPage) => targetPage.Scopes.Where(scope => scope.Source == "parent").Select(scope =>
            model.Tables.Single(candidate => string.IsNullOrWhiteSpace(scope.Column.Source.LinkConfigTableId) ? candidate.Source.TableName + "_id" == scope.Column.Source.ColumnName : candidate.Source.Id == scope.Column.Source.LinkConfigTableId)).DistinctBy(candidate => candidate.Source.Id).ToArray();

        /// <summary>选择确认另加固定入站名称字段，绝不能借同表另一页的可读投影扩大当前页。</summary>
        internal void InboundSelections()
        {
            foreach (var sourceTable in model.Tables)
            foreach (var sourcePage in sourceTable.Pages)
            // 只有原详情实际生成的选择按钮会发起确认；列表页级覆盖只显示/筛选名称文本。
            foreach (var source in sourcePage.DetailColumns.Where(column => column.Source.ControlKind is "SelectPage" or "MultiSelectPage"))
            {
                SelectionControlDefinition definition;
                try { definition = SelectionControlDefinition.Resolve(source, model.Catalog.ConfigPageColumns.SingleOrDefault(field => field.ConfigPageId == sourcePage.Source.Id && field.ConfigColumnId == source.Source.Id)); }
                catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException) { continue; /* 无法确定目标的配置由来源用途计划报告，不猜其目标。 */ }
                if (definition.PageId != page.Source.Id) continue;
                var name = table.Columns.SingleOrDefault(column => column.Source.ColumnName == definition.NameField || column.JsonName == definition.NameField);
                if (name is null || !page.Readable.Any(column => column.Source.Id == name.Source.Id) || name.IsJsonArray || name.Type != "string?" && !name.IsId)
                { Gap(source.Source.Id, "固定入站名称未在当前目标页开放：" + sourcePage.Source.Id + "/" + source.JsonName); continue; }
                if (!string.IsNullOrWhiteSpace(source.Source.LinkConfigTableId) && source.Source.LinkConfigTableId != table.Source.Id)
                { Gap(source.Source.Id, "固定入站选择页与声明关联表不一致。"); continue; }
                if (table.Source.IsRongyu != 1 || !table.IsGlobal && !table.Columns.Any(column => column.Source.ColumnName == "yz_project_id"))
                { Gap(source.Source.Id, "固定入站选择目标的项目或读取计划尚未证明。"); continue; }
                if (source.Source.ControlKind == "MultiSelectPage" && !SelectionPageRenderer.Enabled(page))
                { Gap(source.Source.Id, "固定入站多选来源与目标type=2确认合同不一致。"); continue; }
                if (SelectionPageRenderer.Enabled(page))
                {
                    var multi = MultiSelectCompilation.Resolve(model, sourceTable, sourcePage, source);
                    if (multi.Definition is null) { Gap(source.Source.Id, multi.Reason ?? "固定入站多选合同尚未证明。"); continue; }
                }
                else
                {
                    try
                    {
                        if (!FixedPageQueryCompilation.TryProveSelection(FixedPageQueryCompilation.For(table, page), definition, out var reason))
                        { Gap(source.Source.Id, reason!); continue; }
                    }
                    catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException) { Gap(source.Source.Id, "固定入站选择依赖无法编译：" + error.Message); continue; }
                }
                Add(name, "固定入站选择确认名称：" + sourcePage.Source.Id + "/" + source.Source.Id, false);
            }
        }

        /// <summary>只读与字段可见条件只影响详情；它们不会把详情其它列带入List。</summary>
        internal void DetailConditions()
        {
            try
            {
                if (ChildrenCompilation.ReadonlyField(page) is {} field) Add(page.Readable.Single(column => column.JsonName == field), "页面及主子只读条件", false);
            }
            catch (InvalidOperationException error) { Gap("", error.Message); }
            foreach (var column in page.DetailColumns)
                if (column.Source.Extra.TryGetValue("visibleCondition", out var rule)) Rule(rule, "详情可见条件：" + column.JsonName, column.Source.Id);
        }

        /// <summary>列表顶部按钮不按行判断；只有原Grid/Edit按钮实际使用的规则参与字段依赖。</summary>
        internal void Buttons(bool detail)
        {
            foreach (var button in model.Catalog.ConfigButtons.Where(button => button.ValueKind == JsonValueKind.Object))
            {
                try
                {
                    if (Text(button, "configPageId") != page.Source.Id) continue;
                    var place = Text(button, "place");
                    if (detail ? place is not ("Edit" or "EditAndGrid") : place != "EditAndGrid") continue;
                    var scheme = Text(button, "visibleScheme", "Normal");
                    if (scheme is not ("Normal" or "AlwaysVisible" or "VisibleWhenWritable")) continue;
                    if (button.TryGetProperty("visibleRuleJson", out var rule)) Rule(rule, "按钮可见条件：" + Text(button, "id"), "");
                }
                catch (InvalidOperationException error) { Gap("", "按钮固定归属或显示配置无法编译：" + error.Message); }
            }
        }

        /// <summary>仅收集原JSON规则明确读取的field；value/valueList始终是常量，不递归猜列。</summary>
        private void Rule(JsonElement value, string reason, string owner)
        {
            if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())) return;
            try
            {
                using var parsed = value.ValueKind == JsonValueKind.String ? JsonDocument.Parse(value.GetString()!) : JsonDocument.Parse(value.GetRawText());
                var nodes = 0;
                Group(parsed.RootElement, 0);
                void Group(JsonElement group, int depth)
                {
                    if (++nodes > 1024 || depth > 32) throw new InvalidOperationException("条件嵌套超过编译边界。");
                    Object(group, ["logic", "conditions", "groups"]);
                    if (group.TryGetProperty("logic", out var logic) && (logic.ValueKind != JsonValueKind.String || logic.GetString()?.ToUpperInvariant() is not ("AND" or "OR"))) throw new InvalidOperationException("条件逻辑必须是AND或OR。");
                    if (group.TryGetProperty("conditions", out var conditions))
                    {
                        if (conditions.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("conditions必须为数组。");
                        foreach (var condition in conditions.EnumerateArray())
                        {
                            Object(condition, ["field", "operator", "value", "valueList"]);
                            var op = Text(condition, "operator", "eq");
                            if (op is not ("eq" or "ne" or "in" or "notIn" or "gt" or "gte" or "lt" or "lte" or "isBlank" or "isNotBlank")) throw new InvalidOperationException("条件操作符尚未支持。");
                            if (!condition.TryGetProperty("field", out var field) || field.ValueKind == JsonValueKind.Null) continue;
                            if (field.ValueKind != JsonValueKind.String) throw new InvalidOperationException("条件字段必须为字符串。");
                            var key = field.GetString()!;
                            if (string.IsNullOrWhiteSpace(key)) continue;
                            if (key.Contains('.')) throw new InvalidOperationException("JSON点路径尚未形成固定读取计划：" + key);
                            var column = page.Readable.SingleOrDefault(column => column.JsonName == key);
                            if (column is not null) { Add(column, reason, false); continue; }
                            var constant = page.Readable.SingleOrDefault(column => column.JsonName + "Show" == key && Kind(column) is "ConstList" or "ConstDownList");
                            if (constant is not null) { Add(constant, reason); continue; }
                            throw new InvalidOperationException("条件字段不在当前页可读投影：" + key);
                        }
                    }
                    if (group.TryGetProperty("groups", out var groups))
                    {
                        if (groups.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("groups必须为数组。");
                        foreach (var child in groups.EnumerateArray()) Group(child, depth + 1);
                    }
                }
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException) { Gap(owner, reason + "无法编译：" + error.Message); }
        }

        /// <summary>未知结构和重复键必须明确报告，不能依赖宽松JSON归一化隐藏读依赖。</summary>
        private static void Object(JsonElement value, string[] keys)
        {
            if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any(field => !keys.Contains(field.Name, StringComparer.Ordinal)) || value.EnumerateObject().Select(field => field.Name).Distinct(StringComparer.Ordinal).Count() != value.EnumerateObject().Count())
                throw new InvalidOperationException("条件必须是无重复键且结构已支持的JSON对象。");
        }
        /// <summary>固定配置只接受原标量，复杂值必须成为显式缺口。</summary>
        private static string Text(JsonElement value, string key, string fallback = "") => value.TryGetProperty(key, out var item) && item.ValueKind != JsonValueKind.Null ? item.ValueKind == JsonValueKind.String ? item.GetString()! : item.ValueKind == JsonValueKind.Number ? item.GetRawText() : throw new InvalidOperationException("固定规则属性必须为标量：" + key) : fallback;
        /// <summary>输出别名不能覆盖另一稳定字段或另一派生来源。</summary>
        private void Output(ReadProjectionOutput field)
        {
            if (output.TryGetValue(field.JsonName, out var previous) && (previous.Source.Source.Id != field.Source.Source.Id || previous.Kind != field.Kind)) Gap(field.Source.Source.Id, "用途输出名冲突：" + field.JsonName);
            else output.TryAdd(field.JsonName, field);
        }
        /// <summary>每个实际物理依赖保留全部用途理由，供后续生成器审计。</summary>
        private void Reason(string id, string reason)
        {
            if (!dependencies.TryGetValue(id, out var reasons)) dependencies[id] = reasons = new(StringComparer.Ordinal);
            reasons.Add(reason);
        }
        /// <summary>未知能力保留字段位置与原因，不能回退为全表读取。</summary>
        private void Gap(string id, string reason) => gaps.Add(new(id, reason));

        /// <summary>计划仅输出已证明的固定列；有Gap的计划不能视为可生成SQL。</summary>
        internal ReadProjectionPlan Finish() => new(purpose, physical.Values.OrderBy(column => column.IsId ? 0 : 1).ThenBy(column => column.Source.Id, StringComparer.Ordinal).ToArray(),
            output.Values.OrderBy(field => field.Source.IsId ? 0 : 1).ThenBy(field => field.Source.Source.Id, StringComparer.Ordinal).ThenBy(field => field.Kind).ToArray(),
            dependencies.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => new ReadProjectionDependency(item.Key, item.Value.Order(StringComparer.Ordinal).ToArray())).ToArray(),
            gaps.Distinct().OrderBy(gap => gap.ColumnId, StringComparer.Ordinal).ThenBy(gap => gap.Reason, StringComparer.Ordinal).ToArray());
    }
}
