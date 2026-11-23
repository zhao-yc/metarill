namespace Metarill.Core.Metadata;

/// <summary>原多值目标页的父参数绑定同一业务父表，可由实际物理列或稀疏级联还原。</summary>
public sealed record MultiSourceParent(ColumnMetadata Column,IReadOnlyList<PageMetadata> LogicalPages)
{
    /// <summary>同一个页面父参数约束的其它层位或物理列，名称读取也必须证明这些键一致。</summary>
    public IReadOnlyList<ColumnMetadata> AdditionalColumns { get; init; } = [];
}

/// <summary>多值页的父键来自固定关系，不由候选请求决定外键路径。</summary>
public sealed record MultiTargetParentScope(TableMetadata Parent,ColumnMetadata Target,bool Logical)
{
    public bool Global=>GlobalCrossTableTarget.Declared(Parent);
    public IReadOnlyList<MultiTargetParentScope> AdditionalTargets { get; init; } = [];

    /// <summary>按稳定字段 ID 检查目标范围，稀疏逻辑位须使用其真实路径。</summary>
    public bool HasTarget(string columnId,bool logicalOnly=false)=>
        Target.Id==columnId&&(!logicalOnly||Logical)||
        AdditionalTargets.Any(scope=>scope.Target.Id==columnId&&(!logicalOnly||scope.Logical));

    /// <summary>原多选目标页可以用多个已证明的字段匹配同一业务父记录。</summary>
    public static MultiTargetParentScope? CompileSelectionPage(MetadataCatalog catalog,TableMetadata table,FixedConditionPlan condition,
        bool allowGlobalParent=false)=>
        CompilePublishedPage(catalog,table,condition,checkbox:false,allowGlobalParent:allowGlobalParent);

    /// <summary>实际多选页可显式开放项目内冗余目标的多个物理父键。</summary>
    public static MultiTargetParentScope? CompileSelectionPage(MetadataCatalog catalog,TableMetadata table,FixedConditionPlan condition,
        bool allowGlobalParent,bool allowStoredPhysicalParents)=>
        CompilePublishedPage(catalog,table,condition,checkbox:false,allowGlobalParent:allowGlobalParent,
            allowStoredPhysicalParents:allowStoredPhysicalParents);

    /// <summary>数据库复选发布页可用显式物理父字段，项目内冗余目标的同父组合须由调用方证明。</summary>
    public static MultiTargetParentScope? CompileCheckboxPage(MetadataCatalog catalog,TableMetadata table,FixedConditionPlan condition,
        bool allowGlobalParent=false,bool allowStoredPhysicalParents=false)=>
        CompilePublishedPage(catalog,table,condition,checkbox:true,allowGlobalParent:allowGlobalParent,
            allowStoredPhysicalParents:allowStoredPhysicalParents);

    /// <summary>全局发布页可使用同一真实父表的物理父字段和已声明级联层位，父页由调用方逐项核对。</summary>
    public static MultiTargetParentScope? CompileGlobalPage(MetadataCatalog catalog,TableMetadata table,FixedConditionPlan condition,bool checkbox=false)=>
        CompilePublishedPage(catalog,table,condition,checkbox,globalTarget:true,allowGlobalParent:true);

