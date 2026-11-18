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
    /// <summary>多值仅复用目标实体资格，源数组、名称和原页面规则仍由多值专用证明核对。</summary>
    public static void RequireMultiple(MetadataCatalog catalog,TableMetadata table,PageMetadata page)=>RequireTarget(catalog,table,page,true);
    /// <summary>跨表与全局候选可绑定受限账号等值；既有其它全局关系仍要求常量。</summary>
    public static void RequireAccountCandidate(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata? name=null)
    {
        if(name is null)RequireTarget(catalog,table,page,true,allowAccount:true);
        else RequirePublishedName(catalog,table,page,name,true,allowAccount:true);
    }

    /// <summary>仅跨表固定用途接受由完整路径读取的逻辑条件，名称和发布身份仍按原规则校验。</summary>
    public static void RequireCrossTableCandidate(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata? name=null)
    {
        if(name is null)RequireTarget(catalog,table,page,true,allowAccount:true,allowLogical:true);
        else RequirePublishedName(catalog,table,page,name,true,allowAccount:true,allowLogical:true,allowTyped:true);
    }

    /// <summary>多值目标和独立页面候选可使用由实际父记录限定的同一范围。</summary>
    public static void RequireMultiCandidate(MetadataCatalog catalog,TableMetadata table,PageMetadata page)
        =>RequireTarget(catalog,table,page,true,allowAccount:true,allowLogical:true,allowMultiParent:true);

    /// <summary>只有会编译完整自树叶名的读取入口可以接受该类全局候选条件。</summary>
    public static void RequireSelfTreeConditionCandidate(MetadataCatalog catalog,TableMetadata table,PageMetadata page,bool allowMultiParent=false)
        =>RequireTarget(catalog,table,page,true,allowAccount:true,allowLogical:true,allowMultiParent:allowMultiParent,allowSelfTree:true);

    /// <summary>全局非冗余页可按账号和真实物理父键限定单值候选。</summary>
    public static MultiTargetParentScope? PhysicalAccountParent(MetadataCatalog catalog,TableMetadata table,PageMetadata page)=>
        SingleParent(catalog,table,page,allowLogical:false);

    /// <summary>单值目标页可以同时执行真实物理父范围和已声明的级联条件。</summary>
    public static MultiTargetParentScope? PhysicalSingleParent(MetadataCatalog catalog,TableMetadata table,PageMetadata page)=>
        SingleParent(catalog,table,page) is {Logical:false} parent?parent:null;

    /// <summary>逻辑父仅接受已发布跨表层位，其完整路径由数据适配器展开。</summary>
    public static MultiTargetParentScope? SingleParent(MetadataCatalog catalog,TableMetadata table,PageMetadata page)=>
        SingleParent(catalog,table,page,allowLogical:true);

    private static MultiTargetParentScope? SingleParent(MetadataCatalog catalog,TableMetadata table,PageMetadata page,bool allowLogical)
    {
        if(!Declared(table)||table.IsRongyu!=0)return null;
        var columns=ConditionColumns(catalog,table,allowLogical:true);
        var condition=FixedConditionPlan.Compile(page.WhereSql,columns.Select(column=>column.ColumnName));
        if(!condition.Variables.Any(MultiTargetParentScope.IsParameter))return null;
        var cross=condition.Fields.Any(field=>CrossTableStorageColumns.IsLogicalPathField(table,columns,field));
        var tree=condition.Fields.Any(field=>CascadeTreeStorageColumns.IsLogicalNameField(table,columns,field));
        if(cross||tree)
        {
            if(!allowLogical)return null;
        }
        else if(!condition.Variables.Any(AccountConditionVariables.IsSupported))return null;
        var parent=MultiTargetParentScope.Compile(catalog,table,condition);
        if(parent is null||parent.Logical&&(!allowLogical||page.Extra.GetValueOrDefault("type").ToString()=="2"))return null;
        RequireTarget(catalog,table,page,true,allowAccount:true,allowLogical:cross||tree,allowMultiParent:true,allowSelfTree:tree);
        return parent;
    }

    /// <summary>只按明确用途开放R0，逻辑成员须在实际级联计划中进一步证明。</summary>
    private static void RequireTarget(MetadataCatalog catalog,TableMetadata table,PageMetadata page,bool allowR0,bool allowAccount=false,bool allowLogical=false,bool allowMultiParent=false,bool allowSelfTree=false)
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
        var multiParent=allowMultiParent&&(table.IsRongyu==0&&condition.Variables.Any(AccountConditionVariables.IsSupported)||
            condition.Fields.Any(field=>CrossTableStorageColumns.IsLogicalPathField(table,columns,field)||
                allowSelfTree&&CascadeTreeStorageColumns.IsLogicalNameField(table,columns,field)))
            ?MultiTargetParentScope.Compile(catalog,table,condition):null;
        foreach(var term in condition.Terms.Where(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable)))
        {
            if(multiParent?.Target.ColumnName==term.Field&&term.Values.Count==1&&
                MultiTargetParentScope.IsParameter(term.Values[0].Text))continue;
            if(!allowAccount||term.Field=="yz_project_id"||term.Comparison!=FixedConditionComparison.Equal||term.Values.Count!=1||
                !AccountConditionVariables.IsPageField(term.Values[0].Text,term.Field))
                throw new NotSupportedException("全局跨表目标只允许已发布的固定条件或明确账号等值。");
            var column=columns.Single(item=>item.ColumnName==term.Field);
            if(column.LogicalType is not ("int32" or "int64" or "string")||
                LogicalConditionField(table,columns,column,allowLogical,allowSelfTree)||!GlobalPhysicalRelationValue.IsScalar(storage,table,column))
                throw new NotSupportedException("全局跨表账号条件要求真实非凭据的业务归属列。");
        }
        if(page.Extra.TryGetValue("exactDataManageMatchField",out var exact)&&!string.IsNullOrWhiteSpace(exact.ToString()))throw new NotSupportedException("全局跨表目标的精确管理范围尚未接线。");
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
        if(!GlobalPhysicalRelationValue.IsPublishedScalar(catalog,storage,table,name))throw new NotSupportedException("全局跨表名称缺少完整物理或受管缓存证明。");
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
