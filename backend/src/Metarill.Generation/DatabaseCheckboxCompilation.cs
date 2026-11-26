using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;

namespace Metarill.Generation;

internal sealed record DatabaseCheckboxModel(ColumnModel Source,TableModel Target,ColumnModel Name,ColumnModel? Names,IReadOnlyList<(ColumnModel Column,string Source)> Scopes,RelationConditionModel? Conditions,IReadOnlyList<(ColumnModel Column,bool Descending)> Order,IReadOnlyList<TableModel> SourceParents,ColumnModel? SourceParent,bool DerivedName=false,PageModel? GlobalPage=null)
{
    // null 表示沿用原物理排序；空集合仍可以是已证明的固定联接。
    internal IReadOnlyList<NonRedundantSingleTargetScope>? CandidateOrderTargets { get; init; }
    internal PageModel? LogicalTargetPage { get; init; }
    internal bool CompoundTargetParents { get; init; }
    internal bool RealtimeLogicalParentScope { get; init; }
    internal bool GlobalRealtimeNameOverride { get; init; }
    internal bool GlobalPageOverride { get; init; }
    // 没有逻辑候选条件的显式目标页仍决定名称路径内每个关系角色。
    internal PageModel? NameTargetPage { get; init; }
    // 普通目标页的物理条件参与原候选、保存和 Excel。
    internal PageModel? PhysicalTargetPage { get; init; }
    internal bool UsesPageRead { get; init; }
    internal bool UsesControlRead { get; init; }
    internal bool UsesNameOverride { get; init; }
    internal ColumnModel? FunctionParentField { get; init; }
    internal ColumnModel? FunctionParentBusinessKey { get; init; }
    internal bool UsesFunctionTextParent => FunctionParentBusinessKey is { Type: "string?" };
    internal ColumnModel? FormParentField { get; init; }
    internal bool SharedAccountScope { get; init; }
    internal bool SharedDerivedPageScope { get; init; }
    internal bool SharedLogicalParentScope { get; init; }
    internal bool HasStoredIdentityScope { get; init; }
    internal bool HasStoredExactScope { get; init; }
    // 非冗余精确字典的物理父范围已经按实际发布字段和来源父键证明。
    internal bool HasExactPhysicalParentScope { get; init; }
    /// <summary>全称和简称使用实际发布的冗余字典项目及物理父范围。</summary>
    internal bool HasStoredGlobalParentScope => LogicalTargetPage is {} page&&
        FixedPageConditionCompilation.HasStoredGlobalMultiParentScope(Target,page);
    internal bool HasStoredStaticScope { get; init; }
    internal bool HasProjectStoredAccountScope { get; init; }
    internal bool CompoundPhysicalSource { get; init; }
    internal bool ProjectFormScope { get; init; }

