using System.Text.Json;
namespace Metarill.Generation;

/// <summary>非冗余自树使用完整页读取证明；父范围与混合关系逐项编译，不按控件数量排除。</summary>
internal static class NonRedundantSelfTreeSubset
{
    /// <summary>独立R1目标树、物理父归属和本页其它关系必须全部可执行，不能丢弃坏同伴字段。</summary>
    internal static bool Supports(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        if(table.Source.IsRongyu!=0||table.IsGlobal||ExactDataManageCompilation.Declared(page)||source.Source.ControlKind!="CascadeTree")return false;
        try
        {
            _=ParentTables(model,table,page);
            if(!FixedTarget(model,table,page,source))return false;
            var trees=table.Columns.Where(column=>column.Source.ControlKind=="CascadeTree").ToArray();
            foreach(var tree in trees.Where(tree=>!NonRedundantCrossTableSubset.Declared(tree)))
                if(!FixedTarget(model,table,page,tree))return false;
            // 跨表同伴按其实际路径与用途验证；不递归调用Supports，隐藏坏链仍拒绝整个包。
            if(trees.Any(NonRedundantCrossTableSubset.Declared)&&NonRedundantCrossTableSubset.Reason(model,table,page) is not null)return false;
            _=NonRedundantReadCompilation.ForPage(model,table,page);return true;
        }
        catch(Exception error)when(error is InvalidOperationException or NotSupportedException or JsonException or ArgumentException){return false;}
    }
    /// <summary>只证明单个独立R1自树目标，供混合检查共用；不反向依赖跨表完整包审计。</summary>
    internal static bool FixedTarget(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        var plan=CascadeTreeWriteCompilation.Resolve(model,table,page,source).Definition;
        return plan is not null&&plan.Read.Target.Source.IsRongyu==1&&!plan.Read.Target.IsGlobal&&plan.Read.Target.Source.Id!=table.Source.Id;
    }
    /// <summary>普通父外键或唯一固定跨表层位均须证明目标物理主键／项目列，不能以来源主键冒充父关系。</summary>
    internal static IReadOnlyList<TableModel> ParentTables(GenerationModel model,TableModel table,PageModel page)
    {
        var parents=new List<TableModel>();
        foreach(var scope in page.Scopes.Where(scope=>scope.Source=="parent"))
        {
            var column=scope.Column;
            if(column.IsId||column.Type is not("string?" or "long?")||!NonRedundantReadCompilation.Physical(model,table,column)||GenerationModel.Enabled(column.Source.Extra,"isJoinColumn"))throw new InvalidOperationException("非冗余自树父范围必须是真实单值外键。");
            var cross=NonRedundantCrossParentCompilation.Find(model,table,page,column);
            var parent=cross is{} path?path.Read.Levels[path.Index].Target:model.Tables.SingleOrDefault(candidate=>column.Source.LinkConfigTableId.Length>0?candidate.Source.Id==column.Source.LinkConfigTableId:candidate.Source.TableName+"_id"==column.Source.ColumnName);
            if(parent is null||parent.IsGlobal||parent.Source.Id==table.Source.Id||parent.Source.IsFunctionTable!=0||parent.Source.IsView!=0)throw new InvalidOperationException("非冗余自树父表缺少明确普通实体归属。");
            var id=parent.Columns.Single(candidate=>candidate.IsId);var project=parent.Columns.SingleOrDefault(candidate=>candidate.Source.ColumnName=="yz_project_id");
            if(project is null||project.Type is not("string?" or "long?")||!NonRedundantReadCompilation.Physical(model,parent,id)||!NonRedundantReadCompilation.Physical(model,parent,project)||GenerationModel.Enabled(id.Source.Extra,"isJoinColumn")||GenerationModel.Enabled(project.Source.Extra,"isJoinColumn"))throw new InvalidOperationException("非冗余自树父表主键／项目列必须是已证明的物理身份。");
            parents.Add(parent);
        }
        return parents.DistinctBy(parent=>parent.Source.Id).ToArray();
    }
}
