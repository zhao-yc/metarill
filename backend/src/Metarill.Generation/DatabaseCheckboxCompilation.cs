using System.Text.Json;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

internal sealed record DatabaseCheckboxModel(ColumnModel Source,TableModel Target,ColumnModel Name,ColumnModel Names,IReadOnlyList<(ColumnModel Column,string Source)> Scopes,RelationConditionModel? Conditions,IReadOnlyList<(ColumnModel Column,bool Descending)> Order,IReadOnlyList<TableModel> SourceParents,ColumnModel? SourceParent,bool DerivedName=false,PageModel? GlobalPage=null)
{
    internal PageModel? LogicalTargetPage { get; init; }
    // 没有逻辑候选条件的显式目标页仍决定名称路径内每个关系角色。
    internal PageModel? NameTargetPage { get; init; }
    // 普通目标页的物理条件参与原候选、保存和 Excel。
    internal PageModel? PhysicalTargetPage { get; init; }
    internal bool UsesPageRead { get; init; }
    internal ColumnModel? FunctionParentField { get; init; }
    internal ExactDataManageModel? ExactTarget { get; init; }
    internal bool LogicalSourceParent { get; init; }
    internal bool GlobalParent { get; init; }
    internal bool IntegerLabel => Name.Type is ("int?" or "long?");
    internal bool TypedLabel => Metarill.Core.Contracts.MultiValueLabel.IsTyped(Name.Source.LogicalType);
    internal string Endpoint=>"findNameId"+GenerationModel.Pascal(Source.Source.ColumnName[..^4])+Source.Source.Id;
    internal IEnumerable<ColumnModel> Dependencies=>(Conditions?.Forms.Select(binding=>binding.Column)??[]).Concat(SourceParent is null?[]:[SourceParent]).DistinctBy(column=>column.Source.Id);
}
internal sealed record DatabaseCheckboxResult(DatabaseCheckboxModel? Definition,string? Reason);

