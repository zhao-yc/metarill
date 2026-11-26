using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
namespace Metarill.Generation;

internal sealed record CrossTableLevelModel(string Key,string Prefix,TableModel Target,PageModel Page,ColumnModel Name,ColumnModel? Parent,IReadOnlyList<(ColumnModel Column,bool Descending)> Order,bool DerivedName=false,IReadOnlyList<string>? NameDependencies=null,bool DerivedOrder=false,ColumnModel? AlternateKey=null)
{
    internal string KeyProperty => AlternateKey?.Property ?? "Id";
    internal string KeyColumn => AlternateKey?.Source.ColumnName ?? "id";
    internal bool TextKey => AlternateKey?.Type=="string?";

    /// <summary>业务列在实体上可空，实际发布结构必须有非空约束。</summary>
    internal string KeyValue(string row) => row+"."+KeyProperty+(AlternateKey?.Type=="long?"?"!.Value":"");
}
internal sealed record CrossTableReadModel(ColumnModel Source,IReadOnlyList<CrossTableLevelModel> Levels,string Joiner)
{
    internal bool HasTextKeys=>Levels.Any(level=>level.TextKey);
    internal string TextLevels=>"new bool[]{"+string.Join(",",Levels.Select(level=>level.TextKey?"true":"false"))+"}";
}
internal sealed record CrossTableReadResult(CrossTableReadModel? Definition,string? Reason);