    /// <summary>下拉字典把实际页面和控件的父参数合成同一组发布范围。</summary>
    public static MultiTargetParentScope? CompileListPage(MetadataCatalog catalog,TableMetadata table,
        IEnumerable<FixedConditionTerm> terms)
    {
        // 同一物理列可重复声明，原前端的两种父参数拼法语义一致。
        var declarations=terms.Where(ParentTerm).Select(term=>
            term is {Comparison:FixedConditionComparison.Equal,Values:[{Kind:FixedConditionValueKind.Variable} value]}&&
            IsParameter(value.Text)?term.Field+"=@parentId":term.ToDefinition()).Distinct(StringComparer.Ordinal);
        var fields=RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id))
            .Select(column=>column.ColumnName);
        var condition=FixedConditionPlan.Compile(string.Join(" AND ",declarations),fields);
        return CompileSelectionPage(catalog,table,condition,allowGlobalParent:true,allowStoredPhysicalParents:true);
    }

    private static MultiTargetParentScope? CompilePublishedPage(MetadataCatalog catalog,TableMetadata table,FixedConditionPlan condition,
        bool checkbox,bool globalTarget=false,bool allowGlobalParent=false,bool allowStoredPhysicalParents=false)
    {
        var terms=condition.Terms.Where(ParentTerm).ToArray();
        if(terms.Length<=1)return Compile(catalog,table,condition);
        // 冗余字典新增的多个父键必须是同一个项目内业务父表的真实物理列。
        var storedProject=allowStoredPhysicalParents&&table.IsRongyu==1&&!globalTarget;
        if(table.IsRongyu!=0&&!storedProject||GlobalCrossTableTarget.Declared(table)!=globalTarget)throw Invalid();
        var fields=RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id))
            .Select(column=>column.ColumnName).ToArray();
        var scopes=terms.Select(term=>Compile(catalog,table,FixedConditionPlan.Compile(term.ToDefinition(),fields),checkbox)!)
            .DistinctBy(scope=>scope.Target.Id).OrderByDescending(scope=>scope.Logical)
            .ThenBy(scope=>scope.Target.Id,StringComparer.Ordinal).ToArray();
        var parent=scopes[0];
        if(parent.Global&&(!allowGlobalParent||storedProject)||storedProject&&scopes.Any(scope=>scope.Logical)||
            scopes.Any(scope=>scope.Parent.Id!=parent.Parent.Id))throw Invalid();
        return parent with{AdditionalTargets=scopes[1..]};
    }

    /// <summary>只从已编译的元数据识别父参数，值由实际入口绑定。</summary>
    private static bool ParentTerm(FixedConditionTerm term)=>term.Values.Any(value=>
        value.Kind==FixedConditionValueKind.Variable&&IsParameter(value.Text));

    public static bool IsParameter(string variable)=>variable.Equals("parent_id",StringComparison.OrdinalIgnoreCase)||
        variable.Equals("parentId",StringComparison.OrdinalIgnoreCase);

    /// <summary>父参数只允许一处等值条件；目标的其它条件仍交给页面读取计划。</summary>
    public static MultiTargetParentScope? Compile(MetadataCatalog catalog,TableMetadata table,FixedConditionPlan condition)=>
        Compile(catalog,table,condition,checkbox:false);

    /// <summary>原复选控件可以显式绑定自定义物理父键，并允许目标使用 int32。</summary>
    public static MultiTargetParentScope? CompileCheckboxParent(MetadataCatalog catalog,TableMetadata table,FixedConditionPlan condition)=>
        Compile(catalog,table,condition,checkbox:true);

    private static MultiTargetParentScope? Compile(MetadataCatalog catalog,TableMetadata table,FixedConditionPlan condition,bool checkbox)
    {
        var terms=condition.Terms.Where(ParentTerm).ToArray();
        if(terms.Length==0)return null;
        if(terms.Length!=1||terms[0].Comparison!=FixedConditionComparison.Equal||terms[0].Values.Count!=1)
            throw Invalid();
        var columns=catalog.ConfigColumns.Where(item=>item.ConfigTableId==table.Id).ToArray();
        var column=RelationStorageColumns.Expand(table,columns).SingleOrDefault(item=>item.ColumnName==terms[0].Field);
        if(column is null||column.IsVirtualColumn!=0)throw Invalid();
        var originalKey=column.LogicalType is "int64" or "string"&&column.ColumnName.EndsWith("_id",StringComparison.Ordinal);
        var checkboxKey=checkbox&&CheckboxKey(catalog,table,column,target:true);
        if(!originalKey&&!checkboxKey)throw Invalid();
        var logical=CrossTableStorageColumns.IsLogicalPathField(table,columns,column.ColumnName);
        var parent=logical?CrossParent(catalog,columns,column):(PhysicalKey(catalog,table,column)||checkboxKey)
            ?catalog.ConfigTables.SingleOrDefault(item=>References(column,item)):null;
        if(parent is null||parent.IsView!=0||parent.IsFunctionTable!=0||
            parent.YzProjectId.Length>0&&parent.YzProjectId!=catalog.YzProjectId)throw Invalid();
        var storage=MetadataStoragePolicy.ForTable(catalog,parent);
        var id=storage.PhysicalColumns.SingleOrDefault(item=>item.ColumnName=="id");
        var project=storage.PhysicalColumns.SingleOrDefault(item=>item.ColumnName=="yz_project_id");
        if(id is not {LogicalType:"int64",IsDbNotNull:1}||!GlobalPhysicalRelationValue.IsScalar(storage,parent,id))throw Invalid();
        // 全局父必须由实际级联层位或显式物理表关系证明，缺项目列不能推定全局。
        if(GlobalCrossTableTarget.Declared(parent))
        {
            if(!logical&&column.LinkConfigTableId!=parent.Id||parent.YzProjectId!=catalog.YzProjectId||parent.IsRongyu is not(0 or 1))throw Invalid();
        }
        else if(project is not {LogicalType:"int64" or "string"}||!GlobalPhysicalRelationValue.IsScalar(storage,parent,project))
            throw Invalid();
        return new(parent,column,logical);
    }

    /// <summary>读回原页面的表格或详情开放设置，显式覆盖优先。</summary>
    private static bool Published(PageColumnMetadata field,string key,int fallback)=>
        field.Extra.TryGetValue(key,out var value)
            ?value.ValueKind==System.Text.Json.JsonValueKind.True||value.ToString()=="1"
            :fallback==1;

    /// <summary>稀疏列前缀可以与真实表不同；共享分支必须一致指向同一个层位目标。</summary>
    private static TableMetadata? CrossParent(MetadataCatalog catalog,IReadOnlyList<ColumnMetadata> columns,ColumnMetadata column)
    {
        var targets=new HashSet<string>(StringComparer.Ordinal);
        foreach(var control in columns.Where(item=>item.ControlKind=="CascadeTree"&&item.IsVirtualColumn==0))
        {
            var prefixes=CrossTableStorageColumns.LevelPrefixes(control);
            var index=Array.FindIndex(prefixes.ToArray(),prefix=>prefix+"_id"==column.ColumnName);
            if(index<0)continue;
            using var detail=System.Text.Json.JsonDocument.Parse(control.ControlDetail);
            targets.Add(detail.RootElement.GetProperty("levels")[index].GetProperty("table").GetString()!);
        }
        if(targets.Count!=1)throw Invalid();
        var target=catalog.ConfigTables.SingleOrDefault(item=>item.TableName==targets.Single());
        if(column.LinkConfigTableId.Length>0&&column.LinkConfigTableId!=target?.Id)throw Invalid();
        return target;
    }

    /// <summary>独立回读与回填从真实来源行取父键，不能从请求、当前账号或名称缓存猜测。</summary>
    public MultiSourceParent Source(MetadataCatalog catalog,TableMetadata source,ColumnMetadata relation)=>
        Source(catalog,source,relation,physicalSingle:false);

    /// <summary>按实际业务页证明多值读取的来源父键。</summary>
    public MultiSourceParent Source(MetadataCatalog catalog,TableMetadata source,ColumnMetadata relation,PageMetadata sourcePage)=>
        ResolveSource(catalog,source,relation,single:false,allowLogical:true,sourcePageId:sourcePage.Id);

    /// <summary>共享的复选名称必须从各来源页同一个真实外键读回，独立表单页须实际开放该字段。</summary>
    public MultiSourceParent CheckboxSource(MetadataCatalog catalog,TableMetadata source,ColumnMetadata relation,
        bool allowProjectPhysicalColumns=false)=>
        ResolveSource(catalog,source,relation,single:false,allowLogical:false,checkbox:true,allowFormParent:true,
            allowProjectPhysicalCheckbox:allowProjectPhysicalColumns);

    /// <summary>复选仍从当前页已声明的父范围取值，支持通过关联表已明确的自定义字段名。</summary>
    public MultiSourceParent CheckboxSource(MetadataCatalog catalog,TableMetadata source,ColumnMetadata relation,PageMetadata sourcePage,
        bool allowFormParent=false,bool allowProjectPhysicalColumns=false)=>
        ResolveSource(catalog,source,relation,single:false,allowLogical:true,sourcePageId:sourcePage.Id,checkbox:true,allowFormParent:allowFormParent,
            allowProjectPhysicalCheckbox:allowProjectPhysicalColumns);


    /// <summary>单值固定页和多值页共用持久父键证明；单值只允许实际物理父字段。</summary>
    public MultiSourceParent Source(MetadataCatalog catalog,TableMetadata source,ColumnMetadata relation,bool physicalSingle)=>
        ResolveSource(catalog,source,relation,single:physicalSingle,allowLogical:!physicalSingle);

    /// <summary>非冗余来源使用所有发布页一致的级联父位；冗余来源保留物理外键。</summary>
    public MultiSourceParent SingleSource(MetadataCatalog catalog,TableMetadata source,ColumnMetadata relation)=>
        ResolveSource(catalog,source,relation,single:true,allowLogical:source.IsRongyu==0);

    /// <summary>嵌套名称已经固定实际中间页，父键只由该页证明。</summary>
    public MultiSourceParent SingleSource(MetadataCatalog catalog,TableMetadata source,ColumnMetadata relation,PageMetadata sourcePage,
        bool allowLogicalListForm=false)=>
        ResolveSource(catalog,source,relation,single:true,allowLogical:source.IsRongyu==0,sourcePageId:sourcePage.Id,
            allowLogicalListForm:allowLogicalListForm);

    private MultiSourceParent ResolveSource(MetadataCatalog catalog,TableMetadata source,ColumnMetadata relation,
        bool single,bool allowLogical,string? sourcePageId=null,bool checkbox=false,bool allowFormParent=false,
        bool allowProjectPhysicalCheckbox=false,bool allowLogicalListForm=false)
    {
        var columns=catalog.ConfigColumns.Where(item=>item.ConfigTableId==source.Id).ToArray();
        var keys=columns.Where(column=>(PhysicalKey(catalog,source,column)||checkbox&&CheckboxKey(catalog,source,column,target:false))&&References(column,Parent)).ToArray();
        var explicitKeys=keys.Where(column=>column.LinkConfigTableId==Parent.Id).ToArray();
        var allFields=RelationStorageColumns.Expand(source,columns);
        // 项目内物理组合只扩展普通字典，显式全局字典继续要求原发布证明。
        var projectTarget=catalog.ConfigTables.SingleOrDefault(table=>table.Id==Target.ConfigTableId&&!GlobalCrossTableTarget.Declared(table));
        var projectSource=!Global&&source.IsRongyu is (0 or 1)&&!GlobalCrossTableTarget.Declared(source)&&projectTarget is not null;
        var projectPhysicalSource=projectSource&&(relation.ControlKind=="MultiSelectPage"||checkbox&&allowProjectPhysicalCheckbox);
        var projectListSource=projectSource&&single&&relation.ControlKind=="List";
        var selected=catalog.ConfigPages.Where(page=>page.ConfigTableId==source.Id&&
            (sourcePageId is null||page.Id==sourcePageId)).Select(page=>
        {
            var fields=allFields.Select(column=>column.ColumnName);
            var scoped=FixedConditionPlan.Compile(page.WhereSql,fields).Terms.Where(term=>
                term.Comparison==FixedConditionComparison.Equal&&term.Values.Count==1&&
                term.Values[0].Kind==FixedConditionValueKind.Variable&&IsParameter(term.Values[0].Text)).ToArray();
            // 独立表单父级只能从实际详情开放的唯一物理字段取得。
            if(allowFormParent&&scoped.Length==0)
            {
                var displayed=catalog.ConfigPageColumns.Where(setting=>setting.ConfigPageId==page.Id&&
                    (setting.Extra.TryGetValue("isInEdit",out var flag)?flag.ValueKind==System.Text.Json.JsonValueKind.True||flag.ToString()=="1":setting.IsEdit==1))
                    .Select(setting=>setting.ConfigColumnId).ToHashSet(StringComparer.Ordinal);
                var formKeys=keys.Where(key=>key.Id!=relation.Id&&displayed.Contains(key.Id)).Take(2).ToArray();
                if(formKeys.Length!=1)throw Invalid();
                return (Column:(ColumnMetadata?)formKeys[0],Page:(PageMetadata?)null,AdditionalColumns:Array.Empty<ColumnMetadata>());
            }
            // 普通冗余字典可以使用本页实际开放的逻辑祖先作为独立表单父，公开名称和导入读取同一个键。
            if(allowLogicalListForm&&allowLogical&&scoped.Length==0&&relation.ControlKind=="List"&&!Global)
            {
                var displayed=catalog.ConfigPageColumns.Where(setting=>setting.ConfigPageId==page.Id&&
                    (Published(setting,"isInGrid",setting.IsShow)||Published(setting,"isInEdit",setting.IsEdit)))
                    .Select(setting=>setting.ConfigColumnId).ToHashSet(StringComparer.Ordinal);
                var formFields=columns.Where(column=>column.Id!=relation.Id&&column.LinkConfigTableId==Parent.Id&&
                    displayed.Contains(column.Id)).Take(2).ToArray();
                if(formFields is [var form]&&form.IsVirtualColumn==0&&form.LogicalType is ("int64" or "string")&&
                    CrossTableStorageColumns.IsLogicalPathField(source,columns,form.ColumnName)&&CrossParent(catalog,columns,form)?.Id==Parent.Id)
                    return (Column:(ColumnMetadata?)form,Page:(PageMetadata?)page,AdditionalColumns:Array.Empty<ColumnMetadata>());
            }
            if(scoped.Length==1)
            {
                var field=scoped[0].Field;
                if(keys.SingleOrDefault(key=>key.ColumnName==field) is {} inherited)
                    return (Column:(ColumnMetadata?)inherited,Page:(PageMetadata?)null,AdditionalColumns:Array.Empty<ColumnMetadata>());
                if(allowLogical&&CrossTableStorageColumns.IsLogicalPathField(source,columns,field))
                {
                    var binding=Compile(catalog,source,FixedConditionPlan.Compile(page.WhereSql,fields));
                    if(binding?.Parent.Id==Parent.Id)return (Column:(ColumnMetadata?)binding.Target,Page:(PageMetadata?)page,AdditionalColumns:Array.Empty<ColumnMetadata>());
                }
            }
            var checkboxRelation=relation.ControlKind=="MultiCheckBoxDatabase";
            var multiRelation=relation.ControlKind=="MultiSelectPage";
            var multiSelection=multiRelation&&allowLogical&&source.IsRongyu==0&&
                scoped.Any(term=>CrossTableStorageColumns.IsLogicalPathField(source,columns,term.Field));
            var singleSelection=single&&relation.ControlKind is ("List" or "SelectPage");
            var compoundGlobal=Global&&(singleSelection||multiRelation||checkboxRelation);
            // 下拉来源页已经约束目标所属父表时，所有物理范围都必须纳入证明，不能遇到不同父表就退回单列。
            bool TargetKey(FixedConditionTerm term)=>keys.Any(column=>column.ColumnName==term.Field);
            var compoundPhysical=projectPhysicalSource&&scoped.All(TargetKey)||
                projectListSource&&scoped.Any(TargetKey)&&scoped.All(term=>
                    columns.Any(column=>column.ColumnName==term.Field&&PhysicalKey(catalog,source,column)));
            if(scoped.Length>1&&(checkboxRelation||multiSelection||compoundGlobal||compoundPhysical||singleSelection&&allowLogical))
            {
                // 只有所有范围指向同一父表时才能绑定原 parentId，独立表单父字段仍按原定义解析。
                var parents=scoped.Select(term=>Compile(catalog,source,
                    FixedConditionPlan.Compile(term.ToDefinition(),fields),checkbox:checkboxRelation))
                    .OfType<MultiTargetParentScope>().ToArray();
                if(parents.Length==scoped.Length&&parents.All(parent=>parent.Parent.Id==Parent.Id&&(!parent.Logical||allowLogical)))
                {
                    var logicalParents=parents.Where(parent=>parent.Logical);
                    // 跨页读取固定代表列，已证明的同父关系保留来源的全部实际字段。
                    var keepAll=multiSelection||compoundGlobal||compoundPhysical;
                    var representative=keepAll
                        ?logicalParents.MinBy(parent=>parent.Target.Id,StringComparer.Ordinal)??parents.MinBy(parent=>parent.Target.Id,StringComparer.Ordinal)!
                        :logicalParents.FirstOrDefault()??parents[0];
                    var additional=keepAll?parents.Select(parent=>parent.Target)
                        .Where(column=>column.Id!=representative.Target.Id).DistinctBy(column=>column.Id)
                        .OrderBy(column=>column.Id,StringComparer.Ordinal).ToArray():[];
                    return (Column:(ColumnMetadata?)representative.Target,Page:representative.Logical?page:null,AdditionalColumns:additional);
                }
                if(checkboxRelation||parents.Any(parent=>parent.Parent.Id==Parent.Id)&&
                    (explicitKeys.Length!=1||scoped.Any(term=>term.Field==explicitKeys[0].ColumnName)))
                    throw Invalid();
            }
            return (Column:(relation.ControlKind=="MultiSelectPage"||single&&relation.ControlKind is ("List" or "SelectPage"))&&
                explicitKeys.Length==1?explicitKeys[0]:null,Page:(PageMetadata?)null,AdditionalColumns:Array.Empty<ColumnMetadata>());
        }).ToArray();
        if(selected.Length==0||selected.Any(item=>item.Column is null)||
            selected.Select(item=>item.Column!.Id).Distinct(StringComparer.Ordinal).Count()!=1||
            selected.Any(item=>item.Page is not null)&&selected.Any(item=>item.Page is null)||
            selected.Any(item=>!item.AdditionalColumns.Select(column=>column.Id)
                .SequenceEqual(selected[0].AdditionalColumns.Select(column=>column.Id),StringComparer.Ordinal)))throw Invalid();
        return new(selected[0].Column!,selected.Select(item=>item.Page).OfType<PageMetadata>().ToArray())
            {AdditionalColumns=selected[0].AdditionalColumns};
    }

    private static bool References(ColumnMetadata column,TableMetadata table)=>column.LinkConfigTableId.Length>0
        ?column.LinkConfigTableId==table.Id
        :column.ColumnName==table.TableName+"_id";

    private static bool PhysicalKey(MetadataCatalog catalog,TableMetadata table,ColumnMetadata column)=>
        column.LogicalType is "int64" or "string"&&column.ColumnName.EndsWith("_id",StringComparison.Ordinal)&&
        column.ColumnName!="yz_project_id"&&GlobalPhysicalRelationValue.IsScalar(MetadataStoragePolicy.ForTable(catalog,table),table,column)&&
        !CrossTableStorageColumns.IsLogicalPathField(table,catalog.ConfigColumns,column.ColumnName);

    /// <summary>约定名仍可推导父表，自定义字段必须显式引用同一实体。</summary>
    private static bool CheckboxKey(MetadataCatalog catalog,TableMetadata table,ColumnMetadata column,bool target)=>
        (column.LogicalType is "int64" or "string"||target&&column.LogicalType=="int32")&&
        (column.ColumnName.EndsWith("_id",StringComparison.Ordinal)||column.LinkConfigTableId.Length>0)&&
        column.ColumnName!="yz_project_id"&&GlobalPhysicalRelationValue.IsScalar(MetadataStoragePolicy.ForTable(catalog,table),table,column)&&
        !CrossTableStorageColumns.IsLogicalPathField(table,catalog.ConfigColumns,column.ColumnName);


    private static InvalidOperationException Invalid()=>new("多值目标页父参数要求同一真实父表，以及所有来源入口一致的父键。");
}
