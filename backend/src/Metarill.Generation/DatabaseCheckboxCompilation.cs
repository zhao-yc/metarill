using System.Text.Json;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

internal sealed record DatabaseCheckboxModel(ColumnModel Source,TableModel Target,ColumnModel Name,ColumnModel Names,IReadOnlyList<(ColumnModel Column,string Source)> Scopes,RelationConditionModel? Conditions,IReadOnlyList<(ColumnModel Column,bool Descending)> Order,IReadOnlyList<TableModel> SourceParents,ColumnModel? SourceParent,bool DerivedName=false,PageModel? GlobalPage=null)
{
    internal PageModel? LogicalTargetPage { get; init; }
    internal bool CompoundTargetParents { get; init; }
    internal bool RealtimeLogicalParentScope { get; init; }
    // 没有逻辑候选条件的显式目标页仍决定名称路径内每个关系角色。
    internal PageModel? NameTargetPage { get; init; }
    // 普通目标页的物理条件参与原候选、保存和 Excel。
    internal PageModel? PhysicalTargetPage { get; init; }
    internal bool UsesPageRead { get; init; }
    internal bool UsesControlRead { get; init; }
    internal bool UsesNameOverride { get; init; }
    internal ColumnModel? FunctionParentField { get; init; }
    internal ColumnModel? FormParentField { get; init; }
    internal bool SharedAccountScope { get; init; }
    internal bool SharedLogicalParentScope { get; init; }
    internal ExactDataManageModel? ExactTarget { get; init; }
    internal IReadOnlyList<(CrossTableReadModel Read,int Index)> LogicalSourcePaths { get; init; } = [];
    internal bool LogicalSourceParent => LogicalSourcePaths.Count>0;
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
            if(table.Source.IsRongyu is not(0 or 1)||table.IsGlobal||source.Type!="string?"||!source.Source.ColumnName.EndsWith("_ids",StringComparison.Ordinal)||
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
            if(target.IsGlobal&&!functionForm)
            {
                var proof=table.Source.IsRongyu==1?GlobalMultipleRelationTarget.RequireCached(model.Catalog,table.Source,source.Source,target.Source):GlobalMultipleRelationTarget.Require(model.Catalog,table.Source,source.Source,target.Source);
                if(definition.PageId.Length>0&&definition.PageId!=proof.Page.Id)return Gap("全局复选页级目标覆盖与固定关系不一致。");
                // 复选的搜索、回读与 Excel 必须沿已发布缓存目标页的同一固定范围。
                publishedGlobalPage=target.Pages.Single(page=>page.Source.Id==proof.Page.Id);
                if(NonRedundantReadCompilation.HasGlobalRowProject(target)||FixedPageConditionCompilation.HasMultiTargetScope(model,target,publishedGlobalPage)||
                    GlobalPhysicalRelationValue.IsPublishedTypedCache(model.Catalog,MetadataStoragePolicy.ForTable(model.Catalog,target.Source),target.Source,proof.Name)||
                    table.Source.IsRongyu==1&&publishedGlobalPage.Condition is not null)
                    globalPage=publishedGlobalPage;
            }
            var usesNameOverride=definition.NameField!=baseline.NameField;
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
            var exactTarget=namePage is null?null:ExactDataManageCompilation.Resolve(model,target,namePage);
            var fixedPage=baseline.PageId.Length>0?baseline.PageId:linkedPage;
            var sharedNamesPage=table.Source.IsRongyu==1&&!functionForm&&namePage is not null&&namePage.Source.Id==fixedPage&&
                model.Catalog.ConfigPageColumns.Any(setting=>setting.ConfigColumnId==source.Source.Id&&
                    table.Pages.Any(candidate=>candidate.Source.Id==setting.ConfigPageId)&&
                    baseline.WithPageOverride(setting.Extra.GetValueOrDefault("controlDetail").ToString()).NameField!=baseline.NameField);
            // 各名称都是真实列时，当前锁读一次取齐所有值。回填另按持久归属核对同一目标页。
            var sharedAccountScope=sharedNamesPage&&!derived&&target.Source.IsRongyu==0&&exactTarget is null&&
                !FixedPageConditionCompilation.HasLogicalFields(namePage!)&&FixedPageConditionCompilation.HasAccountVariables(namePage!)&&
                namePage!.Scopes.All(scope=>scope.Source=="project");
            if(sharedAccountScope)
                _=Metarill.Data.Queries.AccountScopedCrossCache.Compile(model.Catalog,table.Source,target.Source,namePage!.Source);
            // 多个目标父层位仍由同一发布页证明，所有名称共用同一个来源父键。
            var sharedLogicalParentScope=sharedNamesPage&&target.Source.IsRongyu==0&&exactTarget is null&&
                namePage!.Scopes.Any(scope=>scope.Source=="parent"&&NonRedundantCrossParentCompilation.Managed(model,target,namePage,scope.Column));
            if(usesNameOverride)
            {
                // 非冗余页仍通过原数组实时投影；冗余名称副本沿发布范围独立证明。
                var realtimeFixedName=table.Source.IsRongyu==0&&target.Source.IsRongyu==0&&!target.IsGlobal&&
                    exactTarget is null&&namePage is not null&&namePage.Source.Id==fixedPage;
                var storage=MetadataStoragePolicy.ForTable(model.Catalog,target.Source);
                bool TextLabel(ColumnModel label)=>MultiSelectCompilation.PhysicalTextLabel(model,target,label)||
                    (sharedLogicalParentScope||realtimeFixedName)&&MultiNameStorageColumns.IsDerivedCheckboxText(target.Source,storage,label.Source);
                var baseName=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==baseline.NameField);
                if(functionForm||target.IsGlobal&&table.Source.IsRongyu!=1||baseName is null||!TextLabel(baseName)||
                    !TextLabel(name)||namePage is not null&&!namePage.Readable.Any(column=>column.Source.Id==name.Source.Id))
                    return Gap("页级名称须沿用同一固定文本关系，显式目标页必须已开放该字段。");
            }
            // 功能表没有持久化来源值。纯物理条件直接使用已绑定的账号和当前表单父键。
            var compoundTargetParents=!functionForm&&target.Source.IsRongyu==0&&
                namePage?.Scopes.Count(scope=>scope.Source=="parent")>1;
            var logicalPage=!sharedAccountScope&&namePage is not null&&(compoundTargetParents||FixedPageConditionCompilation.HasMultiTargetScope(model,target,namePage))&&
                (!functionForm||FixedPageConditionCompilation.HasLogicalFields(namePage)||namePage.Scopes.Any(scope=>scope.Source=="parent"&&
                    (!NonRedundantReadCompilation.Physical(model,target,scope.Column)||
                     CrossTableStorageColumns.IsLogicalPathField(target.Source,model.Catalog.ConfigColumns,scope.Column.Source.ColumnName)||
                     target.IsGlobal&&model.Tables.Any(parent=>parent.IsGlobal&&parent.Source.Id==scope.Column.Source.LinkConfigTableId))))?namePage:null;
            // 同一候选即使由表级继承，也必须用当前普通非冗余来源页的父键重新证明。
            var pageRead=namePage is not null&&table.Source.IsRongyu==0&&target.Source.IsRongyu==0&&!target.IsGlobal&&
                (explicitPage!=fixedPage||logicalPage?.Scopes.Any(scope=>scope.Source=="parent")==true);
            if(explicitPage!=fixedPage&&!pageRead&&(logicalPage is not null||target.Pages.Any(candidate=>candidate.Source.Id==fixedPage&&FixedPageConditionCompilation.HasMultiTargetScope(model,target,candidate))))
                return Gap("复选不能用页覆盖替换表级固定范围。");
            var (scopes,conditions)=RelationConditionsCompilation.Compile(model,table,page,target,definition.WhereSql);
            var realtimeRead=table.Source.IsRongyu==0&&!functionForm;
            var controlRead=realtimeRead&&(conditions is not null||
                scopes.Any(scope=>scope.Source=="parent"||target.IsGlobal&&scope.Source=="project"));
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
            if((realtimeRead||functionForm||globalPage is not null||exactTarget is not null||sharedFixedPage)&&!scopeAlreadyBound&&namePage is not null&&
                (functionForm||namePage.Condition is not null||namePage.Scopes.Any(scope=>scope.Source=="parent")))
            {
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
            var sourceScopeColumns=page.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column).ToArray();
            if(sourceScopeColumns.Any(column=>!NonRedundantReadCompilation.Physical(model,table,column)||column.IsJsonArray||
                column.Type is not("long?" or "string?")))return Gap("来源父必须为固定标量ID。");
            var sourceBindings=sourceScopeColumns.Select(column=>(Column:column,Path:NonRedundantCrossParentCompilation.Find(model,table,page,column))).ToArray();
            var parents=sourceBindings.Select(binding=>binding.Path is {} path?path.Read.Levels[path.Index].Target:Parent(binding.Column))
                .DistinctBy(parent=>parent.Source.Id).ToArray();
            if(parents.Length>1)return Gap("多个来源范围必须绑定同一真实父表。");
            // 目标只有一个 parentId；用可还原的逻辑键作为来源代表，其他同父范围仍逐条验证。
            var logicalBindings=sourceBindings.Where(binding=>binding.Path is not null).ToArray();
            var logicalSource=logicalBindings.Length>0;
            var sourceParent=logicalBindings.FirstOrDefault().Column??sourceScopeColumns.FirstOrDefault();
            var physicalParents=scopes.Where(scope=>scope.Source=="parent").ToArray();
            var pageHasParent=logicalPage?.Scopes.Any(scope=>scope.Source=="parent")==true;
            if(logicalSource&&physicalParents.Length==0&&!pageHasParent)
                return Gap("来源逻辑父键须对应明确的控件或目标页父范围。");
            PageModel? globalParentPage=null;
            var compoundGlobalSource=false;
            if(functionForm&&physicalParents.Length>0)
            {
                if(physicalParents.Length!=1)return Gap("功能表复选必须绑定唯一目标父字段。");
                var targetParent=physicalParents[0].Column;
                var publishedParent=namePage?.Scopes.SingleOrDefault(scope=>scope.Source=="parent").Column;
                var targetBinding=publishedParent?.Source.Id==targetParent.Source.Id&&namePage is not null
                    ?MultiTargetParentScope.Compile(model.Catalog,target.Source,
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

            // 无页面父路由时，固定目标父可绑定来源详情中唯一的真实表单外键。
            if(sharedFixedPage&&sourceScopeColumns.Length==0&&physicalParents.Length==1)
            {
                var binding=MultiTargetParentScope.CompileCheckboxParent(model.Catalog,target.Source,
                    FixedConditionPlan.Compile(namePage!.Source.WhereSql,target.Columns.Select(column=>column.Source.ColumnName)));
                if(binding is null||binding.Global||binding.Logical||binding.Target.Id!=physicalParents[0].Column.Source.Id||
                    Protected(model.Tables.Single(candidate=>candidate.Source.Id==binding.Parent.Id)))
                    return Gap("表单父范围须绑定实际普通目标父键。");
                BindFormParent(binding,binding.CheckboxSource(model.Catalog,table.Source,source.Source,page.Source,allowFormParent:true));
            }
            // 控件和普通目标页的物理父键都须匹配来源已证明的同一张父表。
            if(!functionForm&&physicalParents.Any(scope=>sourceParent is null||Parent(scope.Column).Source.Id!=parents[0].Source.Id))
                return Gap("原parentId必须由来源页同一真实父表证明，不能猜测其他父范围。");
            if(!functionForm&&pageHasParent&&logicalPage is {} scopedPage)
            {
                var syntax=FixedConditionPlan.Compile(scopedPage.Source.WhereSql,target.Columns.Select(column=>column.Source.ColumnName));
                var binding=(target.IsGlobal
                    ?MultiTargetParentScope.CompileGlobalPage(model.Catalog,target.Source,syntax,checkbox:true)
                    :MultiTargetParentScope.CompileCheckboxPage(model.Catalog,target.Source,syntax,allowGlobalParent:true))!;
                var owner=sharedLogicalParentScope?binding.CheckboxSource(model.Catalog,table.Source,source.Source):
                    pageRead?binding.Source(model.Catalog,table.Source,source.Source,page.Source):
                    binding.Source(model.Catalog,table.Source,source.Source);
                if(sharedLogicalParentScope&&sourceScopeColumns.Length==0)
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
            // 只有显式关联了实际级联祖先时，原复选可直接沿来源的全局层位校验。
            var explicitGlobalPath=logicalSource&&physicalParents.Length>0&&parents[0].IsGlobal&&
                physicalParents.All(scope=>scope.Column.Source.LinkConfigTableId==parents[0].Source.Id);
            if(parents.Any(parent=>Protected(parent)||!(parent.IsGlobal&&(globalParentPage is not null||explicitGlobalPath))&&!HasIdentity(model,parent)))
                return Gap("来源父须具有普通业务项目归属或实际全局层位证明。");
            // 原非冗余候选优先采用控件排序，省略时继承实际目标页排序。
            var orderBy=definition.OrderBy;
            if(string.IsNullOrWhiteSpace(orderBy)&&table.Source.IsRongyu==0)
                orderBy=namePage?.Source.OrderBy??"";
            var order=GenerationModel.ValidateOrderBy(string.IsNullOrWhiteSpace(orderBy)?definition.NameField+" asc":orderBy,target.Columns).ToList();
            if(order.Any(item=>(!NonRedundantReadCompilation.Physical(model,target,item.Column)&&!(derived&&item.Column.Source.Id==name.Source.Id))||item.Column.IsJsonArray||ManagerCredentialCompilation.IsCredential(target,item.Column)))return Gap("候选排序必须为非凭据物理字段。");if(!order.Any(item=>item.Column.IsId))order.Add((target.Columns.Single(column=>column.IsId),false));
            var independentCache=table.Source.IsRongyu==1&&usesNameOverride;
            var names=functionForm?SelectionDisplayFieldCompilation.FunctionFormNames(table,source,name):
                independentCache?MultiNameStorageCompilation.FindPhysicalName(table,source,name):SelectionDisplayFieldCompilation.Resolve(table,source);
            if(names is null||!functionForm&&(names.Source.Id==source.Source.Id||names.Type!="string?"||table.Source.IsRongyu==1&&!MultiNameStorageCompilation.Supports(model,table,source,name,names)))return Gap("冗余模式需要已证明的同一关系名称数组缓存，非冗余使用只读联接或自动名称。");
            if(independentCache&&!forService&&!page.Readable.Any(column=>column.Source.Id==names.Source.Id))
                return Gap("本页必须开放简称对应的物理名称数组。");
            if(!functionForm&&table.Source.IsRongyu==0&&(!GenerationModel.Enabled(names.Source.Extra,"isJoinColumn")||names.Source.IsVirtualColumn!=0))return Gap("非冗余名称必须是明确只读联接或自动投影。");
            var result=new DatabaseCheckboxModel(source,target,name,names,scopes,conditions,order,parents,sourceParent,derived,globalPage)
            {
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
                FormParentField=formParent,
                SharedAccountScope=sharedAccountScope,
                SharedLogicalParentScope=sharedLogicalParentScope,
                RealtimeLogicalParentScope=realtimeRead&&usesNameOverride&&logicalPage is not null&&
                    logicalPage.Scopes.Any(scope=>scope.Source=="parent"&&NonRedundantCrossParentCompilation.Managed(model,target,logicalPage,scope.Column)),
                ExactTarget=exactTarget,
                LogicalSourcePaths=logicalBindings.Select(binding=>binding.Path!.Value)
                    .DistinctBy(path=>(path.Read.Source.Source.Id,path.Index)).ToArray(),
                SourceScopeColumns=sourceScopeColumns,
                CompoundSourceDependencies=compoundGlobalSource
                    ?NonRedundantCrossParentCompilation.Dependencies(model,table,page,sourceScopeColumns):[],
                GlobalParentPage=globalParentPage,
                DerivedName=derived||logicalPage is not null
            };
            if(independentCache&&!CanSharePhysicalNames(result))
                return Gap("多个物理名称副本要求同一固定目标范围。");
            if(result.DerivedName)DerivedCheckboxNamesCompilation.Require(model,table,result);
            return new(result,null);
        }
        catch(Exception error)when(error is ArgumentException or InvalidOperationException or JsonException or NotSupportedException){return Gap(error.Message);}
    }
    /// <summary>多份物理名称必须共用同一个保存及维护范围；全局范围直接由实际发布页提供。</summary>
    private static bool CanSharePhysicalNames(DatabaseCheckboxModel definition)
    {
        if(definition.Name.Type!="string?"||definition.ExactTarget is not null||definition.Conditions is not null)
            return false;
        if(definition.SharedLogicalParentScope)
            return !definition.LogicalSourceParent&&definition.SourceParent is not null&&
                (definition.SourceScopeColumns.Count==1||definition.FormParentField is not null);
        if(definition.DerivedName)return false;
        return definition.Target.IsGlobal
            ? definition.NameTargetPage is {Scopes.Count:0} globalPage&&definition.Scopes.Count==0&&
                (globalPage.Condition is null||globalPage.Condition.Syntax.Variables.Count==0||definition.SharedAccountScope)
            : (definition.NameTargetPage?.Condition is null||definition.PhysicalTargetPage?.Condition is {Syntax.Variables.Count:0}||
                definition.SharedAccountScope)&&
                (definition.Scopes.All(scope=>scope.Source=="project")&&definition.NameTargetPage?.Scopes.Any(scope=>scope.Source=="parent")!=true||
                    FixedCacheParent(definition) is not null);
    }

    /// <summary>来源和目标须各有一个实际物理父字段，所有名称缓存绑定同一个来源行键。</summary>
    private static string? FixedCacheParent(DatabaseCheckboxModel definition)
    {
        if(definition.PhysicalTargetPage is null||definition.LogicalSourceParent||definition.GlobalParent||
            definition.SourceScopeColumns.Count!=1&&definition.FormParentField is null||
            definition.SourceParents.Count!=1||definition.SourceParents[0].IsGlobal)
            return null;
        var target=definition.PhysicalTargetPage.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column.Source.Id).ToArray();
        var effective=definition.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column.Source.Id).ToArray();
        return target.Length==1&&target.SequenceEqual(effective,StringComparer.Ordinal)?definition.SourceParent?.Source.Id:null;
    }

    /// <summary>全局固定目录及含物理条件的普通发布页随名称缓存下发。</summary>
    private static string? FixedCachePage(DatabaseCheckboxModel definition) => definition.Target.IsGlobal||definition.SharedLogicalParentScope
        ? definition.NameTargetPage?.Source.Id
        : definition.PhysicalTargetPage is {} page&&(page.Condition is not null||FixedCacheParent(definition) is not null)?page.Source.Id:null;

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
            bindings.AddRange(actual.Select(definition=>new MultiNameWriteBinding(definition.Source,definition.Target,definition.Name,definition.Names)
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
        var managed=(table.Source.IsRongyu==1&&table.Source.IsFunctionTable==0
            ?Caches(model,table).Select(binding=>binding.Cache.Source.Id)
            :table.Pages.SelectMany(page=>ForService(model,table,page)).Select(def=>def.Names.Source.Id)).ToHashSet(StringComparer.Ordinal);ColumnModel Fix(ColumnModel column)=>managed.Contains(column.Source.Id)?column with{IsManagedMultiNames=true}:column;IReadOnlyList<ColumnModel> All(IEnumerable<ColumnModel> columns)=>columns.Select(Fix).ToArray();
        return table with{Columns=All(table.Columns),Pages=table.Pages.Select(page=>page with{Readable=All(page.Readable),Writable=All(page.Writable.Where(column=>!managed.Contains(column.Source.Id))),Searchable=All(page.Searchable),Sortable=All(page.Sortable),ListColumns=All(page.ListColumns),DetailColumns=All(page.DetailColumns)}).ToArray()};
    }).ToArray()};
}