    /// <summary>原列表勾选的加载和冗余数组匹配共用同一候选范围证明。</summary>
    internal bool SupportsRedundantArrayFilter =>
        FormParentField is not null || SharedLogicalParentScope || HasStoredIdentityScope || HasStoredStaticScope ||
        HasProjectStoredAccountScope || HasStoredGlobalParentScope || HasExactPhysicalParentScope || ProjectFormScope || SharedTextParent is not null;
    internal ExactDataManageModel? ExactTarget { get; init; }
    internal IReadOnlyList<(CrossTableReadModel Read,int Index)> LogicalSourcePaths { get; init; } = [];
    internal bool LogicalSourceParent => LogicalSourcePaths.Count>0;
    internal bool IndependentTextSource { get; init; }
    internal ColumnModel? SharedTextParent { get; init; }
    internal bool UsesPageTextParent=>LogicalSourceParent&&(IndependentTextSource||SharedTextParent is not null);
    internal IReadOnlyList<ColumnModel> SourceScopeColumns { get; init; } = [];
    internal IReadOnlyList<ColumnModel> CompoundSourceDependencies { get; init; } = [];
    internal PageModel? GlobalParentPage { get; init; }
    internal bool GlobalParent => GlobalParentPage is not null;
    internal bool IntegerLabel => Name.Type is ("int?" or "long?");
    internal bool TypedLabel => Metarill.Core.Contracts.MultiValueLabel.IsTyped(Name.Source.LogicalType);
    internal string Endpoint=>"findNameId"+GenerationModel.Pascal(Source.Source.ColumnName[..^4])+Source.Source.Id;
    internal IEnumerable<ColumnModel> Dependencies=>(Conditions?.Forms.Select(binding=>binding.Column)??[])
        .Concat(SourceScopeColumns).Concat(CompoundSourceDependencies)
        .Concat(SourceParent is null?[]:[SourceParent]).DistinctBy(column=>column.Source.Id);
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
    internal static IReadOnlyList<DatabaseCheckboxModel> ForService(GenerationModel model,TableModel table,PageModel page)=>table.Source.IsFunctionTable==1?ForPage(model,table,page):table.Columns.Where(column=>Generic(table,column)).Select(column=>Resolve(model,table,page,column,forService:true).Definition).OfType<DatabaseCheckboxModel>().ToArray();
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
    internal static DatabaseCheckboxResult Resolve(GenerationModel model,TableModel table,PageModel page,ColumnModel source,
        bool forService=false,bool rootDefinition=false)
    {
        DatabaseCheckboxResult Gap(string message)=>new(null,"数据库复选尚未支持："+message);
        if(!Generic(table,source))return Gap("固定安全表必须使用其专用协调器。");
        try
        {
            if(model.Catalog.DatabaseProvider!="mysql")return Gap("当前方言尚未完成复选成员及名称用途验收。");
            var functionForm=table.Source.IsFunctionTable==1;
            if(table.Source.IsRongyu is not(0 or 1)||source.Type!="string?"||!source.Source.ColumnName.EndsWith("_ids",StringComparison.Ordinal)||
                !functionForm&&!NonRedundantReadCompilation.Physical(model,table,source))return Gap("来源须为已声明的字符串_ids。");
            if(!functionForm&&!HasIdentity(model,table))return Gap("来源缺少真实物理主键或项目归属。");
            var baseline=DatabaseCheckboxDefinition.Parse(source.Source.ControlDetail);
            var field=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==source.Source.Id);
            if(field?.Extra.GetValueOrDefault("controlKind").ToString() is{Length:>0} kind&&kind!="MultiCheckBoxDatabase")
                return Gap("页级控件覆盖改变了原复选存储协议。");
            var definition=rootDefinition?baseline:baseline.WithPageOverride(field?.Extra.GetValueOrDefault("controlDetail").ToString());
            var target=Target(model,source,definition);if(target.Source.Id!=Target(model,source,baseline).Source.Id)return Gap("页级目标覆盖未绑定同一物理关系，不能错读名称。");
            if(Protected(target)||target.Source.IsFunctionTable==1)return Gap("目标必须是有记录的业务表。");
            PageModel? globalPage=null;
            PageModel? publishedGlobalPage=null;
            PageModel? defaultGlobalPage=null;
            var storedAccountPage=false;
            var storedProjectPage=false;
            var storedStaticPage=false;
            var scopedGlobalAccountPage=false;
            var scopedGlobalParentPage=false;
            var scopedGlobalExactPage=false;
            var storedExactPage=false;
            var globalPageOverride=false;
            if(target.IsGlobal&&!functionForm)
            {
                var proof=table.Source.IsRongyu==1?GlobalMultipleRelationTarget.RequireCached(model.Catalog,table.Source,source.Source,target.Source):GlobalMultipleRelationTarget.Require(model.Catalog,table.Source,source.Source,target.Source);
                defaultGlobalPage=target.Pages.Single(page=>page.Source.Id==proof.Page.Id);
                globalPageOverride=definition.PageId.Length>0&&definition.PageId!=proof.Page.Id;
                if(globalPageOverride||table.Source.IsRongyu==0&&definition.NameField!=baseline.NameField)
                {
                    if(table.Source.IsRongyu!=0)
                        return Gap("全局复选的物理名称副本必须沿用固定关系。");
                    proof=GlobalMultipleRelationTarget.RequirePage(model.Catalog,table.Source,source.Source,target.Source,page.Source);
                    if((definition.PageId.Length>0&&definition.PageId!=proof.Page.Id)||definition.NameField!=proof.Name.ColumnName)
                        return Gap("全局复选的目标页或名称与实际来源配置不一致。");
                }
                // 复选的搜索、回读与 Excel 必须沿已发布缓存目标页的同一固定范围。
                publishedGlobalPage=target.Pages.Single(page=>page.Source.Id==proof.Page.Id);
                // 物理字典的账号条件直接进入复选查询，名称副本仍沿同一发布页重算。
                storedAccountPage=proof.UsesStoredAccountPage;
                storedProjectPage=proof.UsesProjectPage;
                scopedGlobalExactPage=(table.IsGlobal||table.Source.IsRongyu==1)&&proof.UsesNonRedundantExactCheckboxPage;
                scopedGlobalAccountPage=(table.IsGlobal||scopedGlobalExactPage)&&proof.UsesNonRedundantAccountPage;
                scopedGlobalParentPage=proof.UsesPhysicalParentPage;
                storedExactPage=proof.UsesStoredExactCheckboxPage;
                storedStaticPage=(table.IsGlobal||storedExactPage||scopedGlobalExactPage)&&proof.UsesStoredStaticPage;
                if(globalPageOverride||storedAccountPage||storedProjectPage||storedStaticPage||scopedGlobalParentPage||scopedGlobalExactPage||NonRedundantReadCompilation.HasGlobalRowProject(target)||FixedPageConditionCompilation.HasMultiTargetScope(model,target,publishedGlobalPage,includeProjectFixedScope:false)||
                    GlobalPhysicalRelationValue.IsPublishedTypedCache(model.Catalog,MetadataStoragePolicy.ForTable(model.Catalog,target.Source),target.Source,proof.Name)||
                    table.Source.IsRongyu==1&&publishedGlobalPage.Condition is not null)
                    globalPage=publishedGlobalPage;
            }
            var storedIdentityPage=storedAccountPage||storedProjectPage||scopedGlobalAccountPage;
            if(table.IsGlobal&&!storedIdentityPage&&!storedStaticPage&&!scopedGlobalParentPage&&!scopedGlobalExactPage)
                return Gap("全局业务来源必须绑定受支持的已发布字典页。");
            if(storedAccountPage&&table.Source.IsRongyu==1&&!functionForm&&
                !baseline.HasSameCondition(definition,target.Columns.Select(column=>column.Source.ColumnName)))
                return Gap("同一名称副本的页面必须沿用表级复选候选条件。");
            var usesNameOverride=definition.NameField!=baseline.NameField;
            var name=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==definition.NameField);
            var idLabel=name is not null&&name.IsId&&name.Type=="long"&&NonRedundantReadCompilation.Physical(model,target,name);
            var scalarLabel=name is not null&&MultiSelectCompilation.SupportsTypedLabel(model,table,source,target,name);
            if(name is null||!idLabel&&name.Type!="string?"&&!scalarLabel||name.IsJsonArray||ManagerCredentialCompilation.IsCredential(target,name))
                return Gap("名称必须为已声明的非凭据且已证明的标量。");
            var derived=!NonRedundantReadCompilation.Physical(model,target,name);if(!derived&&GenerationModel.Enabled(name.Source.Extra,"isJoinColumn")&&(!target.IsGlobal||!GlobalPhysicalRelationValue.IsPublishedScalar(model.Catalog,MetadataStoragePolicy.ForTable(model.Catalog,target.Source),target.Source,name.Source)))return Gap("物理Join名称仍需独立缓存证明。");
            if(!target.IsGlobal&&!HasIdentity(model,target))return Gap("目标缺少真实项目归属。");
            var linkedPage=source.Source.Extra.GetValueOrDefault("linkConfigPageId").ToString();
            if(linkedPage.Length==0&&publishedGlobalPage is not null)linkedPage=publishedGlobalPage.Source.Id;
            var explicitPage=definition.PageId.Length>0?definition.PageId:linkedPage;
            var namePage=target.Pages.SingleOrDefault(candidate=>candidate.Source.Id==explicitPage);
            if(functionForm&&explicitPage.Length>0&&namePage is null)return Gap("功能表复选的显式目标页未在实际目标表发布。");
            var exactTarget=namePage is null?null:ExactDataManageCompilation.Resolve(model,target,namePage);
            var exactPhysicalParent=scopedGlobalParentPage&&exactTarget is not null&&target.Source.IsRongyu==0;
            var fixedPage=baseline.PageId.Length>0?baseline.PageId:linkedPage;
            bool UsesProjectStoredAccount(PageModel candidate)=>!functionForm&&
                Metarill.Data.Queries.NonRedundantReads.IsProjectStoredCheckboxAccountPage(model.Catalog,target.Source,candidate.Source);
            var projectStoredAccount=namePage is not null&&UsesProjectStoredAccount(namePage);
            // 普通项目字典的全局父仍按实际发布页核对，目标行继续按当前项目和账号限定。
            var projectStoredGlobalParent=projectStoredAccount&&namePage!.Scopes.Any(scope=>scope.Source=="parent")&&
                MultiTargetParentScope.CompileCheckboxPage(model.Catalog,target.Source,
                    FixedConditionPlan.Compile(namePage.Source.WhereSql,target.Columns.Select(column=>column.Source.ColumnName)),
                    allowGlobalParent:true,allowStoredPhysicalParents:true) is {Global:true} storedGlobalBinding
                ?storedGlobalBinding:null;
            if(table.Source.IsRongyu==1&&explicitPage!=fixedPage&&(projectStoredAccount||
                target.Pages.Any(candidate=>candidate.Source.Id==fixedPage&&UsesProjectStoredAccount(candidate))))
                return Gap("复选物理名称副本必须沿用表级账号候选范围。");
            var pageNames=table.Source.IsRongyu==1&&!functionForm&&namePage is not null&&namePage.Source.Id==fixedPage
                ?model.Catalog.ConfigPageColumns.Where(setting=>setting.ConfigColumnId==source.Source.Id&&
                    table.Pages.Any(candidate=>candidate.Source.Id==setting.ConfigPageId))
                    .Select(setting=>baseline.WithPageOverride(setting.Extra.GetValueOrDefault("controlDetail").ToString()).NameField)
                    .ToHashSet(StringComparer.Ordinal):[];
            var sharedNamesPage=pageNames.Any(field=>field!=baseline.NameField);
            // 普通物理父范围由实际两端页面证明，逻辑来源继续使用已有的级联路径。
            var sourceScopeColumns=page.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column).ToArray();
            var sharedTextParent=namePage is null?null:SharedRedundantParentCompilation.CommonTextParent(model,table,page,target,namePage,allowSparseTarget:true)??
                NonRedundantCrossParentCompilation.CommonTextParent(model,table,page,target,namePage,allowStoredTarget:true);
            // 文本父键沿已证明祖先比较，非冗余字典的名称联接沿同一发布页独立验证。
            if(sharedTextParent is not null&&(name.Type!="string?"&&!scalarLabel||
                explicitPage!=fixedPage||target.Source.IsRongyu!=0&&(derived||FixedPageConditionCompilation.HasLogicalFields(namePage!)||
                    exactTarget is not null&&!ExactDataManageCompilation.IsDirectPhysicalSource(exactTarget))))
                return Gap("共用文本父键的复选需要表级发布的标量名称和已证明的页面条件。");
            var ordinaryCompoundParent=!functionForm&&!table.IsGlobal&&exactTarget is null&&namePage is {} actualTarget&&
                (sourceScopeColumns.Length>1||actualTarget.Scopes.Count(scope=>scope.Source=="parent")>1)&&
                !sourceScopeColumns.Any(column=>NonRedundantCrossParentCompilation.Managed(model,table,page,column))
                ?projectStoredGlobalParent??MultiTargetParentScope.CompileProjectCheckboxPhysicalPage(model.Catalog,target.Source,actualTarget.Source)
                :null;
            // 保存按实际身份读取，维护从每条来源记录的持久归属绑定同一账号范围。
            var sharedFixedR0Page=sharedNamesPage&&target.Source.IsRongyu==0&&exactTarget is null&&
                !FixedPageConditionCompilation.HasLogicalFields(namePage!);
            var sharedScalarPage=sharedFixedR0Page&&namePage!.Scopes.All(scope=>scope.Source=="project");
            var sharedStoredAccountScope=sharedNamesPage&&storedIdentityPage;
            var sharedAccountScope=sharedStoredAccountScope||projectStoredAccount&&table.Source.IsRongyu==1||
                sharedScalarPage&&FixedPageConditionCompilation.HasAccountVariables(namePage!);
            var literalConditions=sharedFixedR0Page&&(namePage!.Condition is null||namePage.Condition.Syntax.Variables.Count==0);
            var sharedLiteralScope=sharedScalarPage&&literalConditions;
            // 普通物理目标父沿用原路由及独立表单的持久外键证明。
            var sharedPhysicalParentScope=literalConditions&&!target.IsGlobal&&
                (ordinaryCompoundParent is not null||namePage!.Scopes.Count(scope=>scope.Source=="parent")==1&&namePage.Scopes.Any(scope=>
                    scope.Source=="parent"&&NonRedundantReadCompilation.Physical(model,target,scope.Column)&&
                    !NonRedundantCrossParentCompilation.Managed(model,target,namePage,scope.Column)));
            if(sharedAccountScope&&!sharedStoredAccountScope&&!projectStoredAccount)
                _=Metarill.Data.Queries.AccountScopedCrossCache.Compile(model.Catalog,table.Source,target.Source,namePage!.Source);
            // 任一副本来自联接时，各页的写入口都必须使用可以一次投影全部名称的计划。
            var sharedDerivedPageScope=(!sharedStoredAccountScope&&sharedAccountScope||sharedLiteralScope||sharedPhysicalParentScope)&&
                target.Columns.Any(label=>
                (label.Source.ColumnName==baseline.NameField||pageNames.Contains(label.Source.ColumnName))&&
                !NonRedundantReadCompilation.Physical(model,target,label));
            // 多个目标父层位仍由同一发布页证明，所有名称共用同一个来源父键。
            var sharedLogicalParentScope=sharedNamesPage&&target.Source.IsRongyu==0&&exactTarget is null&&
                namePage!.Scopes.Any(scope=>scope.Source=="parent"&&NonRedundantCrossParentCompilation.Managed(model,target,namePage,scope.Column));
            if(usesNameOverride)
            {
                // 非冗余精确复选沿实际发布页投影已开放的物理或固定联接文本。
                var globalExactName=target.IsGlobal&&(target.Source.IsRongyu==0||storedExactPage)&&exactTarget is not null;
                var realtimeFixedName=table.Source.IsRongyu==0&&
                    (target.Source.IsRongyu==0||storedIdentityPage||storedStaticPage||scopedGlobalParentPage)&&
                    (exactTarget is null||globalExactName||scopedGlobalParentPage&&target.Source.IsRongyu==1)&&
                    namePage is not null&&(namePage.Source.Id==fixedPage||globalPageOverride);
                var storage=MetadataStoragePolicy.ForTable(model.Catalog,target.Source);
                bool TextLabel(ColumnModel label,PageModel? labelPage)=>target.IsGlobal&&
                    (realtimeFixedName||scopedGlobalExactPage&&table.Source.IsRongyu==1)&&target.Source.IsRongyu==0
                    ?GlobalMultipleRelationTarget.IsPublishedCheckboxText(model.Catalog,target.Source,label.Source,labelPage?.Source)
                    :MultiSelectCompilation.PhysicalTextLabel(model,target,label)||
                        (sharedLogicalParentScope||sharedDerivedPageScope||realtimeFixedName)&&
                        MultiNameStorageColumns.IsDerivedCheckboxText(target.Source,storage,label.Source);
                var baseName=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==baseline.NameField);
                // 表级全称和页级简称允许各自使用已发布的不同固定联接。
                if(functionForm||target.IsGlobal&&table.Source.IsRongyu!=1&&!realtimeFixedName||baseName is null||!TextLabel(baseName,defaultGlobalPage??namePage)||
                    !TextLabel(name,namePage)||namePage is not null&&!namePage.Readable.Any(column=>column.Source.Id==name.Source.Id))
                    return Gap("页级名称须沿用同一固定文本关系，显式目标页必须已开放该字段。");
            }
            // 功能表没有持久化来源值。纯物理条件直接使用已绑定的账号和当前表单父键。
            var compoundTargetParents=!functionForm&&target.Source.IsRongyu==0&&
                namePage?.Scopes.Count(scope=>scope.Source=="parent")>1;
            var logicalPage=(sharedTextParent is null||target.Source.IsRongyu==0)&&ordinaryCompoundParent is null&&!sharedAccountScope&&!storedIdentityPage&&namePage is not null&&
                (compoundTargetParents||FixedPageConditionCompilation.HasMultiTargetScope(model,target,namePage,includeProjectFixedScope:false))&&
                (!functionForm||FixedPageConditionCompilation.HasLogicalFields(namePage)||namePage.Scopes.Any(scope=>scope.Source=="parent"&&
                    (!NonRedundantReadCompilation.Physical(model,target,scope.Column)||
                     CrossTableStorageColumns.IsLogicalPathField(target.Source,model.Catalog.ConfigColumns,scope.Column.Source.ColumnName)||
                     target.IsGlobal&&model.Tables.Any(parent=>parent.IsGlobal&&parent.Source.Id==scope.Column.Source.LinkConfigTableId))))?namePage:null;
            // 同一候选即使由表级继承，也必须用当前普通非冗余来源页的父键重新证明。
            var pageRead=globalPageOverride||namePage is not null&&table.Source.IsRongyu==0&&target.Source.IsRongyu==0&&!target.IsGlobal&&
                (explicitPage!=fixedPage||logicalPage?.Scopes.Any(scope=>scope.Source=="parent")==true);
            if(explicitPage!=fixedPage&&!pageRead&&(logicalPage is not null||target.Pages.Any(candidate=>candidate.Source.Id==fixedPage&&FixedPageConditionCompilation.HasMultiTargetScope(model,target,candidate,includeProjectFixedScope:false))))
                return Gap("复选不能用页覆盖替换表级固定范围。");
            var whereSql=definition.WhereSql;
            if((storedProjectPage||storedStaticPage||storedExactPage||scopedGlobalAccountPage||
                scopedGlobalParentPage&&(target.Source.IsRongyu==1||scopedGlobalExactPage))&&table.Source.IsRongyu==1)
            {
                var fields=target.Columns.Select(column=>column.Source.ColumnName).ToArray();
                var publishedCondition=FixedConditionPlan.Compile(publishedGlobalPage!.Source.WhereSql,fields);
                if(!definition.IsCoveredByStaticPage(publishedCondition,fields))
                    return Gap("全局复选的冗余名称要求控件和页面覆盖条件由实际发布字典页覆盖。");
                // 发布字典页已经应用这些条件，物理副本和候选共用一份过滤。
                whereSql="";
            }
            var projectForm=table.Source.IsRongyu==1&&!functionForm&&!table.IsGlobal&&exactTarget is null&&
                namePage is {} formPage&&explicitPage==fixedPage&&logicalPage is null
                ?baseline.AdditionalPhysicalFormConditions(model.Catalog,target.Source,formPage.Source):null;
            if(projectForm is not null)
            {
                if(projectForm!=definition.AdditionalPhysicalFormConditions(model.Catalog,target.Source,namePage!.Source))
                    return Gap("同一名称副本的页面必须沿用表级复选候选条件。");
                whereSql=projectForm;
            }
            if(projectStoredAccount&&table.Source.IsRongyu==1)
            {
                // 缓存只能重现表级候选与物理记录条件。重复项由实际字典页执行，等价页可独立选择显示名称。
                var targetFields=target.Columns.Select(column=>column.Source.ColumnName);
                whereSql=definition.AdditionalProjectConditions(namePage!.Source.WhereSql,targetFields);
                if(whereSql!=baseline.AdditionalProjectConditions(namePage.Source.WhereSql,targetFields))
                    return Gap("同一名称副本的页面必须沿用表级复选候选条件。");
            }
            var (scopes,conditions)=RelationConditionsCompilation.Compile(model,table,page,target,whereSql);
            if(sharedTextParent is not null&&scopes.Any(scope=>scope.Source=="parent"&&scope.Column!=sharedTextParent))
                return Gap("复选的控件父范围必须与发布的文本父字段一致。");
            if(projectForm is not null&&page.Searchable.Contains(source)&&conditions!.Forms.Any(binding=>
                !sourceScopeColumns.Contains(binding.Column)&&!ControlRenderer.HasScalarSourceFilter(model,page,binding.Column)))
                return Gap("启用复选列表筛选时须开启表单依赖字段的标量筛选。");
            var realtimeRead=table.Source.IsRongyu==0&&!functionForm;
            var globalRealtimeNameOverride=realtimeRead&&target.IsGlobal&&target.Source.IsRongyu==0&&usesNameOverride;
            var controlRead=realtimeRead&&(conditions is not null||
                scopes.Any(scope=>scope.Source=="parent"||target.IsGlobal&&scope.Source=="project"));
            // 固定页的项目条件进入候选、写和 Excel，实时名称已从实际发布页读取该条件。
            if(storedIdentityPage)
                scopes=scopes.Concat(publishedGlobalPage!.Scopes)
                    .DistinctBy(scope=>(scope.Column.Source.Id,scope.Source)).ToArray();
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
            // 名称页和简称页共用声明的常量、物理父键或已证明的账号条件。
            var sharedFixedPage=sharedAccountScope||sharedNamesPage&&!target.IsGlobal&&namePage is not null&&
                (namePage.Condition is {Syntax.Variables.Count:0}||namePage.Condition is null&&namePage.Scopes.Any(scope=>scope.Source=="parent"))&&
                !FixedPageConditionCompilation.HasLogicalFields(namePage)&&namePage.Scopes.Count(scope=>scope.Source=="parent")<=1;
            // 普通全局派生名称没有行项目列时，固定发布条件由复选查询显式合并。
            if((sharedTextParent is not null||realtimeRead||functionForm||globalPage is not null||exactTarget is not null||sharedFixedPage||projectStoredAccount||ordinaryCompoundParent is not null||projectForm is not null)&&!scopeAlreadyBound&&namePage is not null&&
                (functionForm||projectForm is not null||namePage.Condition is not null||namePage.Scopes.Any(scope=>scope.Source=="parent")))
            {
                physicalPage=namePage;
                scopes=scopes.Concat(physicalPage.Scopes.Where(scope=>scope.Source=="parent"||target.IsGlobal))
                    .DistinctBy(scope=>(scope.Column.Source.Id,scope.Source)).ToArray();
            }
            if((functionForm||sharedTextParent is not null)&&logicalPage is not null)
                scopes=scopes.Concat(logicalPage.Scopes.Where(scope=>scope.Source=="parent"))
                    .DistinctBy(scope=>(scope.Column.Source.Id,scope.Source)).ToArray();
            bool PublishedLogicalParent(ColumnModel column)=>(functionForm||column==sharedTextParent)&&logicalPage is not null&&
                NonRedundantCrossParentCompilation.Managed(model,target,logicalPage,column);
            if(scopes.Any(scope=>scope.Column.Type is not("long?" or "int?" or "string?")||!NonRedundantReadCompilation.Physical(model,target,scope.Column)&&!PublishedLogicalParent(scope.Column)||scope.Column.IsJsonArray||ManagerCredentialCompilation.IsCredential(target,scope.Column)))return Gap("身份／父范围必须绑定真实物理ID标量或已发布的逻辑父级。");
            TableModel Parent(ColumnModel column)=>model.Tables.SingleOrDefault(parent=>column.Source.LinkConfigTableId.Length>0?parent.Source.Id==column.Source.LinkConfigTableId:parent.Source.TableName+"_id"==column.Source.ColumnName)??throw new InvalidOperationException("复选父范围缺少明确真实父表。");
            var textSource=CrossTableReadCompilation.TextSourceParent(model,table,page);
            if(sourceScopeColumns.Any(column=>(column!=textSource&&!NonRedundantReadCompilation.Physical(model,table,column))||column.IsJsonArray||
                column.Type is not("long?" or "string?")))return Gap("来源父必须为固定标量ID。");
            var sourceBindings=sourceScopeColumns.Select(column=>(Column:column,Path:NonRedundantCrossParentCompilation.Find(model,table,page,column))).ToArray();
            var parents=sourceBindings.Select(binding=>binding.Path is {} path?path.Read.Levels[path.Index].Target:
                    binding.Column==textSource?SharedRedundantParentCompilation.ForColumn(model,table,page,binding.Column).Single().Level.Target:Parent(binding.Column))
                .DistinctBy(parent=>parent.Source.Id).ToArray();
            if(parents.Length>1)return Gap("多个来源范围必须绑定同一真实父表。");
            // 目标只有一个 parentId；用可还原的逻辑键作为来源代表，其他同父范围仍逐条验证。
            var logicalBindings=sourceBindings.Where(binding=>binding.Path is not null).ToArray();
            var logicalSource=logicalBindings.Length>0;
            var sourceParent=logicalBindings.FirstOrDefault().Column??(projectStoredAccount
                ?sourceScopeColumns.MinBy(column=>column.Source.Id,StringComparer.Ordinal):sourceScopeColumns.FirstOrDefault());
            var physicalParents=scopes.Where(scope=>scope.Source=="parent").ToArray();
            var ordinaryCompoundSource=ordinaryCompoundParent is not null&&sourceScopeColumns.Length>1;
            if(ordinaryCompoundSource)
            {
                var binding=ordinaryCompoundParent!;
                var owner=binding.CheckboxSource(model.Catalog,table.Source,source.Source,page.Source,allowProjectPhysicalColumns:true);
                if(owner.LogicalPages.Count>0||physicalParents.Any(scope=>!binding.HasTarget(scope.Column.Source.Id))||
                    !owner.AdditionalColumns.Select(column=>column.Id).Append(owner.Column.Id).ToHashSet(StringComparer.Ordinal)
                        .SetEquals(sourceScopeColumns.Select(column=>column.Source.Id)))
                    return Gap("复选来源页必须证明完整的同父物理字段。");
                if(binding.Global)
                    Metarill.Data.Queries.NonRedundantReads.RequireCompoundGlobalSourceParent(model.Catalog,target.Source.Id,namePage!.Source.Id,owner);
                sourceParent=sourceScopeColumns.Single(column=>column.Source.Id==owner.Column.Id);
            }
            var pageHasParent=logicalPage?.Scopes.Any(scope=>scope.Source=="parent")==true;
            var independentTextSource=textSource is not null&&physicalParents.Length==0&&!pageHasParent&&
                namePage?.Scopes.Any(scope=>scope.Source=="parent")!=true;
            if(textSource is not null&&!independentTextSource&&sharedTextParent is null)
                return Gap("复选目标父尚未证明使用与来源相同的文本业务键。");
            if(logicalSource&&!independentTextSource&&physicalParents.Length==0&&!pageHasParent)
                return Gap("来源逻辑父键须对应明确的控件或目标页父范围。");
            PageModel? globalParentPage=null;
            if(projectStoredGlobalParent is not null)
            {
                _=Metarill.Data.Queries.NonRedundantReads.CompileMultiGlobalParent(model.Catalog,target.Source.Id,namePage!.Source.Id);
                globalParentPage=namePage;
            }
            var compoundGlobalSource=false;
            ColumnModel? functionParentBusinessKey = null;
            if(functionForm&&physicalParents.Length>0)
            {
                var targetParent=physicalParents[0].Column;
                // 功能表只提供一个父值。多列须由同一实际发布页证明属于同一父记录。
                MultiTargetParentScope? targetBinding=null;
                if(namePage is {} parentPage&&physicalParents.All(scope=>parentPage.Scopes.Any(published=>
                    published.Source=="parent"&&published.Column.Source.Id==scope.Column.Source.Id)))
                {
                    targetBinding=GlobalCrossTableTarget.DeclaresExactPage(target.Source,parentPage.Source)
                        ?GlobalCrossTableTarget.ExactPageParent(model.Catalog,target.Source,parentPage.Source)
                        :GlobalCrossTableTarget.StoredMultiParent(model.Catalog,target.Source,parentPage.Source,checkbox:true);
                    if(targetBinding is null&&physicalParents.Length>1&&!target.IsGlobal&&
                        Metarill.Data.Queries.NonRedundantReads.IsProjectExactCheckboxGlobalParent(model.Catalog,target.Source,parentPage.Source))
                        targetBinding=MultiGlobalParentCompilation.CheckboxPageParent(model,target,parentPage);
                    if(targetBinding is null&&physicalParents.Length==1)
                        targetBinding=MultiTargetParentScope.Compile(model.Catalog,target.Source,
                            FixedConditionPlan.Compile(parentPage.Source.WhereSql,target.Columns.Select(column=>column.Source.ColumnName)));
                }
                if(physicalParents.Length>1&&(targetBinding is not {Logical:false}||
                    physicalParents.Any(scope=>!targetBinding.HasTarget(scope.Column.Source.Id))))
                    return Gap("功能表复选的多个目标父字段须绑定同一实际发布父页。");
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
                // 逻辑父层以发布的唯一业务键为准，物理父字段和未指定业务键的层位沿用主键。
                if (!parent.IsGlobal && logicalPage is { } parentScope &&
                    NonRedundantCrossParentCompilation.Managed(model, target, parentScope, targetParent))
                {
                    var path = NonRedundantCrossParentCompilation.Find(model, target, parentScope, targetParent)!.Value;
                    var level = path.Read.Levels[path.Index];
                    if (level.Target.Source.Id != parent.Source.Id)
                        return Gap("功能表复选未证明目标父层对应的业务键。");
                    functionParentBusinessKey = level.AlternateKey;
                }
                if(parent.IsGlobal)
                {
                    if(targetBinding is not {Global:true}||namePage is null)return Gap("全局目标父需要显式发布的复选目标页。");
                    _=Metarill.Data.Queries.NonRedundantReads.CompileMultiGlobalParent(model.Catalog,target.Source.Id,namePage.Source.Id);
                    globalParentPage=namePage;
                }
            }
            ColumnModel? formParent=null;
            void BindFormParent(MultiTargetParentScope binding,MultiSourceParent owner)
            {
                var field=table.Columns.Single(column=>column.Source.Id==owner.Column.Id);
                if(page.Searchable.Contains(source)&&!ControlRenderer.HasScalarSourceFilter(model,page,field))
                    throw new InvalidOperationException("启用复选列表筛选时须开启表单父字段的标量筛选。");
                formParent=sourceParent=field;
                parents=[model.Tables.Single(candidate=>candidate.Source.Id==binding.Parent.Id)];
            }

            // 无页面父路由时，固定目标页的实际父字段绑定来源详情中唯一的真实表单外键。
            var ordinaryFormParent=ordinaryCompoundParent??(sourceScopeColumns.Length==0&&realtimeRead&&!table.IsGlobal&&
                exactTarget is null&&physicalPage is {} published&&physicalParents.Length==1
                ?MultiTargetParentScope.CompileProjectCheckboxPhysicalPage(model.Catalog,target.Source,published.Source)
                :null);
            if(sourceScopeColumns.Length==0&&(sharedFixedPage&&physicalParents.Length==1||
                projectStoredAccount&&physicalParents.Length>0||ordinaryFormParent is not null))
            {
                var binding=ordinaryFormParent;
                if(binding is null)
                {
                    var syntax=FixedConditionPlan.Compile(namePage!.Source.WhereSql,target.Columns.Select(column=>column.Source.ColumnName));
                    binding=projectStoredAccount
                        ?MultiTargetParentScope.CompileCheckboxPage(model.Catalog,target.Source,syntax,allowGlobalParent:true,allowStoredPhysicalParents:true)
                        :MultiTargetParentScope.CompileCheckboxParent(model.Catalog,target.Source,syntax);
                }
                if(binding is null||binding.Global&&projectStoredGlobalParent is null||binding.Logical||physicalParents.Any(scope=>!binding.HasTarget(scope.Column.Source.Id))||
                    Protected(model.Tables.Single(candidate=>candidate.Source.Id==binding.Parent.Id)))
                    return Gap("表单父范围须绑定实际普通目标父键。");
                BindFormParent(binding,binding.CheckboxSource(model.Catalog,table.Source,source.Source,page.Source,allowFormParent:true));
            }
            if(!functionForm&&pageHasParent&&logicalPage is {} scopedPage)
            {
                var syntax=FixedConditionPlan.Compile(scopedPage.Source.WhereSql,target.Columns.Select(column=>column.Source.ColumnName));
                var storedGlobalParent=GlobalCrossTableTarget.StoredMultiParent(model.Catalog,target.Source,scopedPage.Source,
                    checkbox:true,allowExact:exactTarget is not null);
                var binding=(storedGlobalParent??(target.IsGlobal
                    ?MultiTargetParentScope.CompileGlobalPage(model.Catalog,target.Source,syntax,checkbox:true)
                    :MultiTargetParentScope.CompileCheckboxPage(model.Catalog,target.Source,syntax,allowGlobalParent:true)))!;
                // 保留实际来源页的父参数。无父路由的精确物理字典从已开放的表单外键取得父值。
                var usesFormParent=sharedLogicalParentScope||storedGlobalParent is not null||exactPhysicalParent;
                var owner=globalPageOverride?binding.CheckboxSource(model.Catalog,table.Source,source.Source,page.Source,
                    allowFormParent:exactPhysicalParent||storedGlobalParent is not null):
                    usesFormParent?binding.CheckboxSource(model.Catalog,table.Source,source.Source):
                    pageRead?binding.Source(model.Catalog,table.Source,source.Source,page.Source):
                    binding.Source(model.Catalog,table.Source,source.Source);
                if(sourceScopeColumns.Length==0&&usesFormParent)
                    BindFormParent(binding,owner);
                if(binding.Global)
                {
                    _=Metarill.Data.Queries.NonRedundantReads.CompileMultiGlobalParent(model.Catalog,target.Source.Id,scopedPage.Source.Id);
                    globalParentPage=scopedPage;
                    if(owner.AdditionalColumns.Count>0)
                    {
                        Metarill.Data.Queries.NonRedundantReads.RequireCompoundGlobalSourceParent(model.Catalog,target.Source.Id,scopedPage.Source.Id,owner);
                        sourceParent=sourceScopeColumns.SingleOrDefault(field=>field.Source.Id==owner.Column.Id);
                        compoundGlobalSource=true;
                    }
                }
                if(sourceParent?.Source.Id!=owner.Column.Id||(owner.LogicalPages.Count>0)!=logicalSource)
                    return Gap("复选目标页父参数缺少同一来源父记录。");
                scopes=scopes.Concat(scopedPage.Scopes.Where(scope=>scope.Source=="parent")).DistinctBy(scope=>(scope.Column.Source.Id,scope.Source)).ToArray();
            }
            // 控件物理范围必须匹配由路由或详情表单证明的同一张实际父表。
            if(!functionForm&&physicalParents.Any(scope=>scope.Column!=sharedTextParent&&(sourceParent is null||Parent(scope.Column).Source.Id!=parents[0].Source.Id)))
                return Gap("原parentId必须由来源页同一真实父表证明，不能猜测其他父范围。");
            // 只有显式关联了实际级联祖先时，原复选可直接沿来源的全局层位校验。
            var explicitGlobalPath=logicalSource&&physicalParents.Length>0&&parents[0].IsGlobal&&
                physicalParents.All(scope=>scope.Column.Source.LinkConfigTableId==parents[0].Source.Id);
            if(parents.Any(parent=>Protected(parent)||!(parent.IsGlobal&&(globalParentPage is not null||explicitGlobalPath||independentTextSource))&&!HasIdentity(model,parent)))
                return Gap("来源父须具有普通业务项目归属或实际全局层位证明。");
            // 原非冗余候选优先采用控件排序，省略时继承实际目标页排序。
            var orderBy=definition.OrderBy;
            if(string.IsNullOrWhiteSpace(orderBy)&&table.Source.IsRongyu==0)
                orderBy=namePage?.Source.OrderBy??"";
            var order=GenerationModel.ValidateOrderBy(string.IsNullOrWhiteSpace(orderBy)?definition.NameField+" asc":orderBy,target.Columns).ToList();
            if (order.Any(item => item.Column.IsJsonArray || ManagerCredentialCompilation.IsCredential(target, item.Column)))
                return Gap("候选排序须为非凭据标量。");
            var joinedOrder = order.Select(item => item.Column).Where(column =>
                !NonRedundantReadCompilation.Physical(model, target, column) && !(derived && column.Source.Id == name.Source.Id)).ToArray();
            if (joinedOrder.Length > 0 && (namePage is null || joinedOrder.Any(column =>
                    !CascadeTargetOrderCompilation.CanUseJoin(model, target, namePage, column))))
                return Gap("候选排序须为物理标量或实际目标页开放的固定单值联接。");
            if (!order.Any(item => item.Column.IsId)) order.Add((target.Columns.Single(column => column.IsId), false));
            var independentCache=table.Source.IsRongyu==1&&usesNameOverride;
            var names=idLabel?null:functionForm?SelectionDisplayFieldCompilation.FunctionFormNames(table,source,name):
                independentCache?MultiNameStorageCompilation.FindPhysicalName(table,source,name):SelectionDisplayFieldCompilation.Resolve(table,source);
            if(!idLabel&&(names is null||!functionForm&&(names.Source.Id==source.Source.Id||names.Type!="string?"||table.Source.IsRongyu==1&&!MultiNameStorageCompilation.Supports(model,table,source,name,names))))return Gap("冗余模式需要已证明的同一关系名称数组缓存，非冗余使用只读联接或自动名称。");
            if(independentCache&&names is not null&&!forService&&!page.Readable.Any(column=>column.Source.Id==names.Source.Id))
                return Gap("本页必须开放简称对应的物理名称数组。");
            if(!idLabel&&!functionForm&&table.Source.IsRongyu==0&&(!GenerationModel.Enabled(names!.Source.Extra,"isJoinColumn")||names.Source.IsVirtualColumn!=0))return Gap("非冗余名称必须是明确只读联接或自动投影。");
            var result=new DatabaseCheckboxModel(source,target,name,names,scopes,conditions,order,parents,sourceParent,derived,globalPage)
            {
                CandidateOrderTargets=joinedOrder.Length==0?null:RequireOrderTargets(model,target,namePage!,order),
                LogicalTargetPage=logicalPage,
                CompoundTargetParents=compoundTargetParents,
                NameTargetPage=namePage,
                PhysicalTargetPage=physicalPage,
                // 普通目标页的物理范围也要进入业务行的回显和名称筛选。
                UsesPageRead=pageRead||realtimeRead&&(physicalPage is not null||
                    target.IsGlobal&&publishedGlobalPage is not null&&(controlRead||globalPage is not null||logicalPage is not null)),
                UsesControlRead=controlRead,
                UsesNameOverride=usesNameOverride,
                FunctionParentField=functionForm?sourceParent:null,
                FunctionParentBusinessKey = functionParentBusinessKey,
                FormParentField=formParent,
                SharedAccountScope=sharedAccountScope,
                SharedDerivedPageScope=sharedDerivedPageScope,
                SharedLogicalParentScope=sharedLogicalParentScope,
                HasStoredIdentityScope=storedIdentityPage,
                HasStoredExactScope=storedExactPage||table.Source.IsRongyu==1&&scopedGlobalExactPage,
                HasExactPhysicalParentScope=exactPhysicalParent,
                HasStoredStaticScope=storedStaticPage,
                HasProjectStoredAccountScope=projectStoredAccount,
                CompoundPhysicalSource=ordinaryCompoundSource||projectStoredAccount&&sourceScopeColumns.Length>1,
                ProjectFormScope=projectForm is not null,
                GlobalRealtimeNameOverride=globalRealtimeNameOverride,
                GlobalPageOverride=globalPageOverride,
                RealtimeLogicalParentScope=realtimeRead&&exactTarget is null&&logicalPage is not null&&
                    logicalPage.Scopes.Any(scope=>scope.Source=="parent"&&NonRedundantCrossParentCompilation.Managed(model,target,logicalPage,scope.Column)),
                ExactTarget=exactTarget,
                IndependentTextSource=independentTextSource,
                SharedTextParent=sharedTextParent,
                LogicalSourcePaths=logicalBindings.Select(binding=>binding.Path!.Value)
                    .DistinctBy(path=>(path.Read.Source.Source.Id,path.Index)).ToArray(),
                SourceScopeColumns=sourceScopeColumns,
                CompoundSourceDependencies=compoundGlobalSource
                    ?NonRedundantCrossParentCompilation.Dependencies(model,table,page,sourceScopeColumns):[],
                GlobalParentPage=globalParentPage,
                // 全局业务的精确字典由固定查询合并实际字典条件与当前角色，物理显示名同样经过该计划。
                DerivedName=derived||logicalPage is not null||globalRealtimeNameOverride||sharedDerivedPageScope||scopedGlobalExactPage
            };
            if(independentCache&&!CanSharePhysicalNames(result))
                return Gap("多个物理名称副本要求同一固定目标范围。");
            if(result.DerivedName)DerivedCheckboxNamesCompilation.Require(model,table,result);
            return new(result,null);
        }
        catch(Exception error)when(error is ArgumentException or InvalidOperationException or JsonException or NotSupportedException){return Gap(error.Message);}
    }
    /// <summary>只为排序解析实际字典页和中间联接，类型须与已开放的目标字段一致。</summary>
    private static IReadOnlyList<NonRedundantSingleTargetScope> RequireOrderTargets(GenerationModel model,
        TableModel target, PageModel page, IReadOnlyList<(ColumnModel Column, bool Descending)> order)
    {
        var joined = order.Select(item => item.Column).Where(column =>
            !NonRedundantReadCompilation.Physical(model, target, column)).DistinctBy(column => column.Source.Id).ToArray();
        var outputs = new[] { "id" }.Concat(joined.Select(column => column.Source.ColumnName)).ToArray();
        var dependencies = order.Select(item => item.Column.Source.ColumnName).ToArray();
        var targets = NonRedundantReadCompilation.ProjectPathTargets(model, target, page, outputs, multiValuePath: true);
        var plan = NonRedundantReads.CompileProjectRelation(model.Catalog, target.Source.Id, outputs, dependencies, targets);
        if (plan.OutputTypes["id"] != "int64" || joined.Any(column => plan.OutputTypes[column.Source.ColumnName] != column.Source.LogicalType))
            throw new InvalidOperationException("复选排序必须落到同类型的固定单值列。");
        return targets;
    }

    /// <summary>多份物理名称必须共用同一个保存及维护范围；全局范围直接由实际发布页提供。</summary>
    private static bool CanSharePhysicalNames(DatabaseCheckboxModel definition)
    {
        // 精确权限由每次请求绑定。副本范围必须能用实际来源行重算。
        if(definition.Name.Type!="string?"||definition.ExactTarget is not null&&
            !definition.HasStoredGlobalParentScope&&!definition.HasStoredExactScope||
            definition.Conditions is not null&&!definition.HasStoredIdentityScope&&!definition.HasProjectStoredAccountScope&&!definition.ProjectFormScope)
            return false;
        if(definition.HasStoredGlobalParentScope||definition.HasExactPhysicalParentScope)
            return !definition.LogicalSourceParent&&definition.SourceParent is not null&&definition.SourceParents.Count==1;
        if(definition.SharedLogicalParentScope)
            return !definition.LogicalSourceParent&&definition.SourceParent is not null&&
                (definition.SourceScopeColumns.Count==1||definition.FormParentField is not null);
        if(definition.DerivedName&&!definition.SharedDerivedPageScope&&!definition.HasStoredExactScope)return false;
        // 账号字典的全称和简称可共享控件显式声明的项目等值；各页的完整条件已与表级核对。
        return definition.Target.IsGlobal
            ? definition.NameTargetPage is {} globalPage&&
                (globalPage.Scopes.Count==0||definition.HasStoredIdentityScope&&globalPage.Scopes.All(scope=>scope.Source=="project"))&&
                (definition.Scopes.Count==0||definition.HasStoredIdentityScope&&definition.Scopes.All(scope=>scope.Source=="project"))&&
                (globalPage.Condition is null||globalPage.Condition.Syntax.Variables.Count==0||definition.SharedAccountScope)
            : (definition.NameTargetPage?.Condition is null||definition.PhysicalTargetPage?.Condition is {Syntax.Variables.Count:0}||
                definition.SharedAccountScope)&&
                (definition.Scopes.All(scope=>scope.Source=="project")&&definition.NameTargetPage?.Scopes.Any(scope=>scope.Source=="parent")!=true||
                    FixedCacheParent(definition) is not null);
    }

    /// <summary>所有名称缓存绑定同一个来源行键，已发布的全部实际目标父字段必须一致。</summary>
    internal static string? FixedCacheParent(DatabaseCheckboxModel definition)
    {
        // 项目内冗余字典的全局父已经沿各字段的实际发布页核验，副本仍须保存同一个来源物理父键。
        var projectGlobalParent=definition.HasProjectStoredAccountScope&&definition.GlobalParent&&!definition.Target.IsGlobal;
        if(definition.PhysicalTargetPage is null||definition.LogicalSourceParent||definition.GlobalParent&&!projectGlobalParent||
            definition.SourceScopeColumns.Count==0&&definition.FormParentField is null||
            definition.SourceScopeColumns.Count>1&&!definition.CompoundPhysicalSource||
            definition.SourceParents.Count!=1||definition.SourceParents[0].IsGlobal&&!projectGlobalParent)
            return null;
        var target=definition.PhysicalTargetPage.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column.Source.Id).ToArray();
        var effective=definition.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column.Source.Id).ToArray();
        return target.Length>0&&target.ToHashSet(StringComparer.Ordinal).SetEquals(effective)?definition.SourceParent?.Source.Id:null;
    }

    /// <summary>全局固定目录及含物理条件的普通发布页随名称缓存下发。</summary>
    private static string? FixedCachePage(DatabaseCheckboxModel definition) => definition.Target.IsGlobal||
        definition.SharedLogicalParentScope||definition.SharedDerivedPageScope
        ? definition.NameTargetPage?.Source.Id
        : definition.PhysicalTargetPage is {} page&&(page.Condition is not null||definition.ProjectFormScope||FixedCacheParent(definition) is not null)?page.Source.Id:null;

    /// <summary>所有写入口以同一事务写齐各页和表级的名称副本。</summary>
    internal static IReadOnlyList<MultiNameWriteBinding> Caches(GenerationModel model,TableModel table)
    {
        if(table.Source.IsRongyu!=1||table.Source.IsFunctionTable==1)return [];
        var definitions=table.Pages.SelectMany(page=>ForService(model,table,page).Select(definition=>(Page:page,Definition:definition))).ToArray();
        var bindings=new List<MultiNameWriteBinding>();
        foreach(var relation in definitions.GroupBy(item=>item.Definition.Source.Source.Id))
        {
            var actual=relation.Select(item=>item.Definition).ToList();
            var alternate=relation.FirstOrDefault(item=>item.Definition.UsesNameOverride);
            if(alternate.Definition is null)alternate=relation.FirstOrDefault(item=>item.Definition.ProjectFormScope);
            string? fixedPage=null;
            string? fixedParent=null;
            if(alternate.Definition is {} changed)
            {
                var root=Resolve(model,table,alternate.Page,changed.Source,forService:true,rootDefinition:true).Definition;
                if(root is null||actual.Append(root).Any(definition=>!CanSharePhysicalNames(definition)||
                    definition.Target.Source.Id!=root.Target.Source.Id||FixedCachePage(definition)!=FixedCachePage(root)||
                    FixedCacheParent(definition)!=FixedCacheParent(root)))
                    throw new InvalidOperationException("同一数据库复选的多页名称副本必须采用同一固定候选范围。");
                fixedPage=FixedCachePage(root);
                fixedParent=FixedCacheParent(root);
                actual.Add(root);
            }
            bindings.AddRange(actual.Where(definition=>definition.Names is not null).Select(definition=>new MultiNameWriteBinding(definition.Source,definition.Target,definition.Name,definition.Names!)
                {CheckboxTargetPageId=fixedPage,CheckboxSourceParentId=fixedParent}));
        }
        foreach(var binding in bindings.GroupBy(item=>item.Cache.Source.Id))
            if(binding.Select(item=>(item.Source.Source.Id,item.Target.Source.Id,item.Value.Source.Id)).Distinct().Count()!=1)
                throw new InvalidOperationException("复选的同一物理名称列绑定了不同来源或目标名称。");
        return bindings.DistinctBy(binding=>binding.Cache.Source.Id).OrderBy(binding=>binding.Cache.Source.ColumnName,StringComparer.Ordinal).ToArray();
    }

    /// <summary>名称数组是服务端管理的只读字段；保持原布局，不把普通名称列继续作为可写DTO输入。</summary>
    internal static GenerationModel Prepare(GenerationModel model)=>model with{Tables=model.Tables.Select(table=>
    {
        var definitions=table.Pages.SelectMany(page=>ForService(model,table,page)).ToArray();
        var idDisplaySources=definitions.Where(def=>def.Name.IsId)
            .Select(def=>def.Source.Source.ColumnName[..^4]).ToHashSet(StringComparer.Ordinal);
        foreach(var source in table.Source is { IsFunctionTable:0 } ? model.Catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Source.Id&&column.ControlKind is "MultiSelectPage" or "MultiCheckBoxDatabase") : [])
        {
            try
            {
                var id=source.ControlKind=="MultiCheckBoxDatabase"
                    ?DatabaseCheckboxDefinition.Parse(source.ControlDetail).NameField=="id"
                    :JsonDocument.Parse(string.IsNullOrWhiteSpace(source.ControlDetail)?"{}":source.ControlDetail).RootElement.TryGetProperty("nameField",out var field)&&field.GetString()=="id";
                if(id)idDisplaySources.Add(source.ColumnName[..^4]);
            }
            catch(JsonException) { }
        }
        // 切换为主键显示后，旧名称副本不再属于当前页面读取合同，避免按过时联接路径编译。
        bool staleIdName(ColumnModel column)=>column.Source.Extra.GetValueOrDefault("columnFullName").ToString() is {Length:>0} path&&
            idDisplaySources.Any(stem=>path.StartsWith(stem+".",StringComparison.Ordinal));
        var managed=(table.Source.IsRongyu==1&&table.Source.IsFunctionTable==0
            ?Caches(model,table).Select(binding=>binding.Cache.Source.Id)
            :definitions.Where(def=>def.Names is not null).Select(def=>def.Names!.Source.Id)).ToHashSet(StringComparer.Ordinal);ColumnModel Fix(ColumnModel column)=>managed.Contains(column.Source.Id)?column with{IsManagedMultiNames=true}:column;IReadOnlyList<ColumnModel> All(IEnumerable<ColumnModel> columns)=>columns.Select(Fix).ToArray();
        IReadOnlyList<ColumnModel> Visible(IEnumerable<ColumnModel> columns)=>columns.Where(column=>!staleIdName(column)).ToArray();
        // 原业务记录读写必须保留项目归属；字典范围只用于复选候选。
        var scopedGlobal=table.HasScopedGlobalCache||table.IsGlobal&&table.Source.IsRongyu==1&&definitions.Length>0;
        return table with{HasScopedGlobalCache=scopedGlobal,Columns=All(table.Columns),Pages=table.Pages.Select(page=>page with{Readable=All(Visible(page.Readable)),Writable=All(Visible(page.Writable.Where(column=>!managed.Contains(column.Source.Id)))),Searchable=All(Visible(page.Searchable)),Sortable=All(Visible(page.Sortable)),ListColumns=All(Visible(page.ListColumns)),DetailColumns=All(Visible(page.DetailColumns))}).ToArray()};
    }).ToArray()};
}
