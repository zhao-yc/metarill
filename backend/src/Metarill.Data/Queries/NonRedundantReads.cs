using System.Globalization;
using Metarill.Core.Metadata;
using Metarill.Core.Security;

namespace Metarill.Data.Queries;

/// <summary>生成Service使用的数据库中立读计划句柄；具体SQL仍只在数据适配层构建。</summary>
public sealed class NonRedundantReadPlan
{
    internal NonRedundantReadPlan(MySqlNonRedundantReadPlan mysql){MySql=mysql;}
    internal MySqlNonRedundantReadPlan MySql{get;}
    /// <summary>按调用方已核验的身份绑定目标页账号条件，共享的发布计划保持不可变。</summary>
    public NonRedundantReadPlan BindAccount(Metarill.Core.Security.RuntimeActor actor)
    {
        var bound=MySql.BindAccount(actor);
        return ReferenceEquals(bound,MySql)?this:new(bound);
    }
    /// <summary>指示读取时是否须绑定服务端账号字段。</summary>
    public bool RequiresAccount=>MySql.RequiresAccount;
    public IReadOnlyDictionary<string,string> OutputTypes=>new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(MySql.Outputs.ToDictionary(name=>name,name=>MySql.Types[name],StringComparer.OrdinalIgnoreCase));
}

/// <summary>业务页发布时固定的单值目标页和可选来源父键，不接受请求指定。</summary>
public sealed record NonRedundantSingleTargetScope(string ColumnId,string PageId,string? SourceParentId=null)
{
    /// <summary>中间页没有额外条件时仍携带后续联接的实际目标页。</summary>
    public bool ApplyPageCondition { get; init; } = true;

    /// <summary>固定父键由实际来源页证明，同表其它页面可以使用不同键。</summary>
    public string? SourcePageId { get; init; }

    /// <summary>下拉附加条件来自生成时固定的实际来源页，表单值从该来源记录求值。</summary>
    public string? ControlSourcePageId { get; init; }

    /// <summary>子绑定只用于当前目标别名，重复联接同一张表时不能借用其他角色的范围。</summary>
    public IReadOnlyList<NonRedundantSingleTargetScope> NestedTargets { get; init; } = [];
}

/// <summary>同一物理多值关系按当前页绑定名称路径及已证明的目标页范围。</summary>
public sealed record NonRedundantMultiTargetScope(string ColumnId,string SourcePageId,string? TargetPageId,bool ApplyPageScope,bool ApplyControlScope=false);

/// <summary>在生成校验或宿主启动阶段编译，业务方法不判断数据库品牌。</summary>
public static class NonRedundantReads
{
    /// <summary>声明可配置非冗余表的方言，具体字段和关系组合仍由生成编译校验。</summary>
    public static bool SupportsProvider(string? provider) =>
        string.Equals(provider, "mysql", StringComparison.OrdinalIgnoreCase);

    /// <summary>输出与隐藏查询依赖分别声明；后者不会自动出现在返回记录中。</summary>
    public static NonRedundantReadPlan Compile(MetadataCatalog metadata,string tableId,IReadOnlyList<string> outputs,IReadOnlyList<string>? dependencies=null,bool deferCascadeNames=false)=>CompileWithCrossTables(metadata,tableId,outputs,[],dependencies,deferCascadeNames);
    /// <summary>列表、详情、筛选与导出按业务页绑定的目标范围读取单值名称。</summary>
    public static NonRedundantReadPlan CompileProjectPage(MetadataCatalog metadata,string tableId,IReadOnlyList<string> outputs,
        IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantCrossTableScope> crossTables,
        IReadOnlyList<NonRedundantSingleTargetScope> singleTargets)=>
        new(MySqlNonRedundantReadCompiler.CompileProjectPage(metadata,tableId,outputs,dependencies,crossTables,singleTargets));

    /// <summary>同一业务页的列表、详情和导出使用实际多值候选的发布配置。</summary>
    public static NonRedundantReadPlan CompileProjectPage(MetadataCatalog metadata,string tableId,IReadOnlyList<string> outputs,
        IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantCrossTableScope> crossTables,
        IReadOnlyList<NonRedundantSingleTargetScope> singleTargets,IReadOnlyList<NonRedundantMultiTargetScope>? multiTargets)=>
        new(MySqlNonRedundantReadCompiler.CompileProjectPage(metadata,tableId,outputs,dependencies,crossTables,singleTargets,multiTargets));

