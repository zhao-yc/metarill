using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
namespace Metarill.Core.Metadata;

/// <summary>显式全局跨表目标的共享发布证明；全局只改变目标行范围，不改变来源项目或页面授权。</summary>
public static class GlobalCrossTableTarget
{
    /// <summary>只识别原scopeKind精确声明，缺项目字段不能自动推断为全局。</summary>
    public static bool Declared(TableMetadata table)=>table.Extra.TryGetValue("scopeKind",out var value)&&value.ValueKind==JsonValueKind.String&&value.GetString()=="global";
    /// <summary>全局物理实体必须明确发布；请求项目／父变量不能在全局范围中被静默省略。</summary>
    public static void Require(MetadataCatalog catalog,TableMetadata table,PageMetadata page)=>RequireTarget(catalog,table,page,false);
    /// <summary>单值及其候选根可另证R0实体；级联和文件父范围继续使用原R1入口。</summary>
    public static void RequireSingle(MetadataCatalog catalog,TableMetadata table,PageMetadata page)=>RequireTarget(catalog,table,page,true);
    /// <summary>全局单值范围来自实际发布的业务页或单选页；记录权限由调用入口校验。</summary>
    public static bool DeclaresExactSingle(TableMetadata table,PageMetadata page)=>
        Declared(table)&&page.Extra.GetValueOrDefault("type").ToString() is "" or "0" or "1"&&
        HasExactMatch(page);

    /// <summary>多选页的发布资格单独识别，单值关系不能借用多选目标页。</summary>
    public static bool DeclaresExactMulti(TableMetadata table,PageMetadata page)=>
        Declared(table)&&page.Extra.GetValueOrDefault("type").ToString()=="2"&&HasExactMatch(page);

    /// <summary>页面自身的读取和写前匹配共用两类精确范围的发布证明。</summary>
    public static bool DeclaresExactPage(TableMetadata table,PageMetadata page)=>
        DeclaresExactSingle(table,page)||DeclaresExactMulti(table,page);

    private static bool HasExactMatch(PageMetadata page)=>
        !string.IsNullOrWhiteSpace(page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString());

    /// <summary>证明全局单值匹配字段及发布身份，下发和实际读计划均使用此校验。</summary>
    public static void RequireExactSingleCandidate(MetadataCatalog catalog,TableMetadata table,PageMetadata page)=>
        _=ExactSingleParent(catalog,table,page);

    /// <summary>同一发布证明返回实际父层位，候选不能用任意父字段替代。</summary>
    public static MultiTargetParentScope? ExactSingleParent(MetadataCatalog catalog,TableMetadata table,PageMetadata page)=>
        ExactSingleParent(catalog,table,page,null);

    /// <summary>联接匹配沿调用方证明栈校验终点，防止经另一个精确单选页重置循环检测。</summary>
    internal static MultiTargetParentScope? ExactSingleParent(MetadataCatalog catalog,TableMetadata table,PageMetadata page,
        ISet<string>? resolving)
    {
        if(!DeclaresExactSingle(table,page))
            throw new NotSupportedException("全局单值范围必须使用发布的业务页或单选页。");
        return ExactPageParent(catalog,table,page,resolving);
    }

    /// <summary>直接读写、选档和缓存使用同一页的标量路径及真实父级证明。</summary>
    public static MultiTargetParentScope? ExactPageParent(MetadataCatalog catalog,TableMetadata table,PageMetadata page)=>
        ExactPageParent(catalog,table,page,null);

