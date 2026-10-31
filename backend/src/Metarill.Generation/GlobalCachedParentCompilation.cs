namespace Metarill.Generation;

/// <summary>无项目列R1来源的原物理List父上下文；只验证已发布父实体及活动状态，不扩大页面权限。</summary>
internal static class GlobalCachedParentCompilation
{
    /// <summary>父字段必须确为当前页的固定parent范围，并复用同一已证明List关系。</summary>
    internal static GlobalCachedSourceBinding? ForColumn(GenerationModel model,TableModel table,PageModel page,ColumnModel column)
        =>page.Scopes.Any(scope=>scope.Source=="parent"&&scope.Column.Source.Id==column.Source.Id)?GlobalCachedSourceCompilation.ForModel(model).SingleOrDefault(binding=>binding.Source.Source.Id==table.Source.Id&&binding.Foreign.Source.Id==column.Source.Id):null;
    /// <summary>同一父表的不同来源列分别验证，不能只凭表名代替角色。</summary>
    internal static IReadOnlyList<GlobalCachedSourceBinding> ForTable(GenerationModel model,TableModel table,PageModel page,TableModel parent)
        =>page.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>ForColumn(model,table,page,scope.Column)).OfType<GlobalCachedSourceBinding>().Where(binding=>binding.Target.Source.Id==parent.Source.Id).ToArray();
    /// <summary>写入取当前写锁，候选来源父只读；原父范围规则仍不借用目标页面的列表权限。</summary>
    internal static string Call(GlobalCachedSourceBinding binding,PageModel page,string parent,string locking)
        =>$"        await ValidateGlobalCachedParent{page.Source.Id}_{binding.Foreign.Source.Id}Async({parent},actor,{locking},cancellationToken);\n";
    /// <summary>与普通物理父规则一致，只校验真实ID/软删除；目录及Actor绑定由独立证明和服务入口保证。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page)
    {
        var result=new System.Text.StringBuilder();
        foreach(var binding in page.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>ForColumn(model,table,page,scope.Column)).OfType<GlobalCachedSourceBinding>().DistinctBy(binding=>binding.Foreign.Source.Id))
        {
            result.AppendLine("    /// <summary>全局父只属于当前发布目录；parentId不是项目或权限证明。</summary>")
                .AppendLine($"    private async Task ValidateGlobalCachedParent{page.Source.Id}_{binding.Foreign.Source.Id}Async(string? parentId,RuntimeActor actor,bool locking,CancellationToken cancellationToken)")
                .AppendLine("    {").AppendLine("        ValidateActor(actor);")
                .AppendLine("        if(!long.TryParse(parentId,NumberStyles.None,CultureInfo.InvariantCulture,out var key)||key<=0||parentId!=key.ToString(CultureInfo.InvariantCulture))throw new ArgumentException(\"当前页面要求规范正整数parentId。\");")
                .AppendLine($"        var query=session.Select<{binding.Target.EntityType}>().Where(entity=>entity.Id==key);");
            if(binding.Target.Columns.Any(column=>column.Source.ColumnName=="delete_no"))result.AppendLine("        query=query.Where(entity=>entity.DeleteNo==null);");
            result.AppendLine("        if(locking){if((await query.ForUpdate().Limit(1).ToListAsync(entity=>new{entity.Id},cancellationToken)).Count!=1)throw new InvalidOperationException(\"父记录不存在或已删除。\");}")
                .AppendLine("        else if(!await query.AnyAsync(cancellationToken))throw new InvalidOperationException(\"父记录不存在或已删除。\");").AppendLine("    }");
        }
        return result.ToString();
    }
}
