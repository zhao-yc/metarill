namespace Metarill.Generation;

internal sealed record MultiNameWriteBinding(ColumnModel Source,TableModel Target,ColumnModel Value,ColumnModel Cache);

/// <summary>多值名称属于表级派生值；页级ID展示不能取消根缓存，多个名称页共享同一次目标当前读。</summary>
internal static class MultiNameWriteCompilation
{
    /// <summary>仅汇总已证明的根定义与页级定义，不从相似列名推断缓存或解释客户端元数据。</summary>
    internal static IReadOnlyList<MultiNameWriteBinding> ForTable(GenerationModel model,TableModel table)
    {
        if(table.Source.IsRongyu!=1)return [];
        var definitions=new List<MultiSelectModel>();
        foreach(var page in table.Pages)
        foreach(var source in table.Columns.Where(column=>column.Source.ControlKind=="MultiSelectPage"))
        {
            if(MultiSelectCompilation.Resolve(model,table,page,source,forService:true).Definition is{} actual)definitions.Add(actual);
            if(MultiSelectCompilation.Resolve(model,table,page,source,forService:true,rootDefinition:true).Definition is{} root)definitions.Add(root);
        }
        foreach(var group in definitions.GroupBy(def=>def.Source.Source.Id))
            if(group.Select(def=>def.Target.Source.Id).Distinct(StringComparer.Ordinal).Count()>1)throw new InvalidOperationException("同一多值ID在不同页面绑定了不同目标表，无法证明表级缓存一致性。");
        var result=new List<MultiNameWriteBinding>();
        foreach(var group in definitions.Where(def=>def.Names is not null).GroupBy(def=>def.Names!.Source.Id))
        {
            if(group.Select(def=>(def.Source.Source.Id,def.Target.Source.Id,def.Name.Source.Id)).Distinct().Count()!=1)throw new InvalidOperationException("同一多值名称缓存存在不同来源或目标字段，无法生成一致写入规则。");
            var definition=group.First();result.Add(new(definition.Source,definition.Target,definition.Name,definition.Names!));
        }
        return result.OrderBy(binding=>binding.Cache.Source.ColumnName,StringComparer.Ordinal).ToArray();
    }
    /// <summary>每个可执行页都使用相同表级缓存集合，目标仍由本页真实候选验证与授权。</summary>
    internal static IReadOnlyList<MultiNameWriteBinding> ForSource(GenerationModel model,TableModel table,MultiSelectModel definition)=>ForTable(model,table).Where(binding=>binding.Source.Source.Id==definition.Source.Source.Id&&binding.Target.Source.Id==definition.Target.Source.Id).ToArray();
}