    private static MultiTargetParentScope? ExactPageParent(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ISet<string>? resolving)
    {
        if(!DeclaresExactPage(table,page)||
            !ReferenceEquals(catalog.ConfigTables.SingleOrDefault(item=>item.Id==table.Id),table)||
            !ReferenceEquals(catalog.ConfigPages.SingleOrDefault(item=>item.Id==page.Id),page)||
            !catalog.YzAdminTypes.Any(admin=>admin.Id==page.YzAdminTypeId&&admin.YzProjectId==catalog.YzProjectId))
            throw new NotSupportedException("全局精确范围必须属于当前项目实际发布的业务页或选择页及后台。");
        var parent=SingleParent(catalog,table,page,allowLogical:true,allowExactSingle:true);
        if(parent is null)
        {
            var columns=ConditionColumns(catalog,table,allowLogical:true);
            var fields=FixedConditionPlan.Compile(page.WhereSql,columns.Select(column=>column.ColumnName)).Fields;
            var tree=fields.Any(field=>CascadeTreeStorageColumns.IsLogicalNameField(table,columns,field));
            var logical=tree||fields.Any(field=>CrossTableStorageColumns.IsLogicalPathField(table,columns,field));
            RequireTarget(catalog,table,page,true,allowAccount:table.IsRongyu==0,allowLogical:logical,
                allowSelfTree:tree,allowExactSingle:true);
        }
        var value=page.Extra["exactDataManageMatchField"];
        var field=value.ValueKind==JsonValueKind.String?ExactDataManageValues.Field(value.GetString()):"";
        var declared=ConditionColumns(catalog,table,allowLogical:true);
        var match=declared.SingleOrDefault(column=>column.ColumnName==field);
        var storage=MetadataStoragePolicy.ForTable(catalog,table);
        var path=CrossTableStorageColumns.IsLogicalPathField(table,declared,field)||
            CascadeTreeStorageColumns.IsLogicalNameField(table,declared,field);
        if(match is null||match.IsVirtualColumn!=0||ExactDataManageValues.IsSensitiveField(field)||
            !path&&!GlobalPhysicalRelationValue.IsScalar(storage,table,match)&&
            !(table.IsRongyu==0&&storage.Kind(match)==MetadataFieldStorage.Join&&
                GlobalPhysicalRelationValue.IsPublishedScalar(catalog,storage,table,match,
                    resolving??new HashSet<string>(StringComparer.Ordinal),page,out _)))
            throw new NotSupportedException("全局精确范围要求非凭据标量、已发布的单值联接或级联路径字段。");
        _=ExactDataManageValues.Unrestricted.ReadLogical(match.LogicalType);
        return parent;
    }

    /// <summary>多值仅复用目标实体资格，源数组、名称和原页面规则仍由多值专用证明核对。</summary>
    public static void RequireMultiple(MetadataCatalog catalog,TableMetadata table,PageMetadata page)=>RequireTarget(catalog,table,page,true);
    /// <summary>验证全局多值字典实际发布的项目、常量及非冗余账号条件。</summary>
    public static void RequireProjectMultiCandidate(MetadataCatalog catalog,TableMetadata table,PageMetadata page,bool checkbox=false)
    {
        var condition=FixedConditionPlan.Compile(page.WhereSql,ConditionColumns(catalog,table,allowLogical:false)
            .Select(column=>column.ColumnName));
        if(!GlobalMultipleRelationTarget.IsProjectPage(table,page,condition,checkbox))
            throw new NotSupportedException("全局多值项目候选必须来自实际发布页的物理项目条件。");
        RequireTarget(catalog,table,page,true,allowAccount:table.IsRongyu==0,allowStoredProject:true);
    }

    /// <summary>跨表与全局候选可绑定受限账号等值；既有其它全局关系仍要求常量。</summary>
    public static void RequireAccountCandidate(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata? name=null)
    {
        if(name is null)RequireTarget(catalog,table,page,true,allowAccount:true);
        else RequirePublishedName(catalog,table,page,name,true,allowAccount:true);
    }

    /// <summary>物理冗余多值字典可按当前项目或实际账号限定，其他全局关系沿用各自证明。</summary>
    public static void RequireStoredMultiAccountCandidate(MetadataCatalog catalog,TableMetadata table,PageMetadata page)
    {
        if(table.IsRongyu!=1)throw new NotSupportedException("全局账号多值候选要求物理冗余字典。");
        RequireTarget(catalog,table,page,true,allowAccount:true,allowStoredProject:true);
    }

    /// <summary>仅跨表固定用途接受由完整路径读取的逻辑条件，名称和发布身份仍按原规则校验。</summary>
    public static void RequireCrossTableCandidate(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata? name=null)
    {
        if(name is null)RequireTarget(catalog,table,page,true,allowAccount:true,allowLogical:true);
        else RequirePublishedName(catalog,table,page,name,true,allowAccount:true,allowLogical:true,allowTyped:true);
    }

    /// <summary>多值目标和独立页面候选可使用由实际父记录限定的同一范围。</summary>
    public static void RequireMultiCandidate(MetadataCatalog catalog,TableMetadata table,PageMetadata page)
        =>RequireTarget(catalog,table,page,true,allowAccount:true,allowLogical:true,allowMultiParent:true,allowPhysicalMultiParent:true);