    /// <summary>派生候选通过实际中间页的单值范围读取名称。</summary>
    public static NonRedundantReadPlan CompileProjectRelation(MetadataCatalog metadata,string tableId,IReadOnlyList<string> outputs,
        IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantSingleTargetScope> singleTargets)=>
        new(MySqlNonRedundantReadCompiler.CompileProjectRelation(metadata,tableId,outputs,dependencies,singleTargets));

    /// <summary>只按已发布的全局目标页读取候选；普通对象读取继续使用按行归属的计划。</summary>
    public static NonRedundantReadPlan CompileGlobalCandidate(MetadataCatalog metadata,string tableId,string pageId,IReadOnlyList<string> outputs,IReadOnlyList<string>? dependencies=null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return metadata.DatabaseProvider switch
        {
            "mysql"=>new(MySqlNonRedundantReadCompiler.CompileGlobalCandidate(metadata,tableId,pageId,outputs,dependencies)),
            _=>throw new NotSupportedException("该数据库尚未完成固定全局候选读取适配。")
        };
    }
    /// <summary>候选附带已发布的后续单值关系，页面和输出范围由生成器指定。</summary>
    public static NonRedundantReadPlan CompileGlobalCandidate(MetadataCatalog metadata,string tableId,string pageId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets)=>
        new(MySqlNonRedundantReadCompiler.CompileGlobalCandidate(metadata,tableId,pageId,outputs,dependencies,
            deferCascadeNames:false,singleTargets:singleTargets));

    /// <summary>已发布全局页面投影的树路径由生成服务补齐，固定查询依赖仍在SQL中执行。</summary>
    public static NonRedundantReadPlan CompileGlobalPage(MetadataCatalog metadata,string tableId,string pageId,IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies)=>
        CompileGlobalPage(metadata,tableId,pageId,outputs,dependencies,null);

    /// <summary>直接选择页和关联候选使用同一单值关系范围。</summary>
    public static NonRedundantReadPlan CompileGlobalPage(MetadataCatalog metadata,string tableId,string pageId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets)=>
        new(MySqlNonRedundantReadCompiler.CompileGlobalCandidate(metadata,tableId,pageId,outputs,dependencies,
            deferCascadeNames:true,singleTargets:singleTargets));

    /// <summary>生成的项目内 R1 单值候选使用物理投影和项目范围，保留独立的数据库适配入口。</summary>
    public static NonRedundantReadPlan CompilePhysicalRelation(MetadataCatalog metadata, string tableId, IReadOnlyList<string> outputs,
        IReadOnlyList<string>? dependencies = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return metadata.DatabaseProvider switch
        {
            "mysql" => new(MySqlNonRedundantReadCompiler.CompilePhysicalRelation(metadata, tableId, outputs, dependencies)),
            _ => throw new NotSupportedException("该数据库尚未完成项目内物理候选读取适配。")
        };
    }

