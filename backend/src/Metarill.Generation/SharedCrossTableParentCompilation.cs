namespace Metarill.Generation;

/// <summary>共享组尚未发布到缓存时，只用已证明的读取定义校验物理父身份，避免循环请求写计划。</summary>
internal static class SharedCrossTableParentCompilation
{
    /// <summary>复用原非冗余父表的主键／项目／标量证明；名称、派生身份和未知父表仍拒绝。</summary>
    internal static void Require(GenerationModel model,TableModel table,PageModel page)
    {
        if(!page.Scopes.Any(s=>s.Source=="parent"))return;var reads=CrossTableReadCompilation.ForService(model,table,page);
        _=NonRedundantSelfTreeSubset.ParentTables(model,table,page,column=>
        {
            var matches=reads.SelectMany(read=>read.Levels.Select((level,index)=>(Read:read,Index:index,Name:level.Prefix+"_id"))).Where(x=>x.Name==column.Source.ColumnName).ToArray();
            if(matches.Select(x=>x.Read.Levels[x.Index].Target.Source.Id).Distinct(StringComparer.Ordinal).Count()>1)throw new InvalidOperationException("共享逻辑父字段指向不同真实父表。");
            return matches.Length==0?null:(matches[0].Read,matches[0].Index);
        });
    }
}