    /// <summary>只有会编译完整自树叶名的读取入口可以接受该类全局候选条件。</summary>
    public static void RequireSelfTreeConditionCandidate(MetadataCatalog catalog,TableMetadata table,PageMetadata page,bool allowMultiParent=false)
        =>RequireTarget(catalog,table,page,true,allowAccount:true,allowLogical:true,allowMultiParent:allowMultiParent,
            allowSelfTree:true,allowPhysicalMultiParent:allowMultiParent);

    /// <summary>全局非冗余页可按账号和真实物理父键限定单值候选。</summary>
    public static MultiTargetParentScope? PhysicalAccountParent(MetadataCatalog catalog,TableMetadata table,PageMetadata page)=>
        SingleParent(catalog,table,page,allowLogical:false);

    /// <summary>单值目标页可以同时执行真实物理父范围和已声明的级联条件。</summary>
    public static MultiTargetParentScope? PhysicalSingleParent(MetadataCatalog catalog,TableMetadata table,PageMetadata page)=>
        SingleParent(catalog,table,page) is {Logical:false} parent?parent:null;

    /// <summary>逻辑父仅接受已发布跨表层位，其完整路径由数据适配器展开。</summary>
    public static MultiTargetParentScope? SingleParent(MetadataCatalog catalog,TableMetadata table,PageMetadata page)=>
        SingleParent(catalog,table,page,allowLogical:true,allowSelectionParentOnly:true);

    private static MultiTargetParentScope? SingleParent(MetadataCatalog catalog,TableMetadata table,PageMetadata page,
        bool allowLogical,bool allowExactSingle=false,bool allowSelectionParentOnly=false)
    {
        // 冗余目标只开放原 type=1 页的物理父范围；逻辑父仍由 R0 计划还原。
        var storedSelection=table.IsRongyu==1&&allowSelectionParentOnly&&page.Extra.GetValueOrDefault("type").ToString()=="1";
        if(!Declared(table)||table.IsRongyu!=0&&!storedSelection)return null;
        var columns=ConditionColumns(catalog,table,allowLogical:true);
        var condition=FixedConditionPlan.Compile(page.WhereSql,columns.Select(column=>column.ColumnName));
        if(!condition.Variables.Any(MultiTargetParentScope.IsParameter))return null;
        var cross=condition.Fields.Any(field=>CrossTableStorageColumns.IsLogicalPathField(table,columns,field));
        var tree=condition.Fields.Any(field=>CascadeTreeStorageColumns.IsLogicalNameField(table,columns,field));
        // 多值页的项目与父键组合由多值入口校验，不借用单值候选的账号范围。
        if(GlobalMultipleRelationTarget.IsProjectParentPage(table,page,condition,
            checkbox:page.Extra.GetValueOrDefault("type").ToString()!="2"))return null;
        // 原单选可只由实际物理父范围限定；其余角色保留原账号或逻辑证明。
        var selectionParentOnly=allowSelectionParentOnly&&!cross&&!tree&&SelectionParentOnly(page,condition);
        if(cross||tree)
        {
            if(!allowLogical||storedSelection)return null;
        }
        else if(!selectionParentOnly&&!(allowExactSingle&&DeclaresExactMulti(table,page))&&
            !condition.Variables.Any(AccountConditionVariables.IsSupported))return null;
        var parent=MultiTargetParentScope.CompileGlobalPage(catalog,table,condition);
        if(parent is null||parent.Logical&&(!allowLogical||page.Extra.GetValueOrDefault("type").ToString()=="2"&&!allowExactSingle))return null;
        RequireTarget(catalog,table,page,true,allowAccount:true,allowLogical:cross||tree,allowMultiParent:true,
            allowSelfTree:tree,allowExactSingle:allowExactSingle,allowStoredSingleParent:storedSelection);
        return parent;
    }

    /// <summary>原单选允许物理父参数是唯一变量，其他账号或逻辑范围继续按各自合同证明。</summary>
    private static bool SelectionParentOnly(PageMetadata page,FixedConditionPlan condition)=>
        page.Extra.GetValueOrDefault("type").ToString()=="1"&&UsesOnlyParentVariables(condition);