/// <summary>原通用复选按来源字段签发固定候选、写入及Excel范围，账号角色继续走专用协调器。</summary>
internal static class DatabaseCheckboxCompilation
{
    /// <summary>不能用普通复选入口绕过真实账号／角色／后台表的固定归属与写协调。
    /// </summary>
    internal static bool Generic(TableModel table,ColumnModel column)=>column.Source.ControlKind=="MultiCheckBoxDatabase"&&!Protected(table);
    private static bool Protected(TableModel table)=>table.Source.Id is FixedRoleCheckboxCompilation.ManagerTable or FixedRoleCheckboxCompilation.RoleTable or FixedRoleCheckboxCompilation.AdminTable||table.Source.TableName is "yz_manager" or "yz_role" or "yz_admin_type"||ManagerCredentialCompilation.IsTable(table);
    /// <summary>业务记录项目必须为真实物理归属，不使用联接缓存替代；主键固定为框架精确long。</summary>
    private static bool HasIdentity(GenerationModel model,TableModel table)=>table.Columns.Any(column=>column.IsId&&column.Type=="long"&&NonRedundantReadCompilation.Physical(model,table,column)&&!GenerationModel.Enabled(column.Source.Extra,"isJoinColumn"))&&table.Columns.Any(column=>column.Source.ColumnName=="yz_project_id"&&column.Type is "long?" or "string?"&&NonRedundantReadCompilation.Physical(model,table,column)&&!column.IsJsonArray&&!GenerationModel.Enabled(column.Source.Extra,"isJoinColumn"));
    /// <summary>所有明确目标声明必须一致；旧表名字面值仅在没有关联表／页绑定时回退使用。
    /// </summary>
    internal static TableModel Target(GenerationModel model,ColumnModel source,DatabaseCheckboxDefinition definition)
    {
        var declared=new List<TableModel>();
        void TableId(string id){if(id.Length>0)declared.Add(model.Tables.SingleOrDefault(table=>table.Source.Id==id)??throw new InvalidOperationException("数据库复选声明目标表不存在。"));}
        void TableName(string name){if(name.Length>0)declared.Add(model.Tables.SingleOrDefault(table=>table.Source.TableName==name)??throw new InvalidOperationException("数据库复选声明目标表名不存在。"));}
        void PageId(string id){if(id.Length>0)declared.Add(model.Tables.SingleOrDefault(table=>table.Pages.Any(page=>page.Source.Id==id))??throw new InvalidOperationException("数据库复选声明目标页不存在。"));}
        TableId(source.Source.LinkConfigTableId);TableName(source.Source.Extra.GetValueOrDefault("joinTable").ToString());PageId(source.Source.Extra.GetValueOrDefault("linkConfigPageId").ToString());TableId(definition.TableId);TableName(definition.Table);PageId(definition.PageId);
        if(declared.Count==0)TableName(definition.LegacyTargetHint.Length>0?definition.LegacyTargetHint:source.Source.ColumnName[..^4]);
        if(declared.Select(table=>table.Source.Id).Distinct(StringComparer.Ordinal).Count()!=1)throw new InvalidOperationException("数据库复选的表／页／联接目标声明冲突。");return declared[0];
    }
    /// <summary>隐藏字段也在复制或默认值触发时校验；不因此开放公开候选或VO。
    /// </summary>
    internal static IReadOnlyList<DatabaseCheckboxModel> ForService(GenerationModel model,TableModel table,PageModel page)=>table.Source.IsFunctionTable==1?ForPage(model,table,page):table.Columns.Where(column=>Generic(table,column)).Select(column=>Resolve(model,table,page,column).Definition).OfType<DatabaseCheckboxModel>().ToArray();
    /// <summary>候选用于原详情及筛选，名称输出仍单独按实际Readable字段决定。
    /// </summary>
    internal static IReadOnlyList<DatabaseCheckboxModel> ForPage(GenerationModel model,TableModel table,PageModel page)=>page.Readable.Concat(page.Writable).Concat(page.Searchable).DistinctBy(column=>column.Source.Id).Where(column=>Generic(table,column)).Select(column=>Resolve(model,table,page,column).Definition).OfType<DatabaseCheckboxModel>().ToArray();
    /// <summary>原详情联动从当前实体读取已开启自动回传的字段。其他配置沿用原表单参数来源。</summary>
    internal static IReadOnlyList<RelationFormBinding>? LiveDetailForm(ColumnModel source, DatabaseCheckboxModel? checkbox)
    {
        if (!GenerationModel.Enabled(source.Source.Extra, "isAutoPostBackQuery"))
            return null;
        var forms = checkbox?.Conditions?.Forms;
        return forms is { Count: > 0 } && forms.All(binding => GenerationModel.Enabled(binding.Column.Source.Extra, "isAutoPostBack"))
            ? forms
            : null;
    }

