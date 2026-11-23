using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Frozen;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Data.Relations;

namespace Metarill.Data.Queries;

/// <summary>多值投影只持有编译后的标识符与固定范围，执行阶段不再读取元数据。</summary>
internal sealed record MySqlPhysicalReference(string Table,Metarill.Data.Schema.ColumnSchema Column);
internal sealed record MySqlValueOrigin(string Table,string Column);
internal sealed record MySqlScalarCacheTarget(string Table,string Alias,string Predicate,string ForeignColumn);
internal sealed record MySqlReverseProjection(string TargetTable,string From,string ForeignExpression,string ForeignType,IReadOnlyDictionary<string,IReadOnlySet<string>> Outputs)
{
    /// <summary>普通等值供字符串索引预筛选，二进制条件仍决定精确ID；数组谓词和整数键保持原义。</summary>
    internal string TargetPredicate => ForeignType switch
    {
        "predicate" => ForeignExpression,
        "string" => "("+ForeignExpression+"=@target_text AND BINARY "+ForeignExpression+"=BINARY @target_text)",
        _ => ForeignExpression+"=@target_id"
    };

    /// <summary>原保存钩子可直接从目标物理行取值的单级缓存。</summary>
    internal IReadOnlySet<string> DirectSaveOutputs {get;init;}=FrozenSet<string>.Empty;

    /// <summary>只有原始更新事件可补刷新直接缓存，下游事件仍按实际变更字段传播。</summary>
    internal bool Affects(string output,IReadOnlySet<string> changed,bool allFields,bool targetSaved)
        =>allFields||Outputs[output].Overlaps(changed)||targetSaved&&DirectSaveOutputs.Contains(output);
}

internal sealed record MySqlMultiProjection(string SourceExpression,string TargetTable,string TargetName,string TargetScope,string Joins="",string? NameExpression=null,MySqlMultiOwner? Owner=null);
internal sealed record MySqlMultiOwner(string Alias,IReadOnlyList<string> Fields,IReadOnlyDictionary<string,string>? SourceExpressions=null,bool PreserveScalarTypes=false);

/// <summary>只由已发布元数据编译的单值读取计划，不提供SQL反序列化或任意表达式入口。</summary>
public sealed class MySqlNonRedundantReadPlan
{
    internal MySqlNonRedundantReadPlan(string from, string projectId, string scope, Dictionary<string,string> expressions,
        Dictionary<string,string> types, string[] outputs, Dictionary<string,string> arrayNamePredicates, Dictionary<string,MySqlMultiProjection> multiProjections,Dictionary<string,string> arrayIdSources,IReadOnlyList<MySqlPhysicalReference> physicalReferences,IReadOnlyDictionary<string,MySqlValueOrigin> origins,IReadOnlyList<MySqlReverseProjection>? reverse=null,IReadOnlyList<MySqlCrossTableProjection>? crosses=null,IReadOnlyDictionary<string,object?>? constants=null,IReadOnlyList<string>? sourceDependencies=null,MySqlMaintenanceReadFrom? maintenanceFrom=null)
    { MaintenanceFrom=maintenanceFrom; From=from; ProjectId=projectId; Scope=scope; Expressions=new ReadOnlyDictionary<string,string>(expressions); Types=new ReadOnlyDictionary<string,string>(types); Outputs=Array.AsReadOnly(outputs); ArrayNamePredicates=new ReadOnlyDictionary<string,string>(arrayNamePredicates); MultiProjections=new ReadOnlyDictionary<string,MySqlMultiProjection>(multiProjections); ArrayIdSources=new ReadOnlyDictionary<string,string>(arrayIdSources); PhysicalReferences=physicalReferences; SourceDependencies=sourceDependencies??[]; Origins=origins; Reverse=reverse??[]; Crosses=crosses??[];this.constants=constants??new ReadOnlyDictionary<string,object?>(new Dictionary<string,object?>()); hasAccountParameters=this.constants.Values.Any(value=>value is FixedConditionSqlPlan.AccountParameter); }
    /// <summary>投影视图复用不可变范围及依赖；执行时只选择预编译联接片段，不重新解析元数据。</summary>
    private MySqlNonRedundantReadPlan(MySqlNonRedundantReadPlan plan,string[] outputs,IReadOnlyList<MySqlPhysicalReference>? physicalReferences=null,IReadOnlyDictionary<string,object?>? boundConstants=null)
    {
        HasPublishedCandidateCondition=plan.HasPublishedCandidateCondition;
        RequiredMultiParent=plan.RequiredMultiParent;CandidateParents=plan.CandidateParents;CandidateActor=plan.CandidateActor;AggregateFrom=plan.AggregateFrom;CascadeLabelOutputs=plan.CascadeLabelOutputs;CascadeLabelPages=plan.CascadeLabelPages;CascadeCacheOwnerFields=plan.CascadeCacheOwnerFields;ImportMatchLock=plan.ImportMatchLock;RuleOutputAliases=plan.RuleOutputAliases;StorageOutputs=plan.StorageOutputs;RequiresPublishedProjectIdentity=plan.RequiresPublishedProjectIdentity;MaintenanceFrom=plan.MaintenanceFrom;From=plan.From;ProjectId=plan.ProjectId;Scope=plan.Scope;Expressions=plan.Expressions;Types=plan.Types;Outputs=Array.AsReadOnly(outputs);ArrayNamePredicates=plan.ArrayNamePredicates;MultiProjections=plan.MultiProjections;ArrayIdSources=plan.ArrayIdSources;PhysicalReferences=physicalReferences??plan.PhysicalReferences;SourceDependencies=plan.SourceDependencies;Origins=plan.Origins;Reverse=plan.Reverse;Crosses=plan.Crosses; constants=boundConstants??plan.constants; hasAccountParameters=boundConstants is null&&plan.hasAccountParameters; ScalarCacheTarget=plan.ScalarCacheTarget;
    }
    /// <summary>缓存传播只读取稳定主键和本次允许的输出，未知字段不能变成新的读取权限。</summary>
    internal MySqlNonRedundantReadPlan ForCacheFields(IReadOnlySet<string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if(!Outputs.Contains("id",StringComparer.Ordinal)||fields.Any(field=>!Outputs.Contains(field,StringComparer.Ordinal)))throw new InvalidOperationException("缓存投影只能选择已发布输出并保留真实主键。");
        return new(this,Outputs.Where(field=>field=="id"||fields.Contains(field)).ToArray());
    }
    /// <summary>维护预检补充完整物理依赖时保留全部冻结读取状态及字典比较器，禁止逐参数重建遗漏计划。</summary>
    internal MySqlNonRedundantReadPlan WithPhysicalReferences(IEnumerable<MySqlPhysicalReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);return new(this,Outputs.ToArray(),Array.AsReadOnly(references.ToArray()));
    }
    private MySqlMaintenanceReadFrom? MaintenanceFrom{get;}
    internal MySqlMaintenanceReadFrom? AggregateFrom { get; init; }
    /// <summary>普通读取跳过维护联接集合计算，保持原路径的计划选择成本。</summary>
    internal bool HasMaintenanceJoinProjection=>MaintenanceFrom is not null;
    /// <summary>连接核验与锁读合并时只能定位已发布的同一物理库。</summary>
    internal string? QualifiedMaintenanceFrom(string quotedDatabase)=>MaintenanceFrom?.RenderQualified(quotedDatabase);
    /// <summary>单一全局直达联接的原谓词，供精确缓存条件写回复用。</summary>
    internal MySqlScalarCacheTarget? ScalarCacheTarget {get;init;}
    /// <summary>维护当前读保留输出及查询依赖的联接；普通读取没有此裁剪合同，继续原完整FROM。</summary>
    internal string FromForFields(IEnumerable<string> fields)
    {
        if(MaintenanceFrom is null)return From;
        var selected=fields.ToHashSet(StringComparer.OrdinalIgnoreCase);foreach(var field in selected)_=Expression(field);
        return MaintenanceFrom.Render(selected);
    }
    internal IReadOnlyList<MySqlReverseProjection> Reverse {get;}
    internal IReadOnlyList<MySqlCrossTableProjection> Crosses{get;}
    private readonly IReadOnlyDictionary<string,object?> constants;
    private readonly bool hasAccountParameters;
    internal bool RequiresAccount=>hasAccountParameters;

    /// <summary>嵌套编译合并冻结的参数标记；实际 SQL 执行仍只能读取已绑定参数。</summary>
    internal void CopyParametersTo(Dictionary<string,object?> destination)
    {
        foreach(var pair in constants)destination.Add(pair.Key,pair.Value);
    }

    /// <summary>任何执行入口都须先绑定账号参数，静态标记不能送入数据库。</summary>
    internal IReadOnlyDictionary<string,object?> Constants => hasAccountParameters
        ? throw new InvalidOperationException("跨表投影缺少已认证的账号参数。")
        : constants;

    /// <summary>沿用冻结的 SQL 与联接结构，为本次请求生成独立参数，不覆盖其它账号的计划。</summary>
    internal MySqlNonRedundantReadPlan BindAccount(Metarill.Core.Security.RuntimeActor actor)
    {
        if(!hasAccountParameters&&CandidateParents.Count==0)return this;
        var bound=hasAccountParameters?constants.ToFrozenDictionary(pair=>pair.Key,
            pair=>pair.Value is FixedConditionSqlPlan.AccountParameter parameter?parameter.Bind(actor):pair.Value,StringComparer.Ordinal):null;
        return new(this,Outputs.ToArray(),boundConstants:bound){CandidateActor=CandidateParents.Count>0?actor:null};
    }
    internal IReadOnlyList<MySqlPhysicalReference> PhysicalReferences {get;}
    internal IReadOnlyList<string> SourceDependencies {get;}
    internal IReadOnlyDictionary<string,MySqlValueOrigin> Origins {get;}
    internal string From { get; }
    /// <summary>覆盖匹配和整表标量维护锁定待写来源行，其余已编译联接保持共享锁。</summary>
    internal string? ImportMatchLock { get; init; }
    internal IReadOnlySet<string> CascadeLabelOutputs { get; init; }=FrozenSet<string>.Empty;
    internal IReadOnlySet<string> CascadeLabelPages { get; init; }=FrozenSet<string>.Empty;
    internal IReadOnlySet<string> CascadeCacheOwnerFields { get; init; }=FrozenSet<string>.Empty;
    internal string Scope { get; }
    /// <summary>候选页的真实父字段允许参与查询时，原父链必须随本次查询完整绑定。</summary>
    internal IReadOnlyList<RedundantSharedParentPlan> CandidateParents { get; init; } = [];
    internal string? RequiredMultiParent{get;init;}
    internal bool HasPublishedCandidateCondition{get;init;}
    internal Metarill.Core.Security.RuntimeActor? CandidateActor { get; private init; }
    internal string ProjectId { get; }
    /// <summary>全局来源只属于编译目录；覆盖项目参数不能切换其身份权威。</summary>
    internal bool RequiresPublishedProjectIdentity {get;init;}
    internal IReadOnlyDictionary<string,string> ArrayNamePredicates {get;}
    internal IReadOnlyDictionary<string,string> ArrayIdSources {get;}
    internal IReadOnlyDictionary<string,MySqlMultiProjection> MultiProjections {get;}
    /// <summary>仅规则实体用途覆盖输出；逻辑筛选表达式保持原计划，不把原始存储值当逻辑祖先。</summary>
    internal IReadOnlyDictionary<string,string> StorageOutputs { get; private init; } = FrozenDictionary<string,string>.Empty;
    /// <summary>复制只读的已证明根列，不接受运行时SQL或请求字段。</summary>
    internal MySqlNonRedundantReadPlan WithStorageOutputs(IReadOnlyDictionary<string,string> expressions,IReadOnlyDictionary<string,string> aliases)=>new(this,Outputs.ToArray()){StorageOutputs=expressions.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),RuleOutputAliases=aliases.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)};
    /// <summary>物理字段同时参与逻辑规则时使用独立固定别名，不能覆盖实体的稀疏存储值。</summary>
    internal IReadOnlyDictionary<string,string> RuleOutputAliases { get; private init; } = FrozenDictionary<string,string>.Empty;
    /// <summary>同一SQL分别保留物理实体输出和逻辑条件；普通计划没有覆盖，行为不变。</summary>
    internal string OutputExpression(string name)=>StorageOutputs.TryGetValue(name,out var expression)?expression:Expression(name);
    internal IReadOnlyDictionary<string,string> Expressions { get; }
    internal IReadOnlyDictionary<string,string> Types { get; }
    public IReadOnlyList<string> Outputs { get; }
    /// <summary>筛选和排序仅能引用本计划明确编译的字段，标识符不是客户端SQL。</summary>
    internal string Expression(string name)=>Expressions.TryGetValue(name,out var expression)?expression:throw new InvalidOperationException("读取字段未编译："+name);

    private ConcurrentDictionary<string,string>? sharedExactExpressions;
    /// <summary>只编译一次权限字段的SQL，供同一不可变计划的后续请求复用。</summary>
    internal string SharedExactExpression(string field)=>
        LazyInitializer.EnsureInitialized(ref sharedExactExpressions,static ()=>new(StringComparer.Ordinal))
            .GetOrAdd(field,static (name,plan)=>plan.BuildSharedExactExpression(name),this);

    /// <summary>授权只接受所有实际末端均在发布范围内且共同祖先一致的逻辑值。</summary>
    private string BuildSharedExactExpression(string field)
    {
        var key=Crosses.Where(cross=>cross.SharedGroupKey is not null&&cross.Selection.Slots.Any(
            slot=>slot.ColumnName==field||slot.ColumnName[..^3]+"_name"==field))
            .Select(cross=>cross.SharedGroupKey).Distinct(StringComparer.Ordinal).SingleOrDefault()
            ??throw new InvalidOperationException("精确范围字段缺少固定共享级联计划。");
        var valid=Crosses.Where(cross=>cross.SharedGroupKey==key).Select(cross=>
            "("+cross.Validity+") AND (("+string.Join(" AND ",cross.RawSlots.Values.Select(raw=>
                "NULLIF(BINARY CAST("+raw+" AS CHAR),BINARY '') IS NULL"))+") OR "+cross.Reachable+")");
        return "(CASE WHEN "+string.Join(" AND ",valid)+" THEN "+Expression(field)+" END)";
    }
}

