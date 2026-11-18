namespace Metarill.Core.Metadata;

/// <summary>原多值目标页的父参数只能绑定同一业务父表的物理单值键。</summary>
public sealed record MultiTargetParentScope(TableMetadata Parent,ColumnMetadata Target)
{
    public static bool IsParameter(string variable)=>variable.Equals("parent_id",StringComparison.OrdinalIgnoreCase)||
        variable.Equals("parentId",StringComparison.OrdinalIgnoreCase);

    /// <summary>父参数只允许一处等值条件；目标的其它条件仍交给页面读取计划。</summary>
    public static MultiTargetParentScope? Compile(MetadataCatalog catalog,TableMetadata table,FixedConditionPlan condition)
    {
        var terms=condition.Terms.Where(term=>term.Values.Any(value=>
            value.Kind==FixedConditionValueKind.Variable&&IsParameter(value.Text))).ToArray();
        if(terms.Length==0)return null;
        if(terms.Length!=1||terms[0].Comparison!=FixedConditionComparison.Equal||terms[0].Values.Count!=1)
            throw Invalid();
        var column=catalog.ConfigColumns.SingleOrDefault(item=>item.ConfigTableId==table.Id&&item.ColumnName==terms[0].Field);
        if(column is null||!PhysicalKey(catalog,table,column))throw Invalid();
        var parent=catalog.ConfigTables.SingleOrDefault(item=>References(column,item));
        if(parent is null||GlobalCrossTableTarget.Declared(parent)||parent.IsView!=0||parent.IsFunctionTable!=0||
            parent.YzProjectId.Length>0&&parent.YzProjectId!=catalog.YzProjectId)throw Invalid();
        var storage=MetadataStoragePolicy.ForTable(catalog,parent);
        var id=storage.PhysicalColumns.SingleOrDefault(item=>item.ColumnName=="id");
        var project=storage.PhysicalColumns.SingleOrDefault(item=>item.ColumnName=="yz_project_id");
        if(id is not {LogicalType:"int64",IsDbNotNull:1}||!GlobalPhysicalRelationValue.IsScalar(storage,parent,id)||
            project is not {LogicalType:"int64" or "string"}||!GlobalPhysicalRelationValue.IsScalar(storage,parent,project))
            throw Invalid();
        return new(parent,column);
    }

    /// <summary>独立回读与回填从真实来源行取父键，不能从请求、当前账号或名称缓存猜测。</summary>
    public ColumnMetadata Source(MetadataCatalog catalog,TableMetadata source,ColumnMetadata relation)
    {
        var columns=catalog.ConfigColumns.Where(item=>item.ConfigTableId==source.Id).ToArray();
        var keys=columns.Where(column=>PhysicalKey(catalog,source,column)&&References(column,Parent)).ToArray();
        var explicitKeys=keys.Where(column=>column.LinkConfigTableId==Parent.Id).ToArray();
        var selected=catalog.ConfigPages.Where(page=>page.ConfigTableId==source.Id).Select(page=>
        {
            var fields=RelationStorageColumns.Expand(source,columns).Select(column=>column.ColumnName);
            var scoped=FixedConditionPlan.Compile(page.WhereSql,fields).Terms.Where(term=>
                term.Comparison==FixedConditionComparison.Equal&&term.Values.Count==1&&
                term.Values[0].Kind==FixedConditionValueKind.Variable&&IsParameter(term.Values[0].Text)).ToArray();
            if(scoped.Length==1&&keys.SingleOrDefault(key=>key.ColumnName==scoped[0].Field) is {} inherited)
                return inherited;
            return relation.ControlKind=="MultiSelectPage"&&explicitKeys.Length==1?explicitKeys[0]:null;
        }).ToArray();
        if(selected.Length==0||selected.Any(column=>column is null)||
            selected.Select(column=>column!.Id).Distinct(StringComparer.Ordinal).Count()!=1)throw Invalid();
        return selected[0]!;
    }

    private static bool References(ColumnMetadata column,TableMetadata table)=>column.LinkConfigTableId.Length>0
        ?column.LinkConfigTableId==table.Id
        :column.ColumnName==table.TableName+"_id";

    private static bool PhysicalKey(MetadataCatalog catalog,TableMetadata table,ColumnMetadata column)=>
        column.LogicalType is "int64" or "string"&&column.ColumnName.EndsWith("_id",StringComparison.Ordinal)&&
        column.ColumnName!="yz_project_id"&&GlobalPhysicalRelationValue.IsScalar(MetadataStoragePolicy.ForTable(catalog,table),table,column)&&
        !CrossTableStorageColumns.IsLogicalPathField(table,catalog.ConfigColumns,column.ColumnName);

    private static InvalidOperationException Invalid()=>new("多值目标页父参数要求同一真实父表，以及所有来源入口一致的物理父键。");
}