    /// <summary>复选使用物理 ID 和已证明名称。原候选条件完整编译，独立于目标页的其他条件。</summary>
    /// </summary>
    internal static DatabaseCheckboxResult Resolve(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        DatabaseCheckboxResult Gap(string message)=>new(null,"数据库复选尚未支持："+message);
        if(!Generic(table,source))return Gap("固定安全表必须使用其专用协调器。");
        try
        {
            if(model.Catalog.DatabaseProvider!="mysql")return Gap("当前方言尚未完成复选成员及名称用途验收。");
            var functionForm=table.Source.IsFunctionTable==1;
            if(table.Source.IsRongyu is not(0 or 1)||table.IsGlobal||source.Type!="string?"||!source.Source.ColumnName.EndsWith("_ids",StringComparison.Ordinal)||
                !functionForm&&!NonRedundantReadCompilation.Physical(model,table,source))return Gap("来源须为已声明的字符串_ids。");
            if(!functionForm&&!HasIdentity(model,table))return Gap("来源缺少真实物理主键或项目归属。");
            var baseline=DatabaseCheckboxDefinition.Parse(source.Source.ControlDetail);
            var field=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==source.Source.Id);
            if(field?.Extra.GetValueOrDefault("controlKind").ToString() is{Length:>0} kind&&kind!="MultiCheckBoxDatabase")
                return Gap("页级控件覆盖改变了原复选存储协议。");
            var definition=baseline.WithPageOverride(field?.Extra.GetValueOrDefault("controlDetail").ToString());
            var target=Target(model,source,definition);if(target.Source.Id!=Target(model,source,baseline).Source.Id)return Gap("页级目标覆盖未绑定同一物理关系，不能错读名称。");
            if(Protected(target)||target.Source.IsFunctionTable==1)return Gap("目标必须是有记录的业务表。");
            PageModel? globalPage=null;
            PageModel? publishedGlobalPage=null;
            if(target.IsGlobal&&!functionForm)
            {
                var proof=table.Source.IsRongyu==1?GlobalMultipleRelationTarget.RequireCached(model.Catalog,table.Source,source.Source,target.Source):GlobalMultipleRelationTarget.Require(model.Catalog,table.Source,source.Source,target.Source);
                if(definition.PageId.Length>0&&definition.PageId!=proof.Page.Id)return Gap("全局复选页级目标覆盖与固定关系不一致。");
                // 复选的搜索、回读与 Excel 必须沿已发布缓存目标页的同一固定范围。
                publishedGlobalPage=target.Pages.Single(page=>page.Source.Id==proof.Page.Id);
                if(NonRedundantReadCompilation.HasGlobalRowProject(target)||FixedPageConditionCompilation.HasMultiTargetScope(model,target,publishedGlobalPage)||
                    GlobalPhysicalRelationValue.IsPublishedTypedCache(model.Catalog,MetadataStoragePolicy.ForTable(model.Catalog,target.Source),target.Source,proof.Name))
                    globalPage=publishedGlobalPage;
            }
            if(definition.NameField!=baseline.NameField)return Gap("页级名称覆盖未绑定同一固定读取和缓存。");
            var name=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==definition.NameField);
            var scalarLabel=name is not null&&MultiSelectCompilation.SupportsTypedLabel(model,table,source,target,name);
            if(name is null||name.Type!="string?"&&!scalarLabel||name.IsJsonArray||ManagerCredentialCompilation.IsCredential(target,name))
                return Gap("名称必须为已声明的非凭据且已证明的标量。");
            var derived=!NonRedundantReadCompilation.Physical(model,target,name);if(!derived&&GenerationModel.Enabled(name.Source.Extra,"isJoinColumn")&&(!target.IsGlobal||!GlobalPhysicalRelationValue.IsPublishedScalar(model.Catalog,MetadataStoragePolicy.ForTable(model.Catalog,target.Source),target.Source,name.Source)))return Gap("物理Join名称仍需独立缓存证明。");
            if(!target.IsGlobal&&!HasIdentity(model,target))return Gap("目标缺少真实项目归属。");
            var linkedPage=source.Source.Extra.GetValueOrDefault("linkConfigPageId").ToString();
            if(linkedPage.Length==0&&publishedGlobalPage is not null)linkedPage=publishedGlobalPage.Source.Id;
            var explicitPage=definition.PageId.Length>0?definition.PageId:linkedPage;
            var namePage=target.Pages.SingleOrDefault(candidate=>candidate.Source.Id==explicitPage);
            if(functionForm&&explicitPage.Length>0&&namePage is null)return Gap("功能表复选的显式目标页未在实际目标表发布。");
            var exactTarget=functionForm&&namePage is not null?ExactDataManageCompilation.Resolve(model,target,namePage):null;
            // 功能表没有持久化来源值。纯物理条件直接使用已绑定的账号和当前表单父键。
            var logicalPage=namePage is not null&&FixedPageConditionCompilation.HasMultiTargetScope(model,target,namePage)&&
                (!functionForm||FixedPageConditionCompilation.HasLogicalFields(namePage)||namePage.Scopes.Any(scope=>scope.Source=="parent"&&
                    (!NonRedundantReadCompilation.Physical(model,target,scope.Column)||
                     model.Tables.Any(parent=>parent.IsGlobal&&parent.Source.Id==scope.Column.Source.LinkConfigTableId))))?namePage:null;
            var fixedPage=baseline.PageId.Length>0?baseline.PageId:linkedPage;
            // 同一候选即使由表级继承，也必须用当前普通非冗余来源页的父键重新证明。
            var pageRead=namePage is not null&&table.Source.IsRongyu==0&&target.Source.IsRongyu==0&&!target.IsGlobal&&
                (explicitPage!=fixedPage||logicalPage?.Scopes.Any(scope=>scope.Source=="parent")==true);
            if(explicitPage!=fixedPage&&!pageRead&&(logicalPage is not null||target.Pages.Any(candidate=>candidate.Source.Id==fixedPage&&FixedPageConditionCompilation.HasMultiTargetScope(model,target,candidate))))
                return Gap("复选不能用页覆盖替换表级固定范围。");
            if(!functionForm&&logicalPage is not null&&ExactDataManageCompilation.Declared(logicalPage))
                return Gap("复选目标范围需要与实际目标页及项目归属一致。");
            var (scopes,conditions)=RelationConditionsCompilation.Compile(model,table,page,target,definition.WhereSql);
            // 旧复选允许只绑定表。具有物理行项目归属的全局目录仍按真实身份限定。
            if (functionForm && NonRedundantReadCompilation.HasGlobalRowProject(target))
            {
                if(!HasIdentity(model,target))return Gap("全局目标缺少物理行项目归属。");
                var rowProject=target.Columns.Single(column=>column.Source.ColumnName=="yz_project_id");
                scopes=scopes.Append((Column:rowProject,Source:"project")).DistinctBy(scope=>(scope.Column.Source.Id,scope.Source)).ToArray();
            }
            PageModel? physicalPage=null;
            var scopeAlreadyBound=logicalPage is not null||globalPage is not null&&
                (!derived||NonRedundantReadCompilation.HasGlobalRowProject(target));
            if((table.Source.IsRongyu==0||functionForm)&&!scopeAlreadyBound&&namePage is not null&&
                (functionForm||namePage.Condition is not null||namePage.Scopes.Any(scope=>scope.Source=="parent")))
            {
                if(!functionForm&&ExactDataManageCompilation.Declared(namePage))
                    return Gap("原复选不能借用精确数据管理页的目标范围。");
                physicalPage=namePage;
                scopes=scopes.Concat(physicalPage.Scopes.Where(scope=>scope.Source=="parent"||target.IsGlobal))
                    .DistinctBy(scope=>(scope.Column.Source.Id,scope.Source)).ToArray();
            }
            if(functionForm&&logicalPage is not null)
                scopes=scopes.Concat(logicalPage.Scopes.Where(scope=>scope.Source=="parent"))
                    .DistinctBy(scope=>(scope.Column.Source.Id,scope.Source)).ToArray();
            bool FunctionLogicalParent(ColumnModel column)=>functionForm&&logicalPage is not null&&
                NonRedundantCrossParentCompilation.Managed(model,target,logicalPage,column);
            if(scopes.Any(scope=>scope.Column.Type is not("long?" or "int?" or "string?")||!NonRedundantReadCompilation.Physical(model,target,scope.Column)&&!FunctionLogicalParent(scope.Column)||scope.Column.IsJsonArray||ManagerCredentialCompilation.IsCredential(target,scope.Column)))return Gap("身份／父范围必须绑定真实物理ID标量。");
            TableModel Parent(ColumnModel column)=>model.Tables.SingleOrDefault(parent=>column.Source.LinkConfigTableId.Length>0?parent.Source.Id==column.Source.LinkConfigTableId:parent.Source.TableName+"_id"==column.Source.ColumnName)??throw new InvalidOperationException("复选父范围缺少明确真实父表。");
            var parentScopes=page.Scopes.Where(scope=>scope.Source=="parent").ToArray();if(parentScopes.Length>1)return Gap("多个来源父范围尚待完整协议证明。");
            var sourceParent=parentScopes.FirstOrDefault().Column;
            var logicalSource=sourceParent is not null?NonRedundantCrossParentCompilation.Find(model,table,page,sourceParent):null;
            if(sourceParent is not null&&(!NonRedundantReadCompilation.Physical(model,table,sourceParent)||sourceParent.IsJsonArray||sourceParent.Type is not("long?" or "string?")))return Gap("来源父必须为固定标量ID。");
            var physicalParents=scopes.Where(scope=>scope.Source=="parent").ToArray();
            var pageHasParent=logicalPage?.Scopes.Any(scope=>scope.Source=="parent")==true;
            if(logicalSource is not null&&physicalParents.Length==0&&!pageHasParent)
                return Gap("来源逻辑父键须对应明确的控件或目标页父范围。");
            var parents=logicalSource is {} path?new[]{path.Read.Levels[path.Index].Target}:parentScopes.Select(scope=>Parent(scope.Column)).ToArray();
            var globalParent=false;
            if(functionForm&&physicalParents.Length>0)
            {
                if(physicalParents.Length!=1)return Gap("功能表复选必须绑定唯一目标父字段。");
                var targetParent=physicalParents[0].Column;
                var targetBinding=pageHasParent&&namePage is not null?MultiTargetParentScope.Compile(model.Catalog,target.Source,
                    FixedConditionPlan.Compile(namePage.Source.WhereSql,target.Columns.Select(column=>column.Source.ColumnName))):null;
                var parent=targetBinding is null?Parent(targetParent):model.Tables.Single(item=>item.Source.Id==targetBinding.Parent.Id);
                if(parent.Source.IsFunctionTable!=0||parent.Source.IsView!=0||Protected(parent))
                    return Gap("复选目标父必须是真实业务实体。");
                var fields=page.Readable.Where(column=>column.Source.Id!=source.Source.Id&&!column.IsJsonArray&&
                    column.Type is "long?" or "string?"&&
                    (column.Source.LinkConfigTableId==parent.Source.Id||
                     column.Source.LinkConfigTableId.Length==0&&column.Source.ColumnName==parent.Source.TableName+"_id")).Take(2).ToArray();
                if(fields.Length!=1)return Gap("功能表页面须开启唯一的目标父表单字段。");
                sourceParent=fields[0];
                parents=[parent];
                globalParent=parent.IsGlobal;
                if(globalParent)
                {
                    if(targetBinding is not {Global:true}||logicalPage is null)return Gap("全局目标父需要显式发布的复选目标页。");
                    _=Metarill.Data.Queries.NonRedundantReads.CompileMultiGlobalParent(model.Catalog,target.Source.Id,logicalPage.Source.Id);
                }
            }
            // 控件和普通目标页的物理父键都须匹配来源已证明的同一张父表。
            if(!functionForm&&physicalParents.Any(scope=>sourceParent is null||Parent(scope.Column).Source.Id!=parents[0].Source.Id))
                return Gap("原parentId必须由来源页同一真实父表证明，不能猜测其他父范围。");
            if(!functionForm&&pageHasParent&&logicalPage is {} scopedPage)
            {
                var syntax=FixedConditionPlan.Compile(scopedPage.Source.WhereSql,target.Columns.Select(column=>column.Source.ColumnName));
                var binding=MultiTargetParentScope.Compile(model.Catalog,target.Source,syntax)!;
                var owner=pageRead?binding.Source(model.Catalog,table.Source,source.Source,page.Source)
                    :binding.Source(model.Catalog,table.Source,source.Source);
                globalParent=binding.Global;
                if(globalParent)_=Metarill.Data.Queries.NonRedundantReads.CompileMultiGlobalParent(model.Catalog,target.Source.Id,scopedPage.Source.Id);
                if(sourceParent?.Source.Id!=owner.Column.Id||(owner.LogicalPages.Count>0)!=(logicalSource is not null))
                    return Gap("复选目标页父参数缺少同一来源父记录。");
                scopes=scopes.Concat(scopedPage.Scopes.Where(scope=>scope.Source=="parent")).DistinctBy(scope=>(scope.Column.Source.Id,scope.Source)).ToArray();
            }
            // 只有显式关联了实际级联祖先时，原复选可直接沿来源的全局层位校验。
            var explicitGlobalPath=logicalSource is not null&&physicalParents.Length>0&&parents[0].IsGlobal&&
                physicalParents.All(scope=>scope.Column.Source.LinkConfigTableId==parents[0].Source.Id);
            if(parents.Any(parent=>Protected(parent)||!(parent.IsGlobal&&(globalParent||explicitGlobalPath))&&!HasIdentity(model,parent)))
                return Gap("来源父须具有普通业务项目归属或实际全局层位证明。");
            // 原非冗余候选优先采用控件排序，省略时继承实际目标页排序。
            var orderBy=definition.OrderBy;
            if(string.IsNullOrWhiteSpace(orderBy)&&table.Source.IsRongyu==0)
                orderBy=namePage?.Source.OrderBy??"";
            var order=GenerationModel.ValidateOrderBy(string.IsNullOrWhiteSpace(orderBy)?definition.NameField+" asc":orderBy,target.Columns).ToList();
            if(order.Any(item=>(!NonRedundantReadCompilation.Physical(model,target,item.Column)&&!(derived&&item.Column.Source.Id==name.Source.Id))||item.Column.IsJsonArray||ManagerCredentialCompilation.IsCredential(target,item.Column)))return Gap("候选排序必须为非凭据物理字段。");if(!order.Any(item=>item.Column.IsId))order.Add((target.Columns.Single(column=>column.IsId),false));
            var names=functionForm?SelectionDisplayFieldCompilation.FunctionFormNames(table,source,name):SelectionDisplayFieldCompilation.Resolve(table,source);
            if(!functionForm&&(names.Source.Id==source.Source.Id||names.Type!="string?"||table.Source.IsRongyu==1&&!MultiNameStorageCompilation.Supports(model,table,source,name,names)))return Gap("冗余模式需要已证明的同一关系名称数组缓存，非冗余使用只读联接或自动名称。");
            if(!functionForm&&table.Source.IsRongyu==0&&(!GenerationModel.Enabled(names.Source.Extra,"isJoinColumn")||names.Source.IsVirtualColumn!=0))return Gap("非冗余名称必须是明确只读联接或自动投影。");
            var result=new DatabaseCheckboxModel(source,target,name,names,scopes,conditions,order,parents,sourceParent,derived,globalPage)
            {
                LogicalTargetPage=logicalPage,
                NameTargetPage=namePage,
                PhysicalTargetPage=physicalPage,
                UsesPageRead=pageRead,
                FunctionParentField=functionForm?sourceParent:null,
                ExactTarget=exactTarget,
                LogicalSourceParent=logicalSource is not null,
                GlobalParent=globalParent,
                DerivedName=derived||logicalPage is not null
            };
            if(result.DerivedName)DerivedCheckboxNamesCompilation.Require(model,table,result);
            return new(result,null);
        }
        catch(Exception error)when(error is ArgumentException or InvalidOperationException or JsonException or NotSupportedException){return Gap(error.Message);}
    }
    /// <summary>名称数组是服务端管理的只读字段；保持原布局，不把普通名称列继续作为可写DTO输入。
    /// </summary>
    internal static GenerationModel Prepare(GenerationModel model)=>model with{Tables=model.Tables.Select(table=>
    {
        var managed=table.Pages.SelectMany(page=>ForService(model,table,page)).Select(def=>def.Names.Source.Id).ToHashSet(StringComparer.Ordinal);ColumnModel Fix(ColumnModel column)=>managed.Contains(column.Source.Id)?column with{IsManagedMultiNames=true}:column;IReadOnlyList<ColumnModel> All(IEnumerable<ColumnModel> columns)=>columns.Select(Fix).ToArray();
        return table with{Columns=All(table.Columns),Pages=table.Pages.Select(page=>page with{Readable=All(page.Readable),Writable=All(page.Writable.Where(column=>!managed.Contains(column.Source.Id))),Searchable=All(page.Searchable),Sortable=All(page.Sortable),ListColumns=All(page.ListColumns),DetailColumns=All(page.DetailColumns)}).ToArray()};
    }).ToArray()};
}