/// <summary>编译单值联接与固定多选名称计划；未证明的级联和虚拟表达式继续明确拒绝。</summary>
public static partial class MySqlNonRedundantReadCompiler
{
    /// <summary>输出由生成用途指定；依赖独立编译，不擅自扩成整表读取或读取权限。</summary>
    public static MySqlNonRedundantReadPlan CompileGlobalCandidate(MetadataCatalog catalog,string tableId,string pageId,IReadOnlyList<string> outputs,IReadOnlyList<string>? dependencies=null)=>
        CompileGlobalCandidate(catalog,tableId,pageId,outputs,dependencies,deferCascadeNames:false);

    /// <summary>全局候选页面由生成的固定树图补齐显示路径，单值候选仍直接读取标量。</summary>
    internal static MySqlNonRedundantReadPlan CompileGlobalCandidate(MetadataCatalog catalog,string tableId,string pageId,
        IReadOnlyList<string> outputs,IReadOnlyList<string>? dependencies,bool deferCascadeNames,
        IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets=null,bool exactSingleCandidate=false)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(outputs);
        MetadataValidation.Validate(catalog);
        if(catalog.DatabaseProvider!="mysql")throw new NotSupportedException("当前固定读取执行仅完成MySQL适配。");
        if(outputs.Count==0||outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=outputs.Count)
            throw new InvalidOperationException("输出字段不能为空或重复。");
        var page=catalog.ConfigPages.SingleOrDefault(item=>item.Id==pageId&&item.ConfigTableId==tableId)
            ??throw new InvalidOperationException("全局候选页必须属于固定目标表。");
        var table=catalog.ConfigTables.Single(item=>item.Id==tableId);
        var pageType=page.Extra.GetValueOrDefault("type").ToString();
        if(!exactSingleCandidate&&pageType!="2"&&GlobalCrossTableTarget.SingleParent(catalog,table,page) is not null)
            return CompileCrossPageCandidate(catalog,tableId,pageId,outputs,dependencies??[],accountParentPageType:pageType,singleTargets:singleTargets);
        return new Builder(catalog,tableId,candidatePage:page,deferCascadeNames:deferCascadeNames,exactSingleCandidate:exactSingleCandidate,
            globalCandidate:table.IsRongyu==0&&GlobalCrossTableTarget.Declared(table))
        {
            ProjectSingleTargets=(singleTargets??[]).ToDictionary(binding=>binding.ColumnId,StringComparer.Ordinal)
        }.Compile(outputs,dependencies??[]);
    }

    /// <summary>普通读取及导出按对象本身的行项目归属。</summary>
    public static MySqlNonRedundantReadPlan Compile(MetadataCatalog catalog,string tableId,IReadOnlyList<string> outputNames,IReadOnlyList<string>? dependencies=null,bool deferCascadeNames=false)=>CompileWithCrossTables(catalog,tableId,outputNames,[],dependencies,deferCascadeNames);
    /// <summary>冻结目标页绑定仅用于跨表逻辑投影，现有公开编译入口及其二进制签名保持。</summary>
    public static MySqlNonRedundantReadPlan CompileWithCrossTables(MetadataCatalog catalog,string tableId,IReadOnlyList<string> outputNames,IReadOnlyList<NonRedundantCrossTableScope> crossTables,IReadOnlyList<string>? dependencies=null,bool deferCascadeNames=false) =>
        CompileRelationScope(catalog, tableId, outputNames, crossTables, dependencies, deferCascadeNames);

    /// <summary>普通 R1 候选只读取已声明的物理标量，并按当前项目及逻辑删除过滤。</summary>
    internal static MySqlNonRedundantReadPlan CompilePhysicalRelation(MetadataCatalog catalog, string tableId, IReadOnlyList<string> outputs, IReadOnlyList<string>? dependencies) =>
        CompileRelationScope(catalog, tableId, outputs, [], dependencies, false, scopedPhysicalCandidate: true);

    /// <summary>额外依赖只能是已冻结的候选页物理父键。</summary>
    internal static MySqlNonRedundantReadPlan CompilePhysicalRelation(MetadataCatalog catalog,string tableId,IReadOnlyList<string> outputs,
        IReadOnlyList<string>? dependencies,IReadOnlyList<RedundantSharedParentPlan> parents) =>
        CompileRelationScope(catalog,tableId,outputs,[],dependencies,false,scopedPhysicalCandidate:true,candidateParents:parents);

    /// <summary>固定业务页在同一读取计划中绑定单值目标和已有跨表路径。</summary>
    internal static MySqlNonRedundantReadPlan CompileProjectPage(MetadataCatalog catalog,string tableId,IReadOnlyList<string> outputs,
        IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantCrossTableScope> crossTables,
        IReadOnlyList<NonRedundantSingleTargetScope> singleTargets,IReadOnlyList<NonRedundantMultiTargetScope>? multiTargets=null)=>
        CompileRelationScope(catalog,tableId,outputs,crossTables,dependencies,deferCascadeNames:true,
            singleTargets:singleTargets,multiTargets:multiTargets);

    /// <summary>共用方言、输出与发布目录校验，候选用途单独由构建器核验。</summary>
    private static MySqlNonRedundantReadPlan CompileRelationScope(MetadataCatalog catalog, string tableId, IReadOnlyList<string> outputNames,
        IReadOnlyList<NonRedundantCrossTableScope> crossTables, IReadOnlyList<string>? dependencies, bool deferCascadeNames, bool scopedPhysicalCandidate = false,
        IReadOnlyList<RedundantSharedParentPlan>? candidateParents=null,IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets=null,
        IReadOnlyList<NonRedundantMultiTargetScope>? multiTargets=null)
    {
        ArgumentNullException.ThrowIfNull(crossTables);
        ArgumentNullException.ThrowIfNull(catalog); ArgumentNullException.ThrowIfNull(outputNames);
        MetadataValidation.Validate(catalog);
        if(!long.TryParse(catalog.YzProjectId,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var projectId)||projectId<=0)throw new InvalidOperationException("发布项目标识必须为正整数。");
        if(catalog.DatabaseProvider!="mysql")throw new NotSupportedException("当前固定读取执行仅完成MySQL适配。");
        if(outputNames.Count==0||outputNames.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=outputNames.Count)throw new InvalidOperationException("输出字段不能为空或重复。");
        var builder = new Builder(catalog, tableId, deferCascadeNames: deferCascadeNames, crossTables: crossTables, scopedPhysicalCandidate: scopedPhysicalCandidate,candidateParents:candidateParents)
        {
            ProjectSingleTargets=(singleTargets??[]).ToDictionary(binding=>binding.ColumnId,StringComparer.Ordinal),
            ProjectMultiTargets=(multiTargets??[]).ToDictionary(binding=>binding.ColumnId,StringComparer.Ordinal)
        };
        return builder.Compile(outputNames,dependencies??[]);
    }
    /// <summary>维护用途只重算声明的物理副本；普通读取仍坚持原冗余／非冗余边界。</summary>
    internal static MySqlNonRedundantReadPlan CompileRefresh(MetadataCatalog catalog,string tableId,IReadOnlyList<string> fields,IReadOnlyList<RelationCacheDefinition> multiple,IReadOnlyDictionary<string,string>? paths=null)
    {
        MetadataValidation.Validate(catalog);
        if(catalog.DatabaseProvider!="mysql")throw new NotSupportedException("维护回填尚未完成该方言验收。");
        return new Builder(catalog,tableId,refreshFields:fields.ToHashSet(StringComparer.Ordinal),refreshMultiples:multiple,refreshPaths:paths).Compile(["id",..fields,..multiple.Where(item=>item.Multiple).Select(item=>catalog.ConfigColumns.Single(column=>column.Id==item.ForeignColumnId).ColumnName).Distinct(StringComparer.Ordinal)],[]);
    }
    /// <summary>先完整验证目录，再以专用用途编译单一匹配字段；最终物理列必须仍为非凭据标量。</summary>
    public static MySqlNonRedundantReadPlan CompileMatch(MetadataCatalog catalog,string tableId,string field)=>
        CompileScalarMatch(catalog,tableId,field,multiLabel:false);

    /// <summary>多值名称允许原生 TIME 和已证明的规范文本外键，精确数据管理仍只接受原键类型。</summary>
    internal static MySqlNonRedundantReadPlan CompileMultiLabel(MetadataCatalog catalog,string tableId,string field)=>
        CompileScalarMatch(catalog,tableId,field,multiLabel:true);

    private static MySqlNonRedundantReadPlan CompileScalarMatch(MetadataCatalog catalog,string tableId,string field,bool multiLabel)
    {
        if(multiLabel)return CompileMultiLabel(catalog,tableId,field,[field],[]);
        Compile(catalog,tableId,[field]);return new Builder(catalog,tableId,true).Compile([field],[]);
    }

    /// <summary>候选、导入和缓存使用同一名称字段及已声明的范围依赖。</summary>
    internal static MySqlNonRedundantReadPlan CompileMultiLabel(MetadataCatalog catalog,string tableId,string field,IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies)
        =>CompileMultiLabel(catalog,tableId,field,outputs,dependencies,singleTargets:null);

    /// <summary>固定多值名称的中间级联按实际候选页联接，保留原多值键类型。</summary>
    internal static MySqlNonRedundantReadPlan CompileMultiLabel(MetadataCatalog catalog,string tableId,string field,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets)
        =>CompileMultiLabel(catalog,tableId,field,outputs,dependencies,cacheOwner:null,singleTargets:singleTargets);

    /// <summary>发布时验证名称缓存能以来源表真实行归属独立重算。</summary>
    internal static MySqlNonRedundantReadPlan CompileMultiLabelForCache(MetadataCatalog catalog,TableMetadata source,TableMetadata target,
        string field,PageMetadata? selectedPage,ColumnMetadata relation,bool sharedCheckbox)
        =>CompileMultiLabel(catalog,target.Id,field,[field],[],new(source,"cache_owner"),selectedPage,relation,sharedCheckbox:sharedCheckbox);

    private static MySqlNonRedundantReadPlan CompileMultiLabel(MetadataCatalog catalog,string tableId,string field,IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,CascadeCacheOwner? cacheOwner,PageMetadata? selectedPage=null,ColumnMetadata? relation=null,
        IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets=null,bool sharedCheckbox=false)
    {
        MetadataValidation.Validate(catalog);
        if(catalog.DatabaseProvider!="mysql")throw new NotSupportedException("多值名称要求已发布的MySQL读取计划。");
        if(outputs.Count==0||outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=outputs.Count||
            !long.TryParse(catalog.YzProjectId,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var project)||project<=0)
            throw new InvalidOperationException("多值名称的项目或输出字段无效。");
        var parent=cacheOwner is null||selectedPage is null?null:MultiParent(catalog,selectedPage,relation?.ControlKind=="MultiCheckBoxDatabase");
        MultiSourceParent? source=null;
        if(parent is not null)
        {
            var publishedRelation=relation??throw new InvalidOperationException("多值缓存缺少发布关系。");
            source=sharedCheckbox?parent.CheckboxSource(catalog,cacheOwner!.Table,publishedRelation):
                parent.Source(catalog,cacheOwner!.Table,publishedRelation);
        }
        if(parent is {Global:true}&&source is {AdditionalColumns.Count:>0})
            RequireCompoundGlobalSourceParent(catalog,tableId,selectedPage!.Id,source);
        return new Builder(catalog,tableId,true,multiLabel:true,cascadeLabelField:field,cascadeCacheOwner:cacheOwner)
        {
            SelectedMultiPage=selectedPage,
            AllowsExactMultiSelection=relation?.ControlKind=="MultiSelectPage",
            AllowsStoredExactCheckbox=relation?.ControlKind=="MultiCheckBoxDatabase",
            AllowLiteralMultiScope=sharedCheckbox,
            MultiNamePathPage=relation is null?null:MultiTargetNamePage(catalog,catalog.ConfigTables.Single(table=>table.Id==tableId),relation),
            ProjectSingleTargets=(singleTargets??[]).ToDictionary(binding=>binding.ColumnId,StringComparer.Ordinal),
            MultiParentOwner=source is null?null:(cacheOwner!,source.Column,source.AdditionalColumns),
            GlobalCrossCandidate=selectedPage is not null&&GlobalCrossTableTarget.Declared(catalog.ConfigTables.Single(table=>table.Id==tableId))
        }.Compile(outputs,dependencies);
    }
    /// <summary>构造时消费元数据，最终只保留不可变的表达式文本、类型和项目范围。</summary>
    private sealed partial class Builder
    {
        private readonly MetadataCatalog catalog;
        private readonly string rootAlias,aliasPrefix;
        private readonly bool matching,trackReverse,multiLabel;
        private readonly PageMetadata? candidatePage;
        private int publishedArrayScopeCount;
        private readonly List<MySqlReverseProjection> customReverse=[];
        private readonly IReadOnlySet<string>? refreshFields;
        private readonly IReadOnlyDictionary<string,string> refreshPaths;
        private readonly IReadOnlyList<RelationCacheDefinition> refreshMultiples;
        private readonly Dictionary<(string,string),MySqlPhysicalReference> references=new();
        private readonly HashSet<string> sourceDependencies=new(StringComparer.Ordinal);
        private MySqlValueOrigin? origin;
        private string? physicalOriginAlias;
        private readonly HashSet<string> directSaveAliases=new(StringComparer.Ordinal);
        private readonly Dictionary<string,HashSet<string>> directSaveOutputs=new(StringComparer.Ordinal);
        private string output="";
        private readonly Dictionary<string,(string Table,string From,string Foreign,string Type)> reverse=new(StringComparer.Ordinal);
        private readonly Dictionary<string,Dictionary<string,HashSet<string>>> aliasFields=new(StringComparer.Ordinal);
        private readonly bool deferCascadeNames;
        private IReadOnlySet<string> queryDependencies=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,TableMetadata> tables;
        private readonly Dictionary<string,Dictionary<string,ColumnMetadata>> columns;
        private readonly Dictionary<string,MetadataStoragePlan> storage;
        private readonly TableMetadata root;
        private readonly Dictionary<string,string> aliases=new(StringComparer.Ordinal);
        private readonly List<string> joins=[];
        private readonly List<(string Alias,string Sql)> maintenanceJoins=[];
        private readonly Dictionary<string, HashSet<string>> aggregateFields = new(StringComparer.Ordinal);
        private readonly List<(string Alias, string Sql)> aggregateJoins = [];
        private bool aggregateJoinsPreserveCardinality = true;
        private MySqlScalarCacheTarget? scalarCacheTarget;
        private readonly HashSet<string> globalSingleAliases=new(StringComparer.Ordinal);
        private readonly HashSet<string> globalDerivedAliases=new(StringComparer.Ordinal);
        private bool publishedArrayIdentity;
        private readonly Dictionary<string,string> multiPredicates=new(StringComparer.Ordinal);
        private readonly Dictionary<string,string> idSources=new(StringComparer.Ordinal);
        private readonly Dictionary<string,MySqlMultiProjection> multiProjections=new(StringComparer.Ordinal);
        private readonly HashSet<string> resolving=new(StringComparer.Ordinal);
        private readonly bool scopedPhysicalCandidate;
        private readonly IReadOnlyList<RedundantSharedParentPlan> candidateParents;
        private readonly bool accountPhysicalCandidate;
        internal Builder(MetadataCatalog catalog,string tableId,bool matching=false,bool deferCascadeNames=false,IReadOnlySet<string>? refreshFields=null,IReadOnlyList<RelationCacheDefinition>? refreshMultiples=null,IReadOnlyDictionary<string,string>? refreshPaths=null,IReadOnlyList<NonRedundantCrossTableScope>? crossTables=null,string rootAlias="b",string aliasPrefix="r",bool trackReverse=false,PageMetadata? candidatePage=null,bool multiLabel=false,string? cascadeLabelField=null,CascadeCacheOwner? cascadeCacheOwner=null,bool scopedPhysicalCandidate=false,IReadOnlyList<RedundantSharedParentPlan>? candidateParents=null,bool accountPhysicalCandidate=false,bool exactSingleCandidate=false,bool globalCandidate=false)
        {
            this.scopedPhysicalCandidate = scopedPhysicalCandidate;
            this.accountPhysicalCandidate=accountPhysicalCandidate;
            this.candidateParents=System.Array.AsReadOnly(candidateParents?.ToArray()??[]);
            this.rootAlias=rootAlias;this.aliasPrefix=aliasPrefix;this.candidatePage=candidatePage;this.multiLabel=multiLabel;this.cascadeLabelField=cascadeLabelField;this.cascadeCacheOwner=cascadeCacheOwner;
            this.crossTables=(crossTables??[]).ToDictionary(scope=>scope.SourceColumnId,StringComparer.Ordinal);
            this.catalog=catalog;this.matching=matching;this.trackReverse=trackReverse||refreshFields is not null;this.deferCascadeNames=deferCascadeNames;this.refreshFields=refreshFields;this.refreshMultiples=refreshMultiples??[];this.refreshPaths=refreshPaths??new Dictionary<string,string>();
            tables=catalog.ConfigTables.Where(table=>(table.YzProjectId.Length==0?catalog.YzProjectId:table.YzProjectId)==catalog.YzProjectId).ToDictionary(table=>table.Id,StringComparer.Ordinal);
            root=tables.GetValueOrDefault(tableId)??throw new InvalidOperationException("源表不属于当前项目。");
            var physicalCandidate = candidatePage is not null && refreshFields is null && root.IsRongyu == 1;
            // 全局 R0 的候选页可以跨项目读取，但必须同时满足显式全局声明和实际发布页证明。
            // 普通业务页仍保留项目行归属，未传入该精确标志的 R0 候选继续拒绝。
            var publishedGlobalCandidate = globalCandidate && candidatePage is not null && refreshFields is null &&
                root.IsRongyu == 0 && GlobalCrossTableTarget.Declared(root);
            if ((!physicalCandidate && !publishedGlobalCandidate && !scopedPhysicalCandidate && (refreshFields is null ? root.IsRongyu != 0 : root.IsRongyu != 1)) ||
                root.IsFunctionTable != 0 || root.IsView != 0)
                throw new InvalidOperationException("固定读取要求非冗余实体表或明确发布的物理候选。");
            if (candidatePage is not null)
            {
                if (exactSingleCandidate) _=GlobalCrossTableTarget.ExactPageParent(catalog,root,candidatePage);
                else if (physicalCandidate&&!accountPhysicalCandidate) GlobalCrossTableTarget.Require(catalog, root, candidatePage);
                else GlobalCrossTableTarget.RequireAccountCandidate(catalog, root, candidatePage);
            }
            if(tables.Values.GroupBy(table=>table.TableName,StringComparer.OrdinalIgnoreCase).Any(group=>group.Count()!=1))throw new InvalidOperationException("项目表名重复。");
            if(catalog.ConfigColumns.Any(column=>tables.ContainsKey(column.ConfigTableId)&&column.YzProjectId.Length>0&&column.YzProjectId!=catalog.YzProjectId))throw new InvalidOperationException("字段归属与发布项目不一致。");
            columns=tables.Values.ToDictionary(table=>table.Id,table=>RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id)).ToDictionary(column=>column.ColumnName,StringComparer.OrdinalIgnoreCase),StringComparer.Ordinal);
            storage=tables.Values.ToDictionary(table=>table.Id,table=>MetadataStoragePolicy.ForTable(catalog,table),StringComparer.Ordinal);
            if (scopedPhysicalCandidate && (root.IsRongyu != 1 || GlobalCrossTableTarget.Declared(root) ||
                !columns[root.Id].TryGetValue("yz_project_id", out var project) ||
                project.LogicalType is not ("int64" or "string") || !GlobalPhysicalRelationValue.IsScalar(storage[root.Id], root, project)))
                throw new InvalidOperationException("项目内物理候选要求普通 R1 实体及其真实项目列。");
            foreach(var binding in this.crossTables.Values)if(!columns[root.Id].Values.Any(column=>column.Id==binding.SourceColumnId&&column.ControlKind=="CascadeTree"))throw new InvalidOperationException("跨表读取绑定不属于当前来源表。");
        }
        /// <summary>主键作为稳定排序依赖；根及每个目标分别保留软删和项目范围。</summary>
        internal MySqlNonRedundantReadPlan Compile(IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies)
        {
            if(SelectedMultiPage is not null)MultiCandidateParent??=MultiParent(catalog,SelectedMultiPage,AllowsStoredExactCheckbox);
            var globalSource=refreshFields is not null&&GlobalCrossTableTarget.Declared(root);
            var globalDerivedSource = refreshFields is null && root.IsRongyu == 0 && GlobalCrossTableTarget.Declared(root);
            var physicalCandidate = candidatePage is not null && root.IsRongyu == 1;
            if ((physicalCandidate || scopedPhysicalCandidate) && outputs.Concat(dependencies).Append("id")
                .Distinct(StringComparer.OrdinalIgnoreCase).Any(field => !CandidateField(field)))
                throw new NotSupportedException("物理候选只读取已声明的非凭据标量和明确发布的标量名称副本。");

            // 缓存仅作为显式返回的全局名称。复选投影已验证实际名称，不能将缓存列用作独立条件。
            bool CandidateField(string field)
            {
                var column = Column(root,field);
                var rootStorage = storage[root.Id];
                return GlobalPhysicalRelationValue.IsScalar(rootStorage,root,column) ||
                    scopedPhysicalCandidate && !outputs.Contains(field,StringComparer.OrdinalIgnoreCase) &&
                    rootStorage.IsPhysical(column) && candidateParents.Any(parent=>parent.ColumnName==field&&
                        parent.ColumnId==column.Id&&parent.LogicalType==column.LogicalType) ||
                    physicalCandidate && outputs.Contains(field,StringComparer.OrdinalIgnoreCase) &&
                    (MultiValueLabel.IsTyped(column.LogicalType) || multiLabel && field == cascadeLabelField) &&
                    GlobalPhysicalRelationValue.IsPublishedScalar(catalog,rootStorage,root,column);
            }
            if(globalSource)
            {
                GlobalCachedRelationSource.RequireSelfTree(catalog,root);
                foreach(var name in refreshFields!)
                {
                    var field=columns[root.Id][name];
                    var tree=refreshMultiples.SingleOrDefault(binding=>binding.CacheColumnId==field.Id&&
                        SelfTreeCache.UsesWholePath(catalog,root,binding.SelfTreePageId));
                    if(tree is null)
                    {
                        var proof=GlobalCachedRelationSource.RequireCache(catalog,root,field);
                        if(proof.ScopedSingle&&!refreshMultiples.Any(binding=>binding.CacheColumnId==field.Id&&binding.ScopedSinglePageId==proof.Page.Id))
                            throw new InvalidOperationException("全局固定单值副本缺少实际发布页的维护绑定。");
                    }
                    else
                    {
                        var proof=SelfTreeCache.Compile(catalog,root.Id,tree.ForeignColumnId,tree.SelfTreePageId);
                        if(proof!=tree)throw new InvalidOperationException("全局自树回填字段与固定发布定义不一致。");
                    }
                }
            }
            if(globalDerivedSource)
            {
                // 带行归属的普通页可绑定自己的项目条件；候选计划仅证明实际发布的目标页。
                GlobalDerivedRelationSource.RequireTable(catalog,root);
                if(!HasGlobalRowProject(root)&&SelectedMultiPage is null&&candidatePage is null)
                    foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId==root.Id))
                    {
                        // 这里只证明已发布页面结构，公开请求的精确权限仍由生成服务执行。
                        if(GlobalCrossTableTarget.DeclaresExactPage(root,page))
                        {
                            _=GlobalCrossTableTarget.ExactPageParent(catalog,root,page);
                            continue;
                        }
                        // 页面投影由生成服务另外附加自己的父筛选；任意单值读取仍须用无父参数的候选。
                        var multiPage=page.Extra.GetValueOrDefault("type").ToString()=="2"||IsScopedMultiTarget(catalog,root,page.Id)||
                            GlobalCrossTableTarget.SingleParent(catalog,root,page) is not null;
                        if(deferCascadeNames)GlobalCrossTableTarget.RequireSelfTreeConditionCandidate(catalog,root,page,allowMultiParent:crossTables.Count>0||multiPage);
                        else if(crossTables.Count>0||multiPage)GlobalCrossTableTarget.RequireMultiCandidate(catalog,root,page);
                        else GlobalCrossTableTarget.RequireCrossTableCandidate(catalog,root,page);
                    }
                var controls=columns[root.Id].Values.Where(column=>crossTables.ContainsKey(column.Id)).ToArray();
                foreach(var field in outputs.Concat(dependencies).Append("id").Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if(!columns[root.Id].ContainsKey(field))
                    {
                        RequireGlobalSingleDisplay(root,field);
                        continue;
                    }
                    var column=Column(root,field);
                    var logical=column.IsVirtualColumn==0&&!Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(field)&&
                        (controls.Any(control=>CrossTableStorageColumns.ContainsPathField(control,field))||
                         SelectedMultiPage is not null&&(field==cascadeLabelField||
                         IsSelectedLogicalParent(field))&&
                         CrossTableStorageColumns.IsLogicalPathField(root,columns[root.Id].Values.ToArray(),field));
                    if(!logical&&!PublishedGlobalSelfTreeField(column)&&!PublishedGlobalValue(column))
                        throw new NotSupportedException("全局R0读取只接受已证明的标量、单值名称与固定级联字段。");
                }
            }
            queryDependencies=new HashSet<string>(dependencies,StringComparer.OrdinalIgnoreCase);
            if(candidateParents.Any(parent=>!queryDependencies.Contains(parent.ColumnName)||
                !columns[root.Id].TryGetValue(parent.ColumnName,out var field)||field.Id!=parent.ColumnId||
                field.LogicalType!=parent.LogicalType||!storage[root.Id].IsPhysical(field)))
                throw new InvalidOperationException("固定候选缺少与已发布范围一致的物理父键。");
            var id=Column(root,"id");if(id.LogicalType!="int64"||id.IsDbNotNull!=1||!storage[root.Id].IsPhysical(id))throw new InvalidOperationException("读取源缺少非空int64物理主键。");
            var expressions=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var types=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var origins=new Dictionary<string,MySqlValueOrigin>(StringComparer.Ordinal);
            foreach(var name in outputs.Concat(dependencies).Append("id").Distinct(StringComparer.OrdinalIgnoreCase))
            {
                output=name;origin=null;physicalOriginAlias=null;
                if(IsSelectedLogicalParent(name))
                    continue; // 父层位由选中页的完整可见路径解析，不能回退到稀疏原始列。
                var nameCacheOwner=MultiNamePathPage is not null?cascadeCacheOwner:null;
                var resolved=Resolve(root,rootAlias,name,nameCacheOwner);expressions.Add(name,resolved.Sql);types.Add(name,resolved.Type);if(origin is not null)origins[name]=origin;
                // 派生联接或数组即使触及物理列，也只有直接返回该列时才沿用保存刷新。
                if(physicalOriginAlias is{} alias&&directSaveAliases.Contains(alias)&&origin is{} terminal&&resolved.Sql==alias+"."+Q(terminal.Column))
                {
                    if(!directSaveOutputs.TryGetValue(alias,out var fields))directSaveOutputs[alias]=fields=new(StringComparer.Ordinal);
                    fields.Add(name);
                }
            }
            // 全局树允许跨行项目查节点；来源若保留物理项目列，回填和反查仍限该项目。
            var sharedRefresh=globalSource&&!columns[root.Id].ContainsKey("yz_project_id");
            var scope=candidatePage is null
                ?Scope(root,rootAlias,projectScoped:!sharedRefresh&&!GlobalCrossCandidate&&(!globalDerivedSource||HasGlobalRowProject(root)))
                :PublishedScope(root,rootAlias,candidatePage,aliasPrefix+"root",allowAccount:!physicalCandidate||accountPhysicalCandidate);
            if((globalSource||globalDerivedSource)&&scope.Length==0)scope="1=1";
            if(CrossCandidateCondition is not null||ScopedRootControls.Count>0)scope=FixedCrossCandidateScope(scope,expressions);
            if(SelectedMultiPage is not null)scope=SelectedMultiScope(scope);
            if(MultiCandidateParent is {} multiParent)
            {
                var field=multiParent.Target.ColumnName;
                var parentSql=multiParent.Logical?selectedMultiParentSql??expressions[field]:rootAlias+"."+Q(field);
                if(selectedMultiParentSql is not null){expressions[field]=parentSql;types[field]=multiParent.Target.LogicalType;}
                scope+=(scope.Length==0?"":" AND ")+MultiParentPredicate(multiParent,parentSql)+
                    ApplyAdditionalMultiParents(multiParent,parentSql,expressions,types);
            }
            var reversePlans=reverse.Select(item=>new MySqlReverseProjection(item.Value.Table,item.Value.From,item.Value.Foreign,item.Value.Type,
                new ReadOnlyDictionary<string,IReadOnlySet<string>>(aliasFields[item.Key].ToDictionary(field=>field.Key,field=>(IReadOnlySet<string>)field.Value.ToFrozenSet(StringComparer.Ordinal),StringComparer.Ordinal)))
            {
                DirectSaveOutputs=directSaveOutputs.TryGetValue(item.Key,out var fields)?fields.ToFrozenSet(StringComparer.Ordinal):FrozenSet<string>.Empty
            }).Concat(customReverse).ToArray();
            // 全局是关联目标的读取资格；根自身已有真实项目列时仍保留该来源范围。
            var importLock=" FOR UPDATE OF "+rootAlias+(aliases.Count==0?"":" FOR SHARE OF "+string.Join(",",aliases.Values));
            // 计数只能省去以整数键查找单列主键的 LEFT JOIN。跨表有效性校验和当前锁读沿用完整联接。
            var aggregateFrom = aggregateJoinsPreserveCardinality && refreshFields is null &&
                !matching && crossProjections.Count == 0 && scopedCrossProjections.Count == 0 && aggregateJoins.Count > 0
                ? new MySqlMaintenanceReadFrom(" FROM " + Q(root.TableName) + " " + rootAlias,
                    aggregateJoins.Select(join => (join.Sql, (IEnumerable<string>)aggregateFields[join.Alias])))
                : null;
            return new(" FROM "+Q(root.TableName)+" "+rootAlias+string.Concat(joins),catalog.YzProjectId,scope,expressions,types,outputs.ToArray(),expressions.Where(item=>multiPredicates.ContainsKey(item.Value)).ToDictionary(item=>item.Key,item=>multiPredicates[item.Value],StringComparer.OrdinalIgnoreCase),expressions.Where(item=>multiProjections.ContainsKey(item.Value)).ToDictionary(item=>item.Key,item=>multiProjections[item.Value],StringComparer.OrdinalIgnoreCase),expressions.Where(item=>idSources.ContainsKey(item.Value)).ToDictionary(item=>item.Key,item=>idSources[item.Value],StringComparer.OrdinalIgnoreCase),System.Array.AsReadOnly(references.Values.ToArray()),new ReadOnlyDictionary<string,MySqlValueOrigin>(origins),System.Array.AsReadOnly(reversePlans),System.Array.AsReadOnly(crossProjections.Values.ToArray()),new ReadOnlyDictionary<string,object?>(crossConstants),System.Array.AsReadOnly(sourceDependencies.Order(StringComparer.Ordinal).ToArray()),refreshFields is null?null:new MySqlMaintenanceReadFrom(" FROM "+Q(root.TableName)+" "+rootAlias,maintenanceJoins.Select(join=>(join.Sql,(IEnumerable<string>)aliasFields[join.Alias].Keys)))){HasPublishedCandidateCondition=CrossCandidateCondition is not null||
                MultiCandidateParent is {Logical:true}&&ScopedRootControls.Count>0,RequiredMultiParent=MultiParentOwner is null?MultiCandidateParent?.Target.ColumnName:null,CandidateParents=candidateParents,AggregateFrom=aggregateFrom,CascadeLabelOutputs=cascadeLabelOutputs.ToFrozenSet(StringComparer.OrdinalIgnoreCase),CascadeLabelPages=cascadeLabelPages.ToFrozenSet(StringComparer.Ordinal),CascadeCacheOwnerFields=cascadeCacheOwnerFields.ToFrozenSet(StringComparer.Ordinal),ImportMatchLock=importLock,ScalarCacheTarget=maintenanceJoins.Count==1?scalarCacheTarget:null,RequiresPublishedProjectIdentity=globalSource||globalDerivedSource||physicalCandidate||trackReverse&&globalDerivedAliases.Count>0||publishedArrayIdentity||scopedGlobalCrossTarget};
        }
        /// <summary>仅遍历真正的字段依赖，允许自关联但拒绝递归字段循环。</summary>
        private (string Sql,string Type) Resolve(TableMetadata table,string alias,string name,CascadeCacheOwner? singleCacheOwner=null)
        {
            MetadataValidation.RequireIdentifier(name);var key=table.Id+":"+name;
            if(!resolving.Add(key))throw new InvalidOperationException("非冗余字段存在循环依赖。");
            try
            {
                var column=columns[table.Id].GetValueOrDefault(name);
                if(column is not null&&TryCascadeLabel(table,alias,column) is {} label)return label;
                if(alias==rootAlias&&table.Id==root.Id&&refreshFields?.Contains(name)==true&&refreshMultiples.SingleOrDefault(item=>item.CacheColumnId==column?.Id) is{} refresh)
                {
                    var foreign=columns[table.Id].Values.Single(item=>item.Id==refresh.ForeignColumnId);var target=tables[refresh.TargetTableId];var value=columns[target.Id].Values.Single(item=>item.Id==refresh.ValueColumnId);
                    if(refresh.Multiple)return MultiDisplay(table,alias,foreign,value.ColumnName,refresh.CheckboxTargetPageId,refresh.CheckboxSourceParentId);
                    if(refresh.ScopedSinglePageId is not null)return ScopedSingleCacheValue(table,alias,foreign,target,value,refresh);
                    if(SelfTreeCache.UsesWholePath(catalog,table,refresh.SelfTreePageId))
                        return (SelfTreeLabelExpression(table,alias,foreign,column!,refresh.SelfTreePageId),"string");
                    if(refresh.CrossTableRecursivePageId is not null)return RecursiveCacheValue(table,alias,foreign,target,value,refresh);
                    // 标量级联缓存的精确目标由已验证定义确定，叶子无需伪装成普通List关系。
                    ValidateTarget(target);var crossGlobal=refresh.CrossTableColumnId is not null&&GlobalCrossTableTarget.Declared(target);
                    if(crossGlobal)_=CrossTableCacheProof.Require(catalog,table,foreign,target,value,column!,refresh,refreshMultiples);
                    var pageId=refresh.SelfTreePageId??(crossGlobal&&(target.IsRongyu==0||MultiValueLabel.IsTyped(value.LogicalType))&&
                        refresh.CrossTableAccountPageId is null?refresh.CrossTablePageId:null);
                    var publishedPage=pageId is null?null:catalog.ConfigPages.Single(page=>page.Id==pageId);
                    var account=refresh.CrossTableAccountPageId is{} accountPage
                        ?AccountScopedCrossCache.Compile(catalog,table,target,catalog.ConfigPages.Single(page=>page.Id==accountPage))
                        :null;
                    var id=Resolve(table,alias,foreign.ColumnName);
                    var projected=Resolve(target,Join(alias,foreign,target,id.Sql,id.Type,crossGlobal||publishedPage is not null,cachePage:publishedPage,account:account),value.ColumnName);
                    // 自树和跨表的受管标量名称共用文本格式，便于候选与回填结果一致。
                    var textLabel=(refresh.CrossTableColumnId is not null||foreign.ControlKind=="CascadeTree")&&MultiValueLabel.IsTyped(projected.Type);
                    return textLabel?(MySqlMultiValueLabel.Text(projected.Sql,projected.Type),"string"):projected;
                }
                foreach(var tree in columns[table.Id].Values.Where(item=>item.ControlKind=="CascadeTree"&&item.ColumnName.EndsWith("_id",StringComparison.Ordinal)))
                {
                    if(table.IsRongyu!=0||!name.Equals(tree.ColumnName[..^3]+"_name",StringComparison.OrdinalIgnoreCase))continue;
                    var definition=Definition(tree);if(Value(definition,"sourceMode")!="selfTree")continue;
                    var scopedLeaf=SelfTreeCandidateFields.Contains(name);
                    if(matching||alias!=rootAlias||!deferCascadeNames&&!scopedLeaf)throw new NotSupportedException("级联名称要求已证明的来源图投影，不能当作精确匹配或跨表派生值。");
                    // 原叶名筛选、排序和候选条件都验证整条可见祖先链。
                    if(scopedLeaf||queryDependencies.Contains(name))
                    {
                        if(column is null||column.LogicalType!="string"||storage[table.Id].IsPhysical(column))
                            throw new NotSupportedException("自树名称查询依赖须为已声明的逻辑文本名称。");
                        cascadeLabelOutputs.Add(output);
                        return (SelfTreeLabelExpression(table,alias,tree,column),"string");
                    }
                    if(!storage[table.Id].IsPhysical(tree)||!definition.TryGetProperty("selfTree",out var settings)||settings.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("自树派生名称缺少物理来源和固定定义。");
                    var target=tables.Values.SingleOrDefault(item=>item.TableName==Value(settings,"table"))??throw new InvalidOperationException("自树名称目标不属于发布项目。");ValidateTarget(target);
                    var targetName=Value(settings,"nameField");if(targetName.Length==0)targetName="name";var value=Column(target,targetName);
                    if((value.LogicalType!="string"&&!MultiValueLabel.IsTyped(value.LogicalType))||Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(value.ColumnName))throw new NotSupportedException("自树叶子名称必须为已声明的非凭据标量。");
                    if(!storage[target.Id].IsPhysical(value))
                    {
                        if(target.IsRongyu!=0)throw new NotSupportedException("自树派生名称要求R0目标。");
                        var globalTarget=GlobalCrossTableTarget.Declared(target);
                        var targetPage=globalTarget&&SelfTreeLabel(catalog,tree) is {Scoped:false} fixedTree?fixedTree.Page:null;
                        var nested=new Builder(catalog,target.Id,matching:!globalTarget,candidatePage:targetPage,
                            globalCandidate:globalTarget&&target.IsRongyu==0&&targetPage is not null).Compile([value.ColumnName],[]);
                        if(nested.Types[value.ColumnName]!=value.LogicalType||nested.MultiProjections.Count>0||nested.Crosses.Count>0||!nested.Origins.TryGetValue(value.ColumnName,out var terminal)||Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(terminal.Column))throw new NotSupportedException("自树名称必须落到同类型的非凭据标量。");
                    }
                    // 明确交由生成服务的固定图投影，SQL阶段不读取未经完整祖先范围证明的叶子名称。
                    // 只有名称不参与查询依赖时使用占位，由生成服务统一补齐名称和展示路径。
                    return ("NULL","string");
                }
                // 稀疏存储位不是完整逻辑路径；固定SQL投影接通前拒绝将原始空位当作祖先值发布。
                foreach(var tree in columns[table.Id].Values.Where(item=>item.ControlKind=="CascadeTree"))
                {
                    var definition=Definition(tree);if(table.IsRongyu!=0||Value(definition,"sourceMode")=="selfTree")continue;
                    var shared=SharedCrossGroup(table,tree.Id);var sparse=shared?.BranchSelection(tree.Id)??CrossTableSparseSelection.Compile(table,tree.Id,columns[table.Id].Values);
                    if(sparse.Slots.Any(slot=>slot.ColumnName.Equals(name,StringComparison.OrdinalIgnoreCase)||(slot.ColumnName[..^3]+"_name").Equals(name,StringComparison.OrdinalIgnoreCase)))
                    {
                        if(!crossTables.TryGetValue(tree.Id,out var binding)||matching||refreshFields is not null)throw new NotSupportedException("非冗余跨表逻辑路径缺少明确目标页绑定或当前用途尚未接线。");
                        return shared is null?CrossValue(table,alias,tree,sparse,binding,name):SharedCrossValue(table,alias,shared,name);
                    }
                }
                // 多值显示先识别明确的_ids关系，聚合留在相关子查询内，不扩张根结果的基数。
                foreach(var multi in columns[table.Id].Values.Where(item=>item.ControlKind is "MultiSelectPage" or "MultiCheckBoxDatabase"&&item.ColumnName.EndsWith("_ids",StringComparison.Ordinal)))
                {
                    var display=Value(Definition(multi),"nameField");if(display.Length==0)display="name";
                    var auxiliary=multi.ColumnName[..^4]+"_"+(display.EndsWith('s')?display:display+"s");
                    if(display=="id"||!name.Equals(auxiliary,StringComparison.OrdinalIgnoreCase))continue;
                    if(matching)throw new NotSupportedException("精确管理匹配不能使用多值派生名称。");
                    if(column is not null&&(Kind(table,alias,column)!=MetadataFieldStorage.Join||Text(column,"columnFullName")!=multi.ColumnName[..^4]+"."+display&&Text(column,"columnFullName")!=multi.ColumnName+"."+display))throw new InvalidOperationException("多值显示字段必须声明同一关系与名称路径。");
                    return MultiDisplay(table,alias,multi,display);
                }
                if(InferredSingleDisplay(table,name,alias,column) is {} inferred)
                {
                    var target=Target(inferred.Foreign);
                    var joined=JoinSinglePath(table,alias,inferred.Foreign,target,singleCacheOwner);
                    var value=Resolve(target,joined.Alias,inferred.Display,joined.CacheOwner);
                    return joined.Visibility is null?value:("CASE WHEN "+joined.Visibility+" THEN "+value.Sql+" END",value.Type);
                }
                if(column is not null)
                {
                    var kind=Kind(table,alias,column);
                    if(kind==MetadataFieldStorage.Physical)
                    {
                        if(globalSingleAliases.Contains(alias)&&!GlobalPhysicalRelationValue.IsPublishedScalar(catalog,storage[table.Id],table,column))throw new NotSupportedException("全局单值路径不能读取凭据、文件或数组。");
                        if(refreshFields is not null&&(Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(column.ColumnName)||table.TableName=="yz_manager"&&column.ColumnName=="salt"))throw new NotSupportedException("维护回填不能读取凭据或令牌字段。");
                        if(matching)
                        {
                            if(Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(column.ColumnName)||column.ControlKind is "FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "CheckBoxList" or "MultiList"||column.ColumnName.EndsWith("_ids",StringComparison.Ordinal)||column.LogicalType=="json")throw new NotSupportedException("精确管理匹配路径只能使用非凭据单值列。");
                            if(!multiLabel||column.LogicalType!="time")Metarill.Core.Security.ExactDataManageValues.Unrestricted.ReadLogical(column.LogicalType);
                        }
                        if(refreshFields is not null&&alias!=rootAlias&&(column.ControlKind is "FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "CheckBoxList" or "MultiList"||column.LogicalType is "json" or "binary"))throw new NotSupportedException("维护派生文件或数组路径需要独立事务合同，未执行部分回填。");
                        Use(table,column);Touch(alias,column.ColumnName);origin=new(table.TableName,column.ColumnName);physicalOriginAlias=alias;
                        var expression=alias+"."+Q(column.ColumnName);
                        if (alias == rootAlias && MetadataStoragePolicy.IsMultiIdRelation(column))
                        {
                            if (column.LogicalType != "string") throw new NotSupportedException("多值关系必须以字符串保存 JSON ID 数组。");
                            idSources[expression] = Array(expression);
                        }
                        return(expression,column.LogicalType);
                    }
                    if(kind==MetadataFieldStorage.Join)
                    {
                        var path=(alias==rootAlias&&table.Id==root.Id&&refreshPaths.TryGetValue(column.ColumnName,out var refreshPath)?refreshPath:Text(column,"columnFullName")).Split('.');if(path.Length<2||path.Any(string.IsNullOrWhiteSpace))throw new InvalidOperationException("联接字段缺少有效来源路径。");
                        var current=table;var currentAlias=alias;
                        List<string>? visibility=null;
                        foreach(var segment in path[..^1])
                        {
                            MetadataValidation.RequireIdentifier(segment);
                            var relation=columns[current.Id].GetValueOrDefault(segment+"_id")??columns[current.Id].GetValueOrDefault(segment)
                                ??throw new InvalidOperationException("联接路径关系不存在。");
                            var target=Target(relation);
                            var joined=JoinSinglePath(current,currentAlias,relation,target,singleCacheOwner);
                            currentAlias=joined.Alias;
                            singleCacheOwner=joined.CacheOwner;
                            if(joined.Visibility is not null)(visibility??=new()).Add(joined.Visibility);
                            current=target;
                        }
                        var value=Resolve(current,currentAlias,path[^1],singleCacheOwner);
                        return visibility is null?value:("CASE WHEN "+string.Join(" AND ",visibility)+" THEN "+value.Sql+" END",value.Type);
                    }
                    if(kind is MetadataFieldStorage.Virtual or MetadataFieldStorage.Layout)throw new NotSupportedException("当前单值计划不支持虚拟或布局读取字段。");
                    if(name is "insert_manager_real_name" or "update_manager_real_name")
                    {var relation=Column(table,name.Replace("_real_name","_id",StringComparison.Ordinal));var target=tables.Values.SingleOrDefault(item=>item.TableName=="yz_manager")??throw new InvalidOperationException("缺少固定管理员基础设施元数据。");ValidateTarget(target);var id=Resolve(table,alias,relation.ColumnName);return Resolve(target,Join(alias,relation,target,id.Sql,id.Type),"real_name");}
                }

                throw new NotSupportedException("字段不是已支持的物理列或单值派生关系："+name);
            }
            finally{resolving.Remove(key);}
        }
        /// <summary>仅编译明确多选的物理ID与名称；窗口聚合按原数组顺序保留缺失目标的null位置。</summary>
        private (string Sql,string Type) MultiDisplay(TableMetadata table,string owner,ColumnMetadata relation,string display,
            string? checkboxPageId=null,string? checkboxSourceParentId=null)
        {
            if(matching)throw new NotSupportedException("固定单值名称路径不能穿过多值关系或数组。");
            if(!storage[table.Id].IsPhysical(relation)||relation.LogicalType!="string")throw new NotSupportedException("多值关系必须以物理字符串列存储JSON ID数组。");
            var definition=Definition(relation);TableMetadata? target=null;
            var id=Value(definition,"tableId");if(id.Length>0)target=tables.GetValueOrDefault(id);
            var pageId=MultiPageId(definition);if(target is null&&pageId.Length>0)target=PageTarget(pageId);
            if(target is null&&relation.LinkConfigTableId.Length>0)target=tables.GetValueOrDefault(relation.LinkConfigTableId);
            if(target is null&&Text(relation,"linkConfigPageId").Length>0)target=PageTarget(Text(relation,"linkConfigPageId"));
            if(target is null){var targetName=Value(definition,"table");if(targetName.Length==0)targetName=Text(relation,"joinTable");target=tables.Values.SingleOrDefault(item=>item.TableName==targetName);}
            // 原复选仅在完全没有有效目标声明时使用字段前缀，显式坏表／页不能借回退串表。
            if(target is null&&relation.ControlKind=="MultiCheckBoxDatabase"&&id.Length==0&&pageId.Length==0&&relation.LinkConfigTableId.Length==0&&Text(relation,"linkConfigPageId").Length==0&&Value(definition,"table").Length==0&&Text(relation,"joinTable").Length==0)target=tables.Values.SingleOrDefault(item=>item.TableName==relation.ColumnName[..^4]);
            if(target is null)throw new InvalidOperationException("多值关联缺少同项目目标。");ValidateTarget(target);
            if(id.Length>0&&tables.GetValueOrDefault(id)?.Id!=target.Id||pageId.Length>0&&PageTarget(pageId)?.Id!=target.Id||Value(definition,"table") is {Length:>0} declared&&declared!=target.TableName||Text(relation,"linkConfigPageId") is {Length:>0} linkedPage&&PageTarget(linkedPage)?.Id!=target.Id)throw new InvalidOperationException("多值关系的目标声明缺失或相互矛盾。");
            if(relation.LinkConfigTableId.Length>0&&relation.LinkConfigTableId!=target.Id)throw new InvalidOperationException("多值关系目标声明不一致。");
            var global=GlobalCrossTableTarget.Declared(target);
            var (sourcePage,overridden,applyPageScope,control,nameOverride)=ProjectMultiTarget(table,owner,relation,target);
            display=nameOverride??display;
            var value=Column(target,display);
            if(checkboxSourceParentId is not null)
            {
                var (page,targetParent,sourceParent)=MySqlPublishedRelationCondition.RequireProjectCheckboxParent(
                    catalog,table,relation,target,checkboxPageId??throw new InvalidOperationException("缺少复选目标页。"),checkboxSourceParentId);
                return DerivedMultiDisplay(table,owner,relation,target,value,null,page,page,null,null,(targetParent,sourceParent));
            }
            var typedCache=GlobalPhysicalRelationValue.IsPublishedTypedCache(catalog,storage[target.Id],target,value);
            PageMetadata? publishedPage=null;
            PageMetadata? sharedCheckboxPage=null;
            if(global)
            {
                if(checkboxPageId is {} sharedPage)
                    publishedPage=sharedCheckboxPage=MySqlPublishedRelationCondition.RequireSharedCheckboxCachePage(catalog,table,relation,target,value,sharedPage);
                else
                {
                    var proof=table.IsRongyu==1?GlobalMultipleRelationTarget.RequireCached(catalog,table,relation,target):GlobalMultipleRelationTarget.Require(catalog,table,relation,target);
                    // 只有当前来源页已按同一发布目标重新证明的复选名称，才可覆盖表级名称。
                    if(proof.Name.ColumnName!=display&&nameOverride is null)
                        throw new NotSupportedException("全局多值显示必须与固定名称声明一致。");
                    // 名称副本的读取、筛选及回填使用同一发布候选范围。
                    if(HasGlobalRowProject(target)||typedCache||
                        relation.ControlKind=="MultiCheckBoxDatabase"&&!string.IsNullOrWhiteSpace(proof.Page.WhereSql))
                        publishedPage=proof.Page;
                }
                if(trackReverse&&target.IsRongyu==0||typedCache)publishedArrayIdentity=true;
            }
            if(value.ControlKind is "FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase"||Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(value.ColumnName))throw new NotSupportedException("多值名称必须为非凭据的受支持标量。");
            var namePage=overridden??MultiTargetNamePage(catalog,target,relation);
            var selectedScope=overridden is null?MultiTargetLogicalPage(catalog,target,relation):applyPageScope?overridden:null;
            var derivedLabel=!storage[target.Id].IsPhysical(value);
            if(!global&&checkboxPageId is {} scopedCheckboxPage&&(selectedScope is not null||derivedLabel))
                sharedCheckboxPage=MySqlPublishedRelationCondition.RequireSharedCheckboxCachePage(catalog,table,relation,target,value,scopedCheckboxPage);
            // 物理副本仍走原固定范围，派生副本须在它自己的目标联接内执行同一发布条件。
            if(derivedLabel&&sharedCheckboxPage is not null)selectedScope=sharedCheckboxPage;
            if(control is not null||selectedScope is not null||!storage[target.Id].IsPhysical(value))
                return DerivedMultiDisplay(table,owner,relation,target,value,publishedPage,selectedScope,namePage,sourcePage,control,
                    sharedCheckbox:checkboxPageId is not null);
            var scalarLabel=typedCache||MultiNameStorageColumns.IsPhysicalScalarLabel(table,relation,target,value,storage[target.Id]);
            if(value.LogicalType!="string"&&!scalarLabel)throw new NotSupportedException("多值物理名称类型未支持。");
            Use(table,relation);
            if(scalarLabel||global&&checkboxPageId is not null)Use(target,value);
            var label=MySqlMultiValueLabel.Text("multi_value."+Q(display),value.LogicalType);
            var source=owner+"."+Q(relation.ColumnName);
            // JSON_TYPE只在有效JSON分支求值，损坏历史值与非数组按原空集合处理，不能让JSON_TABLE整页失败。
            var array=Array(source);
            // 普通复选维护和传播都应用同一实际发布页，仍按来源行的项目范围读取。
            var targetScope=!global&&checkboxPageId is {} fixedPage
                ?PublishedScope(target,"multi_value",MySqlPublishedRelationCondition.RequireProjectCheckboxPage(
                    catalog,table,relation,target,fixedPage),aliasPrefix+"array"+publishedArrayScopeCount++,projectParent:true)
                :publishedPage is null
                    ?Scope(target,"multi_value",projectScoped:!global)
                    :PublishedScope(target,"multi_value",publishedPage,aliasPrefix+"array"+publishedArrayScopeCount++);
            var expression="CAST(COALESCE((SELECT JSON_ARRAYAGG("+label+") OVER (ORDER BY multi_ids.ordinal ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) FROM JSON_TABLE("+array+", '$[*]' COLUMNS(ordinal FOR ORDINALITY, member_id VARCHAR(32) PATH '$' NULL ON ERROR)) multi_ids LEFT JOIN "+Q(target.TableName)+" multi_value ON "+(relation.ControlKind=="MultiCheckBoxDatabase"||global?"CAST(CAST(multi_value.`id` AS CHAR) AS BINARY)=CAST(multi_ids.member_id AS BINARY)":"CAST(multi_value.`id` AS CHAR)=multi_ids.member_id")+(targetScope.Length==0?"":" AND "+targetScope)+" ORDER BY multi_ids.ordinal LIMIT 1),JSON_ARRAY()) AS CHAR)";
            multiProjections[expression]=new(source,Q(target.TableName),Q(display),targetScope,NameExpression:scalarLabel?label:null);
            // 名称筛选直接对目标做EXISTS，不先聚合整份名称数组，也不把根记录JOIN成多行。
            multiPredicates[expression]="EXISTS (SELECT 1 FROM "+Q(target.TableName)+" multi_value WHERE JSON_CONTAINS("+array+",JSON_QUOTE(CAST(multi_value.`id` AS CHAR)))"+(targetScope.Length==0?"":" AND "+targetScope)+" AND "+label+" LIKE {parameter} ESCAPE '!')";
            if(relation.ControlKind=="MultiCheckBoxDatabase")multiPredicates[expression]="((SELECT 1 FROM "+Q(target.TableName)+" multi_value WHERE "+ArrayMember(array,"CAST(multi_value.`id` AS CHAR)")+(targetScope.Length==0?"":" AND "+targetScope)+" AND "+label+" LIKE {parameter} ESCAPE '!' LIMIT 1) IS NOT NULL)";
            origin=new(target.TableName,value.ColumnName);return(expression,"string");
        }
        /// <summary>坏历史JSON不能使整页SQL失败，源数据不在读取时修补。</summary>
        private static string Array(string source)=>"CASE WHEN JSON_VALID("+source+") THEN CASE WHEN JSON_TYPE("+source+")='ARRAY' THEN "+source+" ELSE '[]' END ELSE '[]' END";
        /// <summary>同时匹配旧数值／字符串ID，二进制比较避免尾空格或子串误命中；表达式只来自固定计划。</summary>
        // 验收库8.4.11中WHERE EXISTS的JSON_TABLE漏判多来源行；有界标量查询保持逐行相关执行。
        private static string ArrayMember(string array, string parameter) => JsonArrayNameQuery.ArrayMemberSql(array, parameter);
        /// <summary>按原优先级解析明确单值关系，所有目标必须在同项目内。</summary>
        private TableMetadata Target(ColumnMetadata relation)
        {
            if(!MetadataStoragePolicy.IsRelationColumn(relation)||relation.ColumnName.EndsWith("_ids",StringComparison.Ordinal)||relation.ControlKind is "CascadeTree" or "MultiSelectPage" or "MultiCheckBoxDatabase")throw new NotSupportedException("当前路径仅支持明确单值关系。");
            var definition=Definition(relation);TableMetadata? target=null;
            var id=Value(definition,"tableId");if(id.Length>0)target=tables.GetValueOrDefault(id);
            var pageId=Value(definition,"pageId");if(target is null&&pageId.Length>0)target=PageTarget(pageId);
            if(target is null&&relation.LinkConfigTableId.Length>0)target=tables.GetValueOrDefault(relation.LinkConfigTableId);
            if(target is null&&Text(relation,"linkConfigPageId").Length>0)target=PageTarget(Text(relation,"linkConfigPageId"));
            if(target is null){var name=Value(definition,"table");if(name.Length==0)name=Text(relation,"joinTable");if(name.Length==0&&relation.ColumnName.EndsWith("_id",StringComparison.Ordinal))name=relation.ColumnName[..^3];target=tables.Values.SingleOrDefault(table=>table.TableName==name);}
            if(target is null)throw new InvalidOperationException("关联目标缺失或不是同项目实体表。");
            if((refreshFields is not null||multiLabel)&&(id.Length>0&&tables.GetValueOrDefault(id)?.Id!=target.Id||pageId.Length>0&&PageTarget(pageId)?.Id!=target.Id||relation.LinkConfigTableId.Length>0&&relation.LinkConfigTableId!=target.Id||Value(definition,"table") is {Length:>0} named&&named!=target.TableName||Text(relation,"joinTable") is {Length:>0} joined&&joined!=target.TableName||Text(relation,"linkConfigPageId") is {Length:>0} linked&&PageTarget(linked)?.Id!=target.Id))throw new InvalidOperationException("固定关系目标的有效声明相互矛盾。");
            ValidateTarget(target);return target;
        }
        /// <summary>固定审计目标与业务关系使用相同实体主键约束，不能因固定表名绕过唯一性前提。</summary>
        private void ValidateTarget(TableMetadata target)
        {
            if(target.IsFunctionTable!=0||target.IsView!=0)throw new InvalidOperationException("关联目标必须为同项目实体表。");
            var key=Column(target,"id");if(key.LogicalType!="int64"||key.IsDbNotNull!=1||!storage[target.Id].IsPhysical(key))throw new InvalidOperationException("关联目标必须使用非空int64物理主键。");
        }
        /// <summary>同一路径复用别名，不同角色到同表和自关联分别命名。</summary>
        private string Join(string ownerAlias,ColumnMetadata relation,TableMetadata target,string source,string sourceType,bool validatedCrossGlobal=false,bool ordinaryRelation=false,PageMetadata? cachePage=null,AccountScopedCrossCache? account=null,bool scopedCacheLink=false)
        {
            // 生成的单值控件将物理字符串外键保存为规范整数文本。读联接按同一表示匹配，避免相邻 bigint 被折叠。
            var fixedTextKey = sourceType=="string" && ordinaryRelation &&
                relation.ControlKind is ("List" or "SelectPage") && relation.ColumnName.EndsWith("_id",StringComparison.Ordinal) &&
                storage[relation.ConfigTableId].IsPhysical(relation) && source==ownerAlias+"."+Q(relation.ColumnName);
            if(matching&&sourceType!="int64"&&!(multiLabel&&fixedTextKey))
                throw new NotSupportedException("精确管理关联键必须与目标主键同为int64，不能借数据库隐式转换匹配。");
            var global=GlobalCrossTableTarget.Declared(target);
            if(!global&&ordinaryRelation&&refreshFields is not null&&ProjectScopedSingleRelationTarget.Resolve(
                catalog,tables[relation.ConfigTableId],relation,target) is not null)
                throw new InvalidOperationException("项目内固定单值名称必须使用已发布维护绑定。");
            var projectSingle=!global&&ordinaryRelation?ProjectSingleBinding(ownerAlias,relation,target):null;
            PageMetadata? publishedPage=cachePage;
            var scopedSingle=false;
            var scopedCrossTable=false;
            var scopedAccount=false;
            MultiTargetParentScope? accountParent=null;
            MultiSourceParent? parentBinding=null;
            var projectParent=!global&&refreshFields is not null&&ownerAlias==rootAlias&&
                GlobalCachedRelationSource.RowProjectSelfTreeParentPages(catalog,root,relation).Count>0;
            if(projectParent)publishedPage=GlobalSingleRelationTarget.RequireProjectParent(catalog,root,relation,target).Page;
            if(global&&!validatedCrossGlobal)
            {
                var owner=tables[relation.ConfigTableId];
                var actualPage=GlobalSingleSourcePage(ownerAlias,owner,relation);
                var proof=owner.IsRongyu==1?GlobalSingleRelationTarget.RequireCached(catalog,owner,relation,target):
                    actualPage is not null?GlobalSingleRelationTarget.Require(catalog,owner,relation,target,actualPage):
                    GlobalSingleRelationTarget.Require(catalog,owner,relation,target);
                scopedSingle=proof.ScopedLogicalPage;
                scopedCrossTable=proof.ScopedCrossTable;
                scopedAccount=proof.ScopedAccount;
                accountParent=proof.Parent;
                parentBinding=proof.SourceParent;
                // 账号条件是已发布目标的实际范围，目标有无项目列都须绑定发布身份。
                if(scopedAccount)scopedGlobalCrossTarget=true;
                if(proof.UsesListPageScope||proof.RequiresScopedPage||HasGlobalRowProject(target)||owner.IsRongyu==1&&GlobalCrossTableTarget.Declared(owner)&&
                    columns[owner.Id].ContainsKey("yz_project_id"))publishedPage=proof.Page;
                // 冗余字典不走非冗余子计划，物理常量直接留在这段联接的可见性条件中。
                if(AllowsStoredExactCheckbox&&actualPage is not null&&target.IsRongyu==1&&!string.IsNullOrWhiteSpace(proof.Page.WhereSql))
                {
                    publishedPage=proof.Page;
                    cascadeLabelPages.Add(proof.Page.Id);
                }
            }
            // 混合文本或浮点键可能发生近似比较，同一源值可能匹配多个大整数主键。
            aggregateJoinsPreserveCardinality &= sourceType is "int64" or "int32";
            var path=ownerAlias+":"+relation.Id+(cachePage is null?"":":"+cachePage.Id)+(account is null?"":":account:"+account.PageId)+
                (scopedCacheLink?":scoped-cache:"+output:"");
            var directSave=refreshFields is not null&&ordinaryRelation&&ownerAlias==rootAlias&&storage[root.Id].IsPhysical(relation)&&source==ownerAlias+"."+Q(relation.ColumnName);
            if(aliases.TryGetValue(path,out var existing))
            {
                if(directSave)directSaveAliases.Add(existing);
                Scope(target,existing,projectScoped:!global);
                if(publishedPage is not null&&!scopedSingle&&accountParent is null)_=PublishedCondition(target,existing,publishedPage,allowAccount:scopedAccount,projectParent:projectParent);
                if(account is not null)_=AccountCacheScope(account,target,existing,ownerAlias,render:false);
                aggregateFields[existing].Add(output);
                Touch(existing,"id");return existing;
            }
            var ownerTable=tables[relation.ConfigTableId];
            // 来源路径须先加入联接；后续目标别名的 ON 才能引用已经声明的祖先。
            if(accountParent is {Global:true}&&parentBinding is {AdditionalColumns.Count:>0})
                RequireCompoundGlobalSourceParent(catalog,target.Id,publishedPage!.Id,parentBinding);
            var sourceParent=parentBinding is not null&&(accountParent?.Logical==true||
                parentBinding.LogicalPages.Count>0||parentBinding.AdditionalColumns.Count>0)
                ?SingleSourceParentValue(ownerTable,ownerAlias,parentBinding):null;
            var alias=aliasPrefix+aliases.Count;aliases.Add(path,alias);if(global){globalSingleAliases.Add(alias);if(target.IsRongyu==0)globalDerivedAliases.Add(alias);}var prefix=" FROM "+Q(root.TableName)+" "+rootAlias+string.Concat(joins);var scope=publishedPage is null?Scope(target,alias,projectScoped:!global):PublishedScope(target,alias,publishedPage,alias,allowAccount:scopedAccount,projectParent:projectParent,scopedSingle:scopedSingle,scopedCrossTable:scopedCrossTable,accountParent:accountParent,sourceParent:accountParent?.Logical==true?sourceParent:null);
            if(accountParent is {Logical:false})
                scope+=(scope.Length==0?"":" AND ")+SinglePhysicalParentCondition(ownerTable,ownerAlias,alias,accountParent,parentBinding!.Column,sourceParent:sourceParent);
            if(projectSingle is not null)
            {
                var condition=ProjectSingleReadCondition(ownerTable,ownerAlias,relation,target,alias,projectSingle);
                if(condition.Length>0)scope+=(scope.Length==0?"":" AND ")+condition;
            }
            if(trackReverse)
            {
                Touch(alias,"id");reverse.Add(alias,(target.TableName,prefix,source,sourceType));
                if(directSave)directSaveAliases.Add(alias);
            }
            if(refreshFields is not null&&sourceType is not("int64" or "string"))throw new NotSupportedException("维护关系ID必须为int64或规范字符串。");
            var equality=(refreshFields is not null||global||fixedTextKey)&&sourceType=="string"?"BINARY CAST("+alias+".`id` AS CHAR)=BINARY "+source:alias+"."+Q("id")+"="+source;
            if(refreshFields is not null&&sourceType=="string")
            {
                // 先以来源的规范整数形式查找目标主键，再用原字节比较排除隐式等值。
                // 使用定点数覆盖有符号 64 位边界，超界字符串不会溢出或转换成另一目标。
                var lookup=$"CASE WHEN REGEXP_LIKE(CONCAT({source},'#'),'^(0|-?[1-9][0-9]{{0,18}})#$','c') THEN CAST({source} AS DECIMAL(20,0)) END";
                equality=$"{alias}.`id`={lookup} AND {equality}";
            }
            if(account is not null)scope+=(scope.Length==0?"":" AND ")+AccountCacheScope(account,target,alias,ownerAlias,render:true);
            var predicate=equality+(scope.Length==0?"":" AND "+scope);
            var sql=" LEFT JOIN "+Q(target.TableName)+" "+alias+" ON "+predicate;
            joins.Add(sql);
            aggregateJoins.Add((alias, sql));
            aggregateFields.Add(alias, new(StringComparer.OrdinalIgnoreCase) { output });
            if(refreshFields is not null)
            {
                maintenanceJoins.Add((alias,sql));
                if(directSave&&global&&target.Id!=root.Id)
                    scalarCacheTarget=new(target.TableName,alias,predicate,relation.ColumnName);
            }
            return alias;
        }
        private bool HasGlobalRowProject(TableMetadata table)=>table.IsRongyu==0&&GlobalCrossTableTarget.Declared(table)&&columns[table.Id].ContainsKey("yz_project_id");

        /// <summary>全局候选和联接用同一固定页面条件，逻辑删除仍由物理字段约束。</summary>
        private string PublishedScope(TableMetadata table,string alias,PageMetadata page,string parameterKey,bool allowAccount=false,bool projectParent=false,bool scopedSingle=false,bool scopedCrossTable=false,MultiTargetParentScope? accountParent=null,string? sourceParent=null)
        {
            var scope=Scope(table,alias,projectScoped:projectParent);
            if(scopedSingle||accountParent is not null)
            {
                // 原单值联接和候选一样携带全部同父字段，隐藏名称也不能绕过附加父范围。
                var logical=scopedCrossTable?PublishedSingleCrossCondition(table,alias,page,sourceParent):
                    PublishedSingleCondition(table,alias,page,parentField:accountParent?.Target,additionalScopes:accountParent?.AdditionalTargets);
                return scope.Length==0?logical:scope+" AND "+logical;
            }
            var condition=PublishedCondition(table,alias,page,allowAccount,projectParent);
            if(condition is null)return scope;
            if(accountPhysicalCandidate&&table.Id==root.Id&&alias==rootAlias&&cascadeCacheOwner is {} owner&&condition.Variables.Count>0)
            {
                var proof=AccountScopedCrossCache.Compile(catalog,owner.Table,table,page,owner.GlobalTreeSource,globalSingleSource:owner.GlobalSingleSource);
                cascadeCacheOwnerFields.UnionWith(proof.Ownership.Select(binding=>binding.Source.ColumnName));
                var owned=AccountCacheScope(proof,table,alias,owner.Alias,render:true,owner.Table,"mt_global_scope_"+parameterKey);
                return scope.Length==0?owned:scope+" AND "+owned;
            }
            var provider=new MySqlDatabaseProvider();
            var clause=allowAccount
                ?condition.RenderAccountProjection(provider,"mt_global_scope_"+parameterKey,field=>alias+"."+Q(field))
                :condition.RenderConstants(provider,"mt_global_scope_"+parameterKey,field=>alias+"."+Q(field));
            foreach(var pair in clause.Parameters)crossConstants.Add(pair.Key,pair.Value);
            return scope.Length==0?clause.Sql:scope+" AND "+clause.Sql;
        }

        /// <summary>候选固定条件只能读取真实非敏感列，并登记为改名传播和维护的依赖。</summary>
        private FixedConditionSqlPlan? PublishedCondition(TableMetadata table,string alias,PageMetadata page,bool allowAccount=false,bool projectParent=false)
        {
            var condition=MySqlPublishedRelationCondition.Compile(catalog,table,page,allowAccount,projectParent);
            if(condition is null)return null;
            foreach(var field in condition.Fields){Use(table,Column(table,field));Touch(alias,field);}
            return condition.Plan;
        }

        /// <summary>范围字段必须是真实物理列；普通读取固定绑定项目，仅已证明的全局目标别名可省略项目谓词。</summary>
        private string Scope(TableMetadata table,string alias,bool projectScoped=true,bool includeDeleted=false)
        {
            if(projectScoped&&(matching||refreshFields is not null)&&!columns[table.Id].ContainsKey("yz_project_id"))throw new NotSupportedException("精确管理关联必须声明真实项目列。");
            var predicates=new List<string>();foreach(var name in new[]{"delete_no","yz_project_id"})
            {
                if(!columns[table.Id].TryGetValue(name,out var column))continue;
                if(!storage[table.Id].IsPhysical(column))throw new InvalidOperationException("范围字段必须为物理列。");
                if(name=="delete_no"&&column.LogicalType!="datetime"||name=="yz_project_id"&&column.LogicalType is not("int64" or "string"))throw new InvalidOperationException("范围字段类型不匹配。");
                if(name=="yz_project_id"&&(!long.TryParse(catalog.YzProjectId,out var project)||project<=0))throw new InvalidOperationException("项目标识无效。");
                if(name=="yz_project_id"&&!projectScoped||name=="delete_no"&&includeDeleted)continue;
                Use(table,column);Touch(alias,column.ColumnName);
                predicates.Add(alias+"."+Q(name)+(name=="delete_no"?" IS NULL":column.LogicalType=="int64"?"=@project_scope_number":"=@project_scope_text"));
            }
            return string.Join(" AND ",predicates);
        }
        /// <summary>选择页只提供目标归属，页面授权由既有HTTP入口建立。</summary>
        private TableMetadata? PageTarget(string pageId)=>catalog.ConfigPages.FirstOrDefault(page=>page.Id==pageId) is {} page?tables.GetValueOrDefault(page.ConfigTableId):null;
        /// <summary>读取固定元数据字段，不猜测缺失物理字段。</summary>
        private ColumnMetadata Column(TableMetadata table,string name)
        {
            var column=columns[table.Id].GetValueOrDefault(name)??throw new InvalidOperationException("缺少字段："+name);if(storage[table.Id].IsPhysical(column))Use(table,column);return column;
        }
        /// <summary>仅维护根投影把指定的物理副本当作派生值，不改变目标R1缓存的正常读取语义。</summary>
        private MetadataFieldStorage Kind(TableMetadata table,string alias,ColumnMetadata column)=>alias==rootAlias&&table.Id==root.Id&&refreshFields?.Contains(column.ColumnName)==true?MetadataFieldStorage.Join:storage[table.Id].Kind(column);
        /// <summary>每个输出分别登记目标字段依赖，复用别名也不能丢失第二个缓存的传播条件。</summary>
        private void Touch(string alias,string field)
        {
            if(!trackReverse)return;if(!aliasFields.TryGetValue(alias,out var fields))aliasFields[alias]=fields=new(StringComparer.Ordinal);if(!fields.TryGetValue(output,out var names))fields[output]=names=new(StringComparer.Ordinal);names.Add(field);
        }
        /// <summary>来源依赖独立记录；只有回填用途编译物理结构，普通读取不增加执行期结构预检。</summary>
        private void Use(TableMetadata table,ColumnMetadata column){if(table.Id==root.Id)sourceDependencies.Add(column.ColumnName);if(trackReverse)references.TryAdd((table.TableName,column.ColumnName),new(table.TableName,Metarill.Data.Schema.MySqlSchemaPlanner.CompileColumn(column)));}
    }
    /// <summary>控件定义仅作结构解析；旧式或重复JSON键必须明确拒绝。</summary>
    private static JsonElement Definition(ColumnMetadata column)
    {
        if(column.ControlKind=="MultiCheckBoxDatabase")return DatabaseCheckboxDefinition.Parse(column.ControlDetail).ReadDefinition(column);
        if(string.IsNullOrWhiteSpace(column.ControlDetail))return JsonSerializer.SerializeToElement(new{});
        using var document=JsonDocument.Parse(column.ControlDetail);var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||root.EnumerateObject().Select(property=>property.Name).Distinct(StringComparer.Ordinal).Count()!=root.EnumerateObject().Count())throw new InvalidOperationException("控件定义必须是无重复键JSON对象。");
        return root.Clone();
    }
    /// <summary>只读字符串结构键，不执行原生SQL配置。</summary>
    private static string Value(JsonElement value,string key)=>value.TryGetProperty(key,out var item)&&item.ValueKind==JsonValueKind.String?item.GetString()!:"";
    /// <summary>兼容扩展字段保留原文本值。</summary>
    private static string Text(ColumnMetadata column,string key)=>column.Extra.TryGetValue(key,out var value)?value.ToString():"";
    /// <summary>MySQL单段标识符严格白名单，方言集中在当前适配文件。</summary>
    private static string Q(string name){MetadataValidation.RequireIdentifier(name);return "`"+name+"`";}
}
