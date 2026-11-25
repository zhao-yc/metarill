namespace Metarill.Generation;

/// <summary>证明原级联只写入当前功能表的表单参数，公共层必须使用同一条已发布路径。</summary>
internal static class FunctionTableCascadeCompilation
{
    /// <summary>完整包在接入原控件前检查读取和全部表单字段映射。</summary>
    internal static string? Reason(GenerationModel model,TableModel table,PageModel page,ColumnModel column)
    {
        var self=CascadeTreeReadCompilation.Resolve(model,table,page,column);
        if(self.Definition is {} tree)
            return Published(model,tree.Target,tree.TargetPage) ??
                FieldReason(table,column.Source.ColumnName[..^3]+"_name",tree.Target,asId:false);

        var cross=CrossTableReadCompilation.Resolve(model,table,page,column);
        if(cross.Definition is not {} plan)return $"{self.Reason} {cross.Reason}";
        if(CrossTableFrontendProof.Reason(model,page,plan) is {} mapping)return mapping;
        var peers=CrossTableReadCompilation.ForPage(model,table,page).Where(item=>item.Source.Source.Id!=column.Source.Id).ToArray();
        var controls=peers.Select(peer=>peer.Source.Source.Id).Append(column.Source.Id).ToHashSet(StringComparer.Ordinal);
        foreach(var level in plan.Levels)
        {
            var reason=Published(model,level.Target,level.Page) ??
                FieldReason(table,level.Prefix+"_id",level.Target,asId:true,controls,textKey:level.TextKey) ??
                FieldReason(table,level.Prefix+"_name",level.Target,asId:false);
            if(reason is not null)return reason;
        }

        foreach(var peer in peers)
        for(var index=0;index<plan.Levels.Count;index++)
        {
            var prefix=plan.Levels[index].Prefix;
            var shared=peer.Levels.TakeWhile(level=>level.Prefix!=prefix).Count();
            if(shared==peer.Levels.Count)continue;
            if(index!=shared||!plan.Levels.Take(index+1).Select(Scope).SequenceEqual(peer.Levels.Take(shared+1).Select(Scope)))
                return "功能表共享级联的上级路径、目标页或名称字段不一致。";
        }
        return null;
    }

    /// <summary>沿用已确认的共享清理，仅清空其他分支受影响的下级。</summary>
    internal static string? Frontend(GenerationModel model,TableModel table,PageModel page,ColumnModel column)
    {
        var plans=CrossTableReadCompilation.ForPage(model,table,page);
        var current=plans.SingleOrDefault(item=>item.Source.Source.Id==column.Source.Id);
        if(current is null)return null;
        var peers=plans.Where(item=>item.Source.Source.Id!=column.Source.Id).ToArray();
        var rules=new List<(string Field,string[][] Descendants)>();
        foreach(var level in current.Levels)
        {
            var descendants=peers.SelectMany(peer=>peer.Levels.SkipWhile(branch=>branch.Prefix!=level.Prefix).Skip(1))
                .DistinctBy(branch=>branch.Prefix).Select(branch=>new[]{GenerationModel.Camel(branch.Prefix+"_id"),GenerationModel.Camel(branch.Prefix+"_name")}).ToArray();
            if(descendants.Length>0)rules.Add((GenerationModel.Camel(level.Prefix+"_id"),descendants));
        }
        return rules.Count==0?null:SharedCrossTableRenderer.FrontendChanges(rules);
    }

    /// <summary>共享层的父列、目标投影和页面权限须同时一致。</summary>
    private static (string Prefix,string Table,string Page,string Label,string? Parent,string Key) Scope(CrossTableLevelModel level)=>
        (level.Prefix,level.Target.Source.Id,level.Page.Source.Id,level.Name.Source.Id,level.Parent?.Source.Id,level.KeyColumn);

    /// <summary>候选运行于包内已发布业务后台；跨表和自树都保留目标页读取权限。</summary>
    private static string? Published(GenerationModel model,TableModel target,PageModel page)
    {
        var project=model.Catalog.YzProjectId;
        return target.Source.YzProjectId==project && page.Source.YzProjectId==project &&
            model.Catalog.YzAdminTypes.Any(admin=>admin.Id==page.Source.YzAdminTypeId&&admin.YzProjectId==project)
            ?null:"功能表的级联目标必须明确属于包内项目和后台。";
    }

    /// <summary>仅允许原级联属性或相同类型的显式表单字段，避免覆盖其他控件。</summary>
    private static string? FieldReason(TableModel table,string field,TableModel target,bool asId,
        IReadOnlySet<string>? controls=null,bool textKey=false)
    {
        var column=table.Columns.SingleOrDefault(item=>item.JsonName.Equals(GenerationModel.Camel(field),StringComparison.OrdinalIgnoreCase));
        if(column is null)return null;
        if(column.Source.ColumnName!=field||column.IsSystem||column.IsJsonArray||
            (asId?column.Type is not("string?" or "long?"):column.Type!="string?")||
            (textKey&&column.Type!="string?")||
            (column.Source.ControlKind is not("" or "Text") && !(asId&&controls?.Contains(column.Source.Id)==true))||
            (asId&&column.Source.LinkConfigTableId.Length>0&&column.Source.LinkConfigTableId!=target.Source.Id))
            return $"功能表级联字段 {field} 与已声明的表单控件冲突。";
        // 共享分支自己的叶列可以同时是另一分支的祖先，实际路径仍须通过页面核对。
        return null;
    }
}