    /// <summary>物理候选的固定范围可以引用生成时已证明的冗余父键。</summary>
    public static NonRedundantReadPlan CompilePhysicalRelation(MetadataCatalog metadata,string tableId,IReadOnlyList<string> outputs,
        IReadOnlyList<string>? dependencies,IReadOnlyList<RedundantSharedParentPlan> parents)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(parents);
        return metadata.DatabaseProvider switch
        {
            "mysql"=>new(MySqlNonRedundantReadCompiler.CompilePhysicalRelation(metadata,tableId,outputs,dependencies,parents)),
            _=>throw new NotSupportedException("该数据库尚未完成项目内物理候选读取适配。")
        };
    }

    /// <summary>判断物理外键槽是否属于已声明共享逻辑字段；名称和祖先ID使用同一组存储证明。</summary>
    public static SharedCrossTableSparseSelection? SharedExactGroup(MetadataCatalog metadata,TableMetadata table,string field)=>
        SharedCrossTableSparseSelection.CompileAll(table,metadata.ConfigColumns.Where(column=>column.ConfigTableId==table.Id))
            .SingleOrDefault(group=>group.Slots.Any(slot=>slot.ColumnName==field||slot.ColumnName[..^3]+"_name"==field));

    /// <summary>识别已声明独立链的逻辑精确字段，避免用原始空祖先或不可见末端授权。</summary>
    public static CrossTableSparseSelection? IndependentCrossExact(MetadataCatalog metadata,TableMetadata table,string field)
    {
        if(table.IsRongyu!=0||GlobalCrossTableTarget.Declared(table))return null;
        var columns=metadata.ConfigColumns.Where(column=>column.ConfigTableId==table.Id).ToArray();
        var members=SharedCrossTableSparseSelection.CompileAll(table,columns).SelectMany(group=>group.Branches)
            .Select(branch=>branch.SourceColumnId).ToHashSet(StringComparer.Ordinal);
        return columns.Where(column=>column.ControlKind=="CascadeTree"&&!members.Contains(column.Id)&&
                CrossTableStorageColumns.ContainsPathField(column,field))
            .Select(column=>CrossTableSparseSelection.Compile(table,column.Id,columns)).SingleOrDefault();
    }

    /// <summary>只编译独立级联范围列及其固定候选路径，启动前核验生成与文件读使用同一逻辑值。</summary>
    public static NonRedundantReadPlan CompileCrossExactMatch(MetadataCatalog metadata,string tableId,string field,IReadOnlyList<NonRedundantCrossTableScope> bindings)
    {
        var plan=CompileWithCrossTables(metadata,tableId,[field],bindings);
        _=ExactDataManageSql.CrossExpression(plan.MySql,field);
        return plan;
    }

    /// <summary>只编译当前权限列及共享同伴需要的固定路径，避免把无关显示字段加入写前锁定。</summary>
    public static NonRedundantReadPlan CompileSharedExactMatch(MetadataCatalog metadata,string tableId,string field,IReadOnlyList<NonRedundantCrossTableScope> bindings)
    {
        var plan=CompileWithCrossTables(metadata,tableId,[field],bindings);
        _=plan.MySql.SharedExactExpression(field);
        return plan;
    }

    /// <summary>规则记录专用：物理根实体输出与逻辑筛选/派生值分开，仍使用同一固定SQL及范围。</summary>
    public static NonRedundantReadPlan CompileRuleRecord(MetadataCatalog metadata,string tableId,IReadOnlyList<string> outputs,IReadOnlyList<string> storageOutputs,IReadOnlyList<NonRedundantCrossTableScope> crossTables,IReadOnlyList<string>? dependencies=null,bool deferCascadeNames=false,IReadOnlyList<string>? logicalOutputs=null) =>
        CompileRuleRecord(metadata,tableId,outputs,storageOutputs,crossTables,dependencies,deferCascadeNames,logicalOutputs,null);

    /// <summary>规则使用业务页固定单值关系，存储输出和逻辑值仍由适配器分别生成。</summary>
    public static NonRedundantReadPlan CompileRuleRecord(MetadataCatalog metadata,string tableId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> storageOutputs,IReadOnlyList<NonRedundantCrossTableScope> crossTables,
        IReadOnlyList<string>? dependencies,bool deferCascadeNames,IReadOnlyList<string>? logicalOutputs,
        IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return metadata.DatabaseProvider switch
        {
            "mysql"=>new(MySqlNonRedundantReadCompiler.CompileRuleRecord(metadata,tableId,outputs,storageOutputs,
                crossTables,dependencies,deferCascadeNames,logicalOutputs,singleTargets)),
            _=>throw new NotSupportedException("该数据库尚未完成规则记录读取适配。")
        };
    }
    /// <summary>跨表逻辑读取须显式传入冻结目标页范围；保留原Compile签名及无绑定门禁。</summary>
    public static NonRedundantReadPlan CompileWithCrossTables(MetadataCatalog metadata,string tableId,IReadOnlyList<string> outputs,IReadOnlyList<NonRedundantCrossTableScope> crossTables,IReadOnlyList<string>? dependencies=null,bool deferCascadeNames=false)
    {
        ArgumentNullException.ThrowIfNull(metadata);ArgumentNullException.ThrowIfNull(crossTables);
        return metadata.DatabaseProvider switch{"mysql"=>new(MySqlNonRedundantReadCompiler.CompileWithCrossTables(metadata,tableId,outputs,crossTables,dependencies,deferCascadeNames)),_=>throw new NotSupportedException("该数据库尚未完成固定非冗余读取适配。")};
    }
    /// <summary>附件等固定入口只证明记录是否满足条件，不将路径或其他业务字段对外投影。</summary>
    public static NonRedundantReadPlan CompileCrossPageMembership(MetadataCatalog metadata,TableMetadata table,PageMetadata page,
        IReadOnlyList<string> conditionFields,IReadOnlySet<string> crossFields)=>
        CompilePageMembership(metadata,table,page,conditionFields,crossFields,new HashSet<string>(StringComparer.Ordinal));

    /// <summary>发布附件记录范围时复用已证明的跨表路径和自树叶名。</summary>
    public static NonRedundantReadPlan CompilePageMembership(MetadataCatalog metadata,TableMetadata table,PageMetadata page,
        IReadOnlyList<string> conditionFields,IReadOnlySet<string> crossFields,IReadOnlySet<string> selfTreeFields)=>
        CompilePageMembership(metadata,table,page,conditionFields,crossFields,selfTreeFields,["id"],[]);

    /// <summary>关系候选可在同一逻辑页面范围中读取固定的名称和排序字段。</summary>
    public static NonRedundantReadPlan CompilePageMembership(MetadataCatalog metadata,TableMetadata table,PageMetadata page,
        IReadOnlyList<string> conditionFields,IReadOnlySet<string> crossFields,IReadOnlySet<string> selfTreeFields,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies)=>
        CompilePageMembership(metadata,table,page,conditionFields,crossFields,selfTreeFields,outputs,dependencies,null);

    /// <summary>逻辑候选的行范围和派生名称分别使用同一发布目录中的已固定关系。</summary>
    public static NonRedundantReadPlan CompilePageMembership(MetadataCatalog metadata,TableMetadata table,PageMetadata page,
        IReadOnlyList<string> conditionFields,IReadOnlySet<string> crossFields,IReadOnlySet<string> selfTreeFields,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets)=>
        metadata.DatabaseProvider switch
        {
            "mysql"=>new(MySqlNonRedundantReadCompiler.CompilePageMembership(metadata,table,page,conditionFields,crossFields,selfTreeFields,outputs,dependencies,singleTargets)),
            _=>throw new NotSupportedException("该数据库尚未完成逻辑页面范围适配。")
        };

    /// <summary>候选范围由完整已发布页面固定，调用方只能提供业务身份与原候选筛选。</summary>
    public static NonRedundantReadPlan CompileCrossPageCandidate(MetadataCatalog metadata,string tableId,string pageId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies)=>
        new(MySqlNonRedundantReadCompiler.CompileCrossPageCandidate(metadata,tableId,pageId,outputs,dependencies));

    /// <summary>全局独立选择页可同时返回已证明的其它级联显示列；范围条件仍只读取实际依赖路径。</summary>
    public static NonRedundantReadPlan CompileCrossPageCandidate(MetadataCatalog metadata,string tableId,string pageId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantCrossTableScope> bindings)=>
        new(MySqlNonRedundantReadCompiler.CompileCrossPageCandidate(metadata,tableId,pageId,outputs,dependencies,bindings));

    /// <summary>逻辑父候选在实际父范围内按已固定的多段关系读取名称。</summary>
    public static NonRedundantReadPlan CompileCrossPageCandidate(MetadataCatalog metadata,string tableId,string pageId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantCrossTableScope>? bindings,
        IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets)=>
        new(MySqlNonRedundantReadCompiler.CompileCrossPageCandidate(metadata,tableId,pageId,outputs,dependencies,
            bindings,singleTargets:singleTargets));

    /// <summary>项目内单选沿发布页合并逻辑父级与精确匹配字段，授权集合由宿主提供。</summary>
    public static NonRedundantReadPlan CompileExactSingleParentCandidate(MetadataCatalog metadata,string tableId,string pageId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantCrossTableScope>? bindings,
        IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets)=>
        new(MySqlNonRedundantReadCompiler.CompileCrossPageCandidate(metadata,tableId,pageId,outputs,dependencies,
            bindings,singleTargets:singleTargets,exactSingleParent:true));

    /// <summary>功能表复选从实际发布的逻辑父级读取，精确授权集合由生成的入口指定。</summary>
    public static NonRedundantReadPlan CompileExactCheckboxParentCandidate(MetadataCatalog metadata,string tableId,string pageId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantCrossTableScope> bindings,
        IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets)=>
        new(MySqlNonRedundantReadCompiler.CompileCrossPageCandidate(metadata,tableId,pageId,outputs,dependencies,
            bindings,singleTargets:singleTargets,exactCheckboxParent:true));

    /// <summary>全局独立多选的分页、统计和已选回读共用实际账号与物理父范围。</summary>
    public static NonRedundantReadPlan CompileGlobalAccountMultiPage(MetadataCatalog metadata,string tableId,string pageId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantCrossTableScope> bindings)=>
        CompileGlobalAccountMultiPage(metadata,tableId,pageId,outputs,dependencies,bindings,null);

    /// <summary>全局账号父页的候选输出继续沿已发布单值范围读取。</summary>
    public static NonRedundantReadPlan CompileGlobalAccountMultiPage(MetadataCatalog metadata,string tableId,string pageId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantCrossTableScope> bindings,
        IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets)=>
        new(MySqlNonRedundantReadCompiler.CompileCrossPageCandidate(metadata,tableId,pageId,outputs,dependencies,
            bindings,accountParentPageType:"2",singleTargets:singleTargets));

    /// <summary>保留已生成的账号父范围入口，同页条件交给固定单选编译。</summary>
    public static NonRedundantReadPlan CompileGlobalAccountSinglePage(MetadataCatalog metadata,string tableId,string pageId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantCrossTableScope> bindings)=>
        CompileGlobalSingleParentPage(metadata,tableId,pageId,outputs,dependencies,bindings);

    /// <summary>全局独立单选同时核验发布的父范围及该页的账号或级联条件。</summary>
    public static NonRedundantReadPlan CompileGlobalSingleParentPage(MetadataCatalog metadata,string tableId,string pageId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantCrossTableScope> bindings)=>
        CompileGlobalSingleParentPage(metadata,tableId,pageId,outputs,dependencies,bindings,null);

    /// <summary>独立单选的详情及导出也采用同一派生名称范围。</summary>
    public static NonRedundantReadPlan CompileGlobalSingleParentPage(MetadataCatalog metadata,string tableId,string pageId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantCrossTableScope> bindings,
        IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets)=>
        new(MySqlNonRedundantReadCompiler.CompileCrossPageCandidate(metadata,tableId,pageId,outputs,dependencies,
            bindings,accountParentPageType:"1",singleTargets:singleTargets));

    /// <summary>全局下拉仅登记附加条件依赖，发布页范围和全局资格仍从原元数据读取。</summary>
    public static NonRedundantReadPlan CompileGlobalListCandidate(MetadataCatalog metadata,string tableId,string pageId,
        IReadOnlyList<string> fields,IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies)=>
        CompileGlobalListCandidate(metadata,tableId,pageId,fields,outputs,dependencies,null);

    /// <summary>附加逻辑筛选和多段名称的范围由同一候选计划执行。</summary>
    public static NonRedundantReadPlan CompileGlobalListCandidate(MetadataCatalog metadata,string tableId,string pageId,
        IReadOnlyList<string> fields,IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,
        IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets)=>
        new(MySqlNonRedundantReadCompiler.CompileCrossPageCandidate(metadata,tableId,pageId,outputs,dependencies,
            listFields:fields,singleTargets:singleTargets));

    /// <summary>匹配计划只编译必要单值路径，并验证最终物理值，不能借别名匹配凭据或文件数组。</summary>
    public static NonRedundantReadPlan CompileMatch(MetadataCatalog metadata,string tableId,string field)=>metadata.DatabaseProvider switch
    {"mysql"=>new(MySqlNonRedundantReadCompiler.CompileMatch(metadata,tableId,field)),_=>throw new NotSupportedException("该数据库尚未完成固定匹配读取适配。")};

    /// <summary>仅为真实发布页编译自树精确叶名，完整祖先可见性与固定页面条件沿用同一读计划。</summary>
    public static NonRedundantReadPlan CompileSelfTreeExactMatch(MetadataCatalog metadata,string tableId,string pageId)
    {
        var table=metadata.ConfigTables.Single(item=>item.Id==tableId);
        var page=metadata.ConfigPages.Single(item=>item.Id==pageId&&item.ConfigTableId==tableId);
        var field=ExactDataManageValues.Field(page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString());
        var columns=RelationStorageColumns.Expand(table,metadata.ConfigColumns.Where(item=>item.ConfigTableId==tableId));
        if(GlobalCrossTableTarget.Declared(table)||table.IsFunctionTable!=0||table.IsView!=0||
            table.YzProjectId!=metadata.YzProjectId||page.YzProjectId!=metadata.YzProjectId||
            !metadata.YzAdminTypes.Any(admin=>admin.Id==page.YzAdminTypeId&&admin.YzProjectId==metadata.YzProjectId)||
            !CascadeTreeStorageColumns.IsLogicalNameField(table,columns,field))
            throw new NotSupportedException("自树精确范围必须绑定本项目普通实体实际发布的叶名。");
        return CompilePageMembership(metadata,table,page,[field],new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal){field},[field],[field]);
    }

    /// <summary>多值显示名称复用单值路径证明，另支持原生 TIME，不扩大精确数据管理的键类型。</summary>
    public static NonRedundantReadPlan CompileMultiLabel(MetadataCatalog metadata,string tableId,string field)=>metadata.DatabaseProvider switch
    {"mysql"=>new(MySqlNonRedundantReadCompiler.CompileMultiLabel(metadata,tableId,field)),_=>throw new NotSupportedException("该数据库尚未完成固定名称读取适配。")};
    /// <summary>多值名称与候选条件、排序依赖一并冻结。</summary>
    public static NonRedundantReadPlan CompileMultiLabel(MetadataCatalog metadata,string tableId,string field,IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies)=>metadata.DatabaseProvider switch
    {"mysql"=>new(MySqlNonRedundantReadCompiler.CompileMultiLabel(metadata,tableId,field,outputs,dependencies)),_=>throw new NotSupportedException("该数据库尚未完成固定名称读取适配。")};

    /// <summary>多值候选沿实际发布页固定中间单值范围。</summary>
    public static NonRedundantReadPlan CompileMultiLabel(MetadataCatalog metadata,string tableId,string field,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets)=>
        singleTargets is null||singleTargets.Count==0?CompileMultiLabel(metadata,tableId,field,outputs,dependencies):
            new(MySqlNonRedundantReadCompiler.CompileMultiLabel(metadata,tableId,field,outputs,dependencies,singleTargets));

    /// <summary>生成和传播按表级多值定义识别固定逻辑目标页。</summary>
    /// <summary>确认该固定页面确实被多选或复选字段引用。</summary>
    public static bool IsScopedMultiTarget(MetadataCatalog metadata,TableMetadata target,string pageId)=>
        MySqlNonRedundantReadCompiler.IsScopedMultiTarget(metadata,target,pageId);

    /// <summary>只识别实际有多选引用的固定目标页，用于该页精确权限与自树的组合证明。</summary>
    public static bool IsScopedMultiSelectTarget(MetadataCatalog metadata,TableMetadata target,string pageId)=>
        MySqlNonRedundantReadCompiler.IsScopedMultiTarget(metadata,target,pageId,selectOnly:true);

    public static bool HasScopedMultiPage(MetadataCatalog metadata,TableMetadata target,ColumnMetadata relation)=>
        MySqlNonRedundantReadCompiler.MultiTargetLogicalPage(metadata,target,relation) is not null;

    /// <summary>多值控件显式绑定的目标页在读取计划内冻结逻辑条件。</summary>
    /// <summary>由已发布目标页的实际逻辑层编译绑定全局父的ID校验。</summary>
    public static NonRedundantReadPlan CompileMultiGlobalParent(MetadataCatalog metadata,string tableId,string pageId)=>
        new(MySqlNonRedundantReadCompiler.CompileMultiGlobalParent(metadata,tableId,pageId));

    /// <summary>比较实际全局父的页面身份，目标页不同时仍可复用已证明的同一父范围。</summary>
    public static string MultiGlobalParentPageId(MetadataCatalog metadata,string tableId,string pageId)=>
        MySqlNonRedundantReadCompiler.PublishedMultiGlobalParent(metadata,tableId,pageId).Page.Id;

    public static NonRedundantReadPlan CompileScopedMultiLabel(MetadataCatalog metadata,string tableId,string pageId,string field,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies)=>
        CompileScopedMultiLabel(metadata,tableId,pageId,field,outputs,dependencies,null);

    /// <summary>目标自身的逻辑范围和中间单值范围同时固定。</summary>
    public static NonRedundantReadPlan CompileScopedMultiLabel(MetadataCatalog metadata,string tableId,string pageId,string field,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets)=>
        new(MySqlNonRedundantReadCompiler.CompileScopedMultiLabel(metadata,tableId,pageId,field,outputs,dependencies,singleTargets));

    /// <summary>Excel 附加条件以宿主发布的逻辑表达式匹配，绑定值仍由真实导入上下文提供。</summary>
    public static NonRedundantReadPlan CompileMultiImportLabel(MetadataCatalog metadata,string tableId,string pageId,string field,
        IReadOnlyList<string> conditionFields,IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets=null)=>
        new(MySqlNonRedundantReadCompiler.CompileScopedMultiLabel(metadata,tableId,pageId,field,
            field=="id"?["id"]:["id",field],[],singleTargets,conditionFields));

    /// <summary>多选当前读取保留实际发布页的逻辑范围和精确匹配依赖，由服务绑定当前账号权限。</summary>
    public static NonRedundantReadPlan CompileExactMultiLabel(MetadataCatalog metadata,string tableId,string pageId,string field,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets=null,
        IReadOnlyList<string>? conditionFields=null)=>
        new(MySqlNonRedundantReadCompiler.CompileScopedMultiLabel(metadata,tableId,pageId,field,outputs,dependencies,
            singleTargets,conditionFields,exactMultiSelection:true));

    /// <summary>非冗余单选 Excel 使用实际单选页与同一固定条件编译器。</summary>
    public static NonRedundantReadPlan CompileSingleImportLabel(MetadataCatalog metadata,string tableId,string pageId,string field,
        IReadOnlyList<string> conditionFields,IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets=null)=>
        new(MySqlNonRedundantReadCompiler.CompileScopedMultiLabel(metadata,tableId,pageId,field,["id",field],[],
            singleTargets,conditionFields,importPageType:"1"));

    /// <summary>生成期取得固定单值名称实际读取的来源物理列；同表其它角色可能形成安全超集，不返回SQL。</summary>
    public static IReadOnlyList<string> CompileSourceDependencies(MetadataCatalog metadata,string tableId,string field)
    {
        var plan=CompileMatch(metadata,tableId,field);return Array.AsReadOnly(plan.MySql.SourceDependencies.ToArray());
    }
    /// <summary>生成期证明固定名称路径是否读到指定实体，不向生成层暴露方言计划或SQL。</summary>
    public static bool MatchReadsTable(MetadataCatalog metadata,string tableId,string field,string referencedTable)
    {
        MetadataValidation.RequireIdentifier(referencedTable);return CompileMatch(metadata,tableId,field).MySql.PhysicalReferences.Any(reference=>reference.Table==referencedTable);
    }
    /// <summary>只读规则取固定逻辑别名；无独立别名的派生名称继续读取原字段，不接受客户端别名。</summary>
    public static T? RuleValue<T>(NonRedundantReadPlan plan,IReadOnlyDictionary<string,object?> row,string name)
    {
        if(!plan.MySql.Outputs.Contains(name,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("逻辑规则字段不属于固定输出。");
        return Value<T>(row,plan.MySql.RuleOutputAliases.TryGetValue(name,out var alias)?alias:name);
    }
    /// <summary>将驱动标量转换为生成属性；空值和长ID保持原类型，不经JSON或浮点中转。</summary>
    public static T? Value<T>(IReadOnlyDictionary<string,object?> row,string name)
    {
        if(!row.TryGetValue(name,out var value))throw new InvalidOperationException("已编译读取未返回声明字段。");
        if(value is null)return default;
        var target=Nullable.GetUnderlyingType(typeof(T))??typeof(T);
        // double在精确类型快捷返回前校验，防止驱动非有限值或文本下溢进入原VO。
        if(target==typeof(double))
        {
            double real;
            if(value is string text)
            {
                if(!Metarill.Core.Contracts.ExactNumericValue.TryDouble(text,out real))throw new InvalidOperationException("非冗余浮点读取必须是有限值，不能溢出或下溢为零。");
            }
            else real=Convert.ToDouble(value,CultureInfo.InvariantCulture);
            if(!double.IsFinite(real))throw new InvalidOperationException("非冗余浮点读取必须是有限值。");
            return (T)(object)real;
        }
        if(value is T exact)return exact;
        if(target==typeof(TimeSpan))return (T)(object)(value is TimeSpan span?span:Metarill.Core.Contracts.SqlTimeValue.ParseText(Convert.ToString(value,CultureInfo.InvariantCulture))!.Value);
        return (T)Convert.ChangeType(value,target,CultureInfo.InvariantCulture);
    }
}

/// <summary>数据库中立的固定读取入口；页面授权和真实父记录检查仍由原生成服务建立。</summary>
public sealed class NonRedundantReader(DatabaseSession session)
{
    private readonly MySqlNonRedundantReader mysql=new(session);
    /// <summary>复用适配层完整范围和参数绑定，不扩展页面输出权限。</summary>
    public Task<FixedReadPage> PageAsync(NonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,IReadOnlyList<FixedReadOrder> order,int page,int pageSize,CancellationToken token=default,bool unpaged=false,string? projectId=null,FixedConditionSqlPlan? condition=null,bool currentRead=false)=>mysql.ReadPageAsync(plan.MySql,filters,order,page,pageSize,token,unpaged,projectId,condition,currentRead);
    /// <summary>按固定主键集合回读，保留原范围、跨表校验与当前读事务约束。</summary>
    public Task<IReadOnlyList<IReadOnlyDictionary<string,object?>>> KeyRowsAsync(NonRedundantReadPlan plan,
        IReadOnlyList<FixedReadFilter> filters, IReadOnlyList<long> keys, CancellationToken token=default,
        string? projectId=null, FixedConditionSqlPlan? condition=null, bool currentRead=false)
        => mysql.ReadKeyRowsAsync(plan.MySql, filters, keys, token, projectId, condition, currentRead);

    /// <summary>有界导出按固定顺序读取原列投影，不发出不需要的COUNT。</summary>
    public Task<IReadOnlyList<IReadOnlyDictionary<string,object?>>> RowsAsync(NonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,IReadOnlyList<FixedReadOrder> order,int limit,CancellationToken token=default,string? projectId=null,FixedConditionSqlPlan? condition=null)=>mysql.ReadRowsAsync(plan.MySql,filters,order,limit,token,projectId,condition);
    /// <summary>写校验沿真实事务锁定固定投影及联接名称，不执行COUNT；旧RowsAsync合同保持。</summary>
    public Task<IReadOnlyList<IReadOnlyDictionary<string,object?>>> CurrentRowsAsync(NonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,IReadOnlyList<FixedReadOrder> order,int limit,CancellationToken token=default,string? projectId=null,FixedConditionSqlPlan? condition=null)=>mysql.ReadCurrentRowsAsync(plan.MySql,filters,order,limit,token,projectId,condition);
    /// <summary>覆盖匹配在当前事务最多返回两条，先锁定将要更新的来源行。</summary>
    public Task<IReadOnlyList<IReadOnlyDictionary<string,object?>>> ImportMatchesAsync(NonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,CancellationToken token=default,string? projectId=null,FixedConditionSqlPlan? condition=null)=>mysql.ReadImportMatchesAsync(plan.MySql,filters,token,projectId,condition);
    /// <summary>保持原跨表候选完整数组，方言层执行无COUNT的固定排序查询。</summary>
    public Task<IReadOnlyList<IReadOnlyDictionary<string,object?>>> AllRowsAsync(NonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,IReadOnlyList<FixedReadOrder> order,CancellationToken token=default,string? projectId=null,FixedConditionSqlPlan? condition=null)=>mysql.ReadAllRowsAsync(plan.MySql,filters,order,token,projectId,condition);
    /// <summary>统计不会继承分页或排序。</summary>
    public Task<FixedReadStatistics> StatisticsAsync(NonRedundantReadPlan plan,IReadOnlyList<FixedReadFilter> filters,IReadOnlyList<string> sums,CancellationToken token=default,string? projectId=null,FixedConditionSqlPlan? condition=null)=>mysql.ReadStatisticsAsync(plan.MySql,filters,sums,token,projectId,condition);
}