/// <summary>把原跨表级联编译为有限强类型链；不推断动态SQL或模糊页面，不改变物理列。</summary>
internal static class CrossTableReadCompilation
{
    /// <summary>只有来源页已声明的可读/筛选控件才能增加原候选端点。</summary>
    internal static IReadOnlyList<CrossTableReadModel> ForPage(GenerationModel model,TableModel table,PageModel page)=>(table.Source.IsFunctionTable==1?page.DetailColumns:page.Readable.Concat(page.Searchable)).DistinctBy(column=>column.Source.Id).Where(column=>column.Source.ControlKind=="CascadeTree").Select(column=>Resolve(model,table,page,column).Definition).OfType<CrossTableReadModel>().ToArray();
    /// <summary>固定读取计划在服务内共用，不因隐藏列增加外部端点。</summary>
    internal static IReadOnlyList<CrossTableReadModel> ForService(GenerationModel model,TableModel table,PageModel page)=>table.Source.IsFunctionTable==1?ForPage(model,table,page):table.Columns.Where(column=>column.Source.ControlKind=="CascadeTree").Select(column=>Resolve(model,table,page,column).Definition).OfType<CrossTableReadModel>().ToArray();
    /// <summary>完整包继续报告未完成的保存、展示、筛选与辅助列；读接口存在不代表完整控件可发布。</summary>
    internal static string? CapabilityReason(GenerationModel model,TableModel table,PageModel page,ColumnModel column)=>Resolve(model,table,page,column).Definition is null?null:"CascadeTree跨表候选/路径已接线；已证明的各级保存、已存名称展示及来源筛选独立接线，导入导出及完整应用验收仍待完成。";
    /// <summary>逐级验证唯一可读目标页、父键指向、名称与稳定排序，任何一步不明就拒绝整个链。</summary>
    internal static CrossTableReadResult Resolve(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        CrossTableReadResult Gap(string reason)=>new(null,"跨表级联读取尚未支持："+reason);
        try
        {
            var overlay=CrossTableFrontendProof.PageOverride(model,page,source);using var json=JsonDocument.Parse(string.IsNullOrWhiteSpace(overlay)?source.Source.ControlDetail:overlay);var root=json.RootElement;Object(root);
            if(Text(root,"sourceMode","crossTable")!="crossTable")return Gap("不是crossTable模式。");
            if(table.Source.IsRongyu is not(0 or 1)||source.Type is not("string?" or "long?")||!source.Source.ColumnName.EndsWith("_id",StringComparison.Ordinal)||source.IsSystem||GenerationModel.Enabled(source.Source.Extra,"isJoinColumn"))return Gap("来源须为普通实体表的物理_id字段。");
            if(Extra(root))return Gap("额外范围或目标页映射不能忽略。");
            if(!root.TryGetProperty("levels",out var entries)||entries.ValueKind!=JsonValueKind.Array||entries.GetArrayLength()<2)return Gap("至少需要2个固定层级。");
            var result=new List<CrossTableLevelModel>();var keys=new HashSet<string>(StringComparer.Ordinal);var prefixes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in entries.EnumerateArray())
            {
                Object(entry);if(Extra(entry))return Gap("层级额外范围或目标页映射不能忽略。");var nameOfTable=Text(entry,"table","");var target=model.Tables.SingleOrDefault(candidate=>candidate.Source.TableName==nameOfTable);
                if(target is null||target.Source.IsFunctionTable==1||target.Source.IsRongyu is not(0 or 1)||ManagerCredentialCompilation.IsTable(target)||TrustedSecurityServiceCompilation.Applies(model,target))return Gap("目标必须是已声明的普通实体表。");
                var key=Text(entry,"key",nameOfTable);var prefix=Text(entry,"columnPrefix",nameOfTable);Metarill.Core.Metadata.MetadataValidation.RequireIdentifier(prefix);
                if(!keys.Add(key)||!prefixes.Add(prefix))return Gap("层标识或字段前缀重复。");
                var alternate = CrossTableStorageColumns.AlternateLevelKey(model.Catalog,target.Source,entry);
                var keyColumn = alternate is null ? null : target.Columns.Single(column=>column.Source.Id==alternate.Id);
                bool Physical(ColumnModel column)=>NonRedundantReadCompilation.Physical(model,target,column)&&!GenerationModel.Enabled(column.Source.Extra,"isJoinColumn")&&!column.IsJsonArray&&!ManagerCredentialCompilation.IsCredential(target,column);
                var name=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==Text(entry,"nameField","name"));
                if(name is null||(name.Source.LogicalType!="string"&&!MultiValueLabel.IsTyped(name.Source.LogicalType))||name.IsJsonArray)
                    return Gap("名称必须为固定单值标量。");
                var derived=!NonRedundantReadCompilation.Physical(model,target,name);
                // 实时名称必须等到确定实际候选页再证明。这里只检查物理联接副本。
                var globalName=!derived&&target.IsGlobal&&Metarill.Core.Metadata.GlobalPhysicalRelationValue.IsPublishedScalar(
                    model.Catalog,Metarill.Core.Metadata.MetadataStoragePolicy.ForTable(model.Catalog,target.Source),target.Source,name.Source);
                if(!derived&&!Physical(name)&&!globalName)return Gap("物理联接名称仍需独立缓存证明。");
                ColumnModel? parent=null;
                if(result.Count==0&&!string.IsNullOrWhiteSpace(Text(entry,"parentField","")))Metarill.Core.Metadata.MetadataValidation.RequireIdentifier(Text(entry,"parentField",""));
                if(result.Count>0)
                {
                    parent=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==Text(entry,"parentField",""));if(parent is null||parent.IsSystem||parent.Type is not("string?" or "long?")||!Physical(parent))return Gap("非首层必须有物理字符串/int64父键。");
                    if(result[^1].TextKey&&parent.Type!="string?")return Gap("文本上级业务键要求真实文本父字段。");
                    if(!string.IsNullOrWhiteSpace(parent.Source.LinkConfigTableId)&&parent.Source.LinkConfigTableId!=result[^1].Target.Source.Id)return Gap("父键未指向上一层。");
                }
                if(!target.IsGlobal&&!target.Columns.Any(column=>column.Source.ColumnName=="yz_project_id"))return Gap("目标缺少项目归属或明确全局声明。");
                var pages=target.Pages.Where(candidate=>!candidate.Scopes.Any(scope=>scope.Source=="parent")&&!ExactDataManageCompilation.Declared(candidate)&&candidate.Readable.Any(column=>column.Source.Id==name.Source.Id)&&(parent is null||candidate.Readable.Any(column=>column.Source.Id==parent.Source.Id))).ToArray();if(pages.Length!=1)return Gap("每级需要唯一且无目标父参数的可读页面。");
                // 递归R1的完整来源行归属在混合传播目录中一次性校验。
                if(!FixedPageConditionCompilation.HasLogicalFields(pages[0])&&
                    FixedPageConditionCompilation.HasAccountVariables(pages[0])&&table.Source.IsRongyu==1&&table.Source.IsFunctionTable==0)
                    _=Metarill.Data.Queries.AccountScopedCrossCache.Compile(model.Catalog,table.Source,target.Source,pages[0].Source);
                if(target.IsGlobal)
                    NonRedundantCrossTargetsCompilation.RequireGlobal(model,target,pages[0],name);
                var order = GenerationModel.ValidateOrderBy(Text(entry,"orderBy",name.Source.ColumnName+" asc"),target.Columns).ToList();
                // 只允许本级目标已开放的固定单值联接，不能把任意派生字段当作物理排序。
                var joinedOrder = order.Select(item => item.Column).Where(column => !Physical(column) &&
                    !((derived || globalName) && column.Source.Id == name.Source.Id)).ToArray();
                if (joinedOrder.Any(column => !CascadeTargetOrderCompilation.CanUseJoin(model, target, pages[0], column)))
                    return Gap("排序须为物理标量或本级开放的固定单值联接。");
                if (!order.Any(item => item.Column.IsId)) order.Add((target.Columns.Single(column => column.IsId), false));
                var nameDependencies = derived && target.Source.Id == table.Source.Id
                    ? Metarill.Data.Queries.NonRedundantReads.CompileSourceDependencies(model.Catalog, target.Source.Id, name.Source.ColumnName)
                    : null;
                result.Add(new(key, prefix, target, pages[0], name, parent, order, derived, nameDependencies, joinedOrder.Length > 0,keyColumn));
            }
            if(result[^1].Prefix+"_id"!=source.Source.ColumnName)return Gap("末级前缀与来源叶子字段不一致。");
            if(!string.IsNullOrWhiteSpace(source.Source.LinkConfigTableId)&&source.Source.LinkConfigTableId!=result[^1].Target.Source.Id)return Gap("叶子关联表与末级不一致。");
            if(result.Select(level=>GenerationModel.Camel(level.Prefix+"_id")).Distinct(StringComparer.Ordinal).Count()!=result.Count)return Gap("层级JSON字段名冲突。");
            var joiner=Text(root,"displayJoiner","/");var plan=new CrossTableReadModel(source,result.ToArray(),joiner);
            var functionForm=table.Source.IsFunctionTable==1;
            // 全局非冗余多选字典沿实际发布链校验父键，冗余全局来源仍走原发布限制。
            var globalTextDictionary=table.Source.IsRongyu==0&&SelectionPageRenderer.Enabled(page);
            if(plan.HasTextKeys&&(model.Catalog.DatabaseProvider!="mysql"||table.Source.IsView!=0||table.IsGlobal&&!globalTextDictionary||
                result.Any(level=>level.Target.Source.IsView!=0)||
                !functionForm&&!SupportsSourceParent(model,table,source,result)))
                return Gap("文本业务键要求已发布的 MySQL 候选链，实体来源父范围须证明同一实际文本键。");
            if(functionForm&&!string.IsNullOrWhiteSpace(overlay)&&!CrossTableFrontendProof.KeepsStorageDefinition(model,page,source)&&
                (result.Any(level=>level.AlternateKey is not null)||HasDeclaredBusinessKey(source.Source.ControlDetail)))
                return Gap("功能表的页级配置不能替换表级业务节点键或固定路径。");
            if(plan.HasTextKeys&&(!CrossTableFrontendProof.KeepsStorageDefinition(model,page,source)||result[^1].TextKey&&source.Type!="string?"))
                return Gap("文本业务键要求表级定义及文本叶字段。");
            if(result.Any(level=>level.Target.Source.IsRongyu==0))NonRedundantCrossTargetsCompilation.Require(model,table,plan);return new(plan,null);
        }
        catch(Exception error)when(error is JsonException or ArgumentException or InvalidOperationException or NotSupportedException){return Gap("定义不能安全编译："+error.Message);}
    }
    /// <summary>旧页面覆盖不能把已发布的业务键悄悄退回实体主键。</summary>
    private static bool HasDeclaredBusinessKey(string detail)
    {
        using var definition=JsonDocument.Parse(detail);
        return definition.RootElement.TryGetProperty("levels",out var levels)&&levels.ValueKind==JsonValueKind.Array&&
            levels.EnumerateArray().Any(level=>Text(level,"idField","id")!="id");
    }
    /// <summary>文本父范围消费本链唯一逻辑层；其它控件各自证明其目标父键。</summary>
    private static bool SupportsSourceParent(GenerationModel model,TableModel table,ColumnModel source,IReadOnlyList<CrossTableLevelModel> levels)
    {
        foreach(var page in table.Pages)
        {
            var parents=page.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column).ToArray();
            if(parents.Length==0)continue;
            if(parents.Length>1)
            {
                // 冗余页的每个父字段都须由物理级联计划证明，且共用同一父表与业务列。
                if(table.Source.IsRongyu==1)
                {
                    var plans=Metarill.Data.Queries.RedundantSharedParents.Compile(model.Catalog,table.Source.Id,page.Source.Id);
                    var actual=parents.Select(column=>plans.GetValueOrDefault(column.Source.Id)?.Levels.LastOrDefault()).ToArray();
                    if(actual.Any(level=>level is null)||
                        actual.Select(level=>(level!.TableId,level.KeyColumn,level.TextKey)).Distinct().Count()!=1)return false;
                    continue;
                }
                // 多条稀疏级联可以共享已发布的整数父键，下级仍可使用文本键。
                var syntax=FixedConditionPlan.Compile(page.Source.WhereSql,table.Columns.Select(column=>column.Source.ColumnName));
                var scope=MultiTargetParentScope.CompileSelectionPage(model.Catalog,table.Source,syntax,allowGlobalParent:true);
                if(scope?.HasCompoundLogicalTextKey(model.Catalog,table.Source)==true)continue;
                if(scope is not {Logical:true,AdditionalTargets.Count:>0}||
                    scope.LogicalAlternateKey(model.Catalog,table.Source) is {LogicalType:not "int64"}||
                    !scope.HasSameLogicalKey(model.Catalog,table.Source))return false;
                continue;
            }
            var level=levels.SingleOrDefault(level=>level.Prefix+"_id"==parents[0].Source.ColumnName);
            // 缺少本分支层位时，非冗余父范围必须属于同一已声明的共享组。
            if(level is null&&table.Source.IsRongyu==0)
            {
                var columns=model.Catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Source.Id);
                var group=SharedCrossTableSparseSelection.CompileAll(table.Source,columns)
                    .SingleOrDefault(group=>group.Branches.Any(branch=>branch.SourceColumnId==source.Source.Id));
                if(group is null||!group.Slots.Any(slot=>slot.ColumnName==parents[0].Source.ColumnName))return false;
            }
        }
        return true;
    }

    /// <summary>识别页面已发布的文本级联父，普通文本外键仍按各自原有协议处理。</summary>
    internal static ColumnModel? TextSourceParent(GenerationModel model,TableModel table,PageModel page)
    {
        var parents=page.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column)
            .DistinctBy(column=>column.Source.Id).OrderBy(column=>column.Source.Id,StringComparer.Ordinal).ToArray();
        // 每条已发布路径都使用文本键时，原父参数保留字符串。普通文本列仍可承载整数父键。
        return parents.Length>0&&parents.All(column=>column.Type=="string?"&&
            (NonRedundantCrossParentCompilation.IsText(model,table,page,column)||
                SharedRedundantParentCompilation.IsText(model,table,page,column)))?parents[0]:null;
    }

    /// <summary>不接受JSON最后值覆盖，结构与读取采用相同键解释。</summary>
    private static void Object(JsonElement value){if(value.ValueKind!=JsonValueKind.Object||value.EnumerateObject().Select(field=>field.Name).Distinct(StringComparer.Ordinal).Count()!=value.EnumerateObject().Count())throw new ArgumentException("层级必须为无重复键对象。");}
    /// <summary>原标签与固定层级外观不进入SQL；额外查询定义明确拒绝。</summary>
    private static bool Extra(JsonElement value)=>new[]{"whereSql","where","sql","pageId","filterMatchMode"}.Any(key=>value.TryGetProperty(key,out _));
    /// <summary>空文本采用原默认，不把对象或数字转成SQL标识符。</summary>
    private static string Text(JsonElement value,string key,string fallback)=>!value.TryGetProperty(key,out var field)||field.ValueKind==JsonValueKind.Null?fallback:field.ValueKind==JsonValueKind.String?string.IsNullOrWhiteSpace(field.GetString())?fallback:field.GetString()!:throw new ArgumentException("层级配置必须为文本："+key);
}