    /// <summary>静态父页的动态条件只能是发布时已证明的父参数。</summary>
    public static bool UsesOnlyParentVariables(FixedConditionPlan condition)=>
        condition.Variables.Count>0&&condition.Variables.All(MultiTargetParentScope.IsParameter);

    /// <summary>只按明确用途开放R0，逻辑成员须在实际级联计划中进一步证明。</summary>
    private static void RequireTarget(MetadataCatalog catalog,TableMetadata table,PageMetadata page,bool allowR0,bool allowAccount=false,bool allowLogical=false,bool allowMultiParent=false,bool allowSelfTree=false,bool allowExactSingle=false,bool allowStoredProject=false,bool allowStoredSingleParent=false,bool allowPhysicalMultiParent=false)
    {
        if(!Declared(table)||(table.IsRongyu!=1&&!(allowR0&&table.IsRongyu==0))||table.IsView!=0||table.IsFunctionTable!=0)throw new NotSupportedException("全局跨表目标要求显式声明的普通R0或R1实体。");
        if(table.IsRongyu==0)GlobalDerivedRelationSource.RequireTable(catalog,table);
        if(table.YzProjectId!=catalog.YzProjectId||page.YzProjectId!=catalog.YzProjectId||page.ConfigTableId!=table.Id)throw new InvalidOperationException("全局跨表表及页面必须明确发布在当前模型中。");
        var columns=ConditionColumns(catalog,table,allowLogical);
        var storage=MetadataStoragePolicy.ForTable(catalog,table);
        var allowed=columns.Where(column=>storage.IsPhysical(column)&&column.IsVirtualColumn==0||
            LogicalConditionField(table,columns,column,allowLogical,allowSelfTree)).Select(column=>column.ColumnName);
        var condition=FixedConditionPlan.Compile(page.WhereSql,allowed);
        // 只有显式通过父范围证明的用途才解析父参数，同时保留原账号和逻辑字段校验。
        var projectParentScope=allowPhysicalMultiParent&&GlobalMultipleRelationTarget.IsProjectParentPage(table,page,condition,
            checkbox:page.Extra.GetValueOrDefault("type").ToString()!="2");
        var multiParent=allowMultiParent&&((table.IsRongyu==0||allowStoredSingleParent)&&
            (condition.Variables.Any(AccountConditionVariables.IsSupported)||allowExactSingle&&DeclaresExactMulti(table,page)||
                SelectionParentOnly(page,condition)||projectParentScope||allowPhysicalMultiParent&&UsesOnlyParentVariables(condition))||
            condition.Fields.Any(field=>CrossTableStorageColumns.IsLogicalPathField(table,columns,field)||
                allowSelfTree&&CascadeTreeStorageColumns.IsLogicalNameField(table,columns,field)))
            ?MultiTargetParentScope.CompileGlobalPage(catalog,table,condition):null;
        foreach(var term in condition.Terms.Where(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable)))
        {
            if((multiParent?.Target.ColumnName==term.Field||multiParent?.AdditionalTargets.Any(scope=>scope.Target.ColumnName==term.Field)==true)&&
                term.Values.Count==1&&
                MultiTargetParentScope.IsParameter(term.Values[0].Text))continue;
            if((allowStoredProject||projectParentScope&&multiParent is {Logical:false}&&
                multiParent.AdditionalTargets.All(parent=>!parent.Logical))&&GlobalMultipleRelationTarget.IsProjectScope(term))
            {
                var project=columns.Single(item=>item.ColumnName==term.Field);
                if(project.LogicalType is not ("int64" or "string")||!GlobalPhysicalRelationValue.IsScalar(storage,table,project))
                    throw new NotSupportedException("全局多值项目条件必须使用真实物理项目ID。");
                continue;
            }
            if(!allowAccount||term.Field=="yz_project_id"||term.Comparison!=FixedConditionComparison.Equal||term.Values.Count!=1||
                !AccountConditionVariables.IsPageField(term.Values[0].Text,term.Field))
                throw new NotSupportedException("全局跨表目标只允许已发布的固定条件或明确账号等值。");
            var column=columns.Single(item=>item.ColumnName==term.Field);
            if(column.LogicalType is not ("int32" or "int64" or "string")||
                LogicalConditionField(table,columns,column,allowLogical,allowSelfTree)||!GlobalPhysicalRelationValue.IsScalar(storage,table,column))
                throw new NotSupportedException("全局跨表账号条件要求真实非凭据的业务归属列。");
        }
        if(!allowExactSingle&&page.Extra.TryGetValue("exactDataManageMatchField",out var exact)&&!string.IsNullOrWhiteSpace(exact.ToString()))throw new NotSupportedException("全局跨表目标的精确管理范围尚未接线。");
    }
    /// <summary>原跨表名称保持物理字符串或已证明R1缓存；R0实时名称由独立单值或多值用途入口分别证明。</summary>
    public static void RequireName(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata name)=>RequirePublishedName(catalog,table,page,name,false);
    /// <summary>仅供单值候选证明R0逻辑名称，跨表原入口不会因此放开。</summary>
    public static void RequireSingleName(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata name)=>RequirePublishedName(catalog,table,page,name,true);
    /// <summary>跨表层名称可来自已发布的业务标量，路径和级联缓存使用统一文本。</summary>
    public static void RequireCrossTableName(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata name,bool allowAccount=false)
        =>RequirePublishedName(catalog,table,page,name,true,allowAccount,allowTyped:true);

    /// <summary>自树选项和缓存使用同一已发布的名称，按统一文本格式展示业务标量。</summary>
    public static void RequireSelfTreeName(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata name)
        =>RequirePublishedName(catalog,table,page,name,true,allowTyped:true);

    /// <summary>实时自树候选的名称与固定级联范围均来自实际发布页。</summary>
    public static void RequireScopedSelfTreeName(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata name)
        =>RequirePublishedName(catalog,table,page,name,true,allowAccount:true,allowLogical:true,allowTyped:true,allowSelfTree:true);

    /// <summary>名称资格与页条件共用原完整证明，存储模式资格由调用用途限定。</summary>
    private static void RequirePublishedName(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata name,bool allowR0,bool allowAccount=false,bool allowLogical=false,bool allowTyped=false,bool allowSelfTree=false)
    {
        RequireTarget(catalog,table,page,allowR0,allowAccount,allowLogical,allowSelfTree:allowSelfTree);var storage=MetadataStoragePolicy.ForTable(catalog,table);
        if(name.LogicalType!="string"&&!(name.ColumnName!="id"&&allowTyped&&MultiValueLabel.IsTyped(name.LogicalType)))
            throw new NotSupportedException("全局目标名称的类型超出当前用途。");
        if(GlobalPhysicalRelationValue.IsScalar(storage,table,name))return;
        if(!GlobalPhysicalRelationValue.IsPublishedScalar(catalog,storage,table,name,page))throw new NotSupportedException("全局跨表名称缺少完整物理或受管缓存证明。");
        var declared=ConditionColumns(catalog,table,allowLogical);
        var columns=declared.ToDictionary(column=>column.ColumnName,StringComparer.Ordinal);
        if(FixedConditionPlan.Compile(page.WhereSql,columns.Keys).Fields.Any(field=>
            !GlobalPhysicalRelationValue.IsScalar(storage,table,columns[field])&&!LogicalConditionField(table,declared,columns[field],allowLogical,allowSelfTree)))
            throw new NotSupportedException("全局名称候选仅接受非凭据物理标量或已声明的逻辑级联字段。");
    }

    private static IReadOnlyList<ColumnMetadata> ConditionColumns(MetadataCatalog catalog,TableMetadata table,bool allowLogical)
    {
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id).ToArray();
        return allowLogical&&table.IsRongyu==0?RelationStorageColumns.Expand(table,columns):columns;
    }

    /// <summary>逻辑条件只来自非冗余固定控件，凭据、虚拟字段和物理账号上下文仍分别处理。</summary>
    private static bool LogicalConditionField(TableMetadata table,IReadOnlyList<ColumnMetadata> columns,ColumnMetadata field,bool allowLogical,bool allowSelfTree)=>
        allowLogical&&field.IsVirtualColumn==0&&!ExactDataManageValues.IsSensitiveField(field.ColumnName)&&
        (CrossTableStorageColumns.IsLogicalPathField(table,columns,field.ColumnName)||
            allowSelfTree&&CascadeTreeStorageColumns.IsLogicalNameField(table,columns,field.ColumnName));

}
