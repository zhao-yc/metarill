namespace Metarill.Data.Schema;

/// <summary>逻辑索引方向只接受有限词表；省略等同逐列ASC，不能把未知方向当作升序。</summary>
internal static class IndexDirectionRules
{
    /// <summary>每个方向与同位置字段绑定，返回独立副本避免调用者后改数组。</summary>
    internal static string[] Read(IndexSchema index)
    {
        if(index.Directions is null)return Enumerable.Repeat("ASC",index.Columns.Count).ToArray();
        if(index.Directions.Count!=index.Columns.Count)throw new InvalidOperationException("索引方向数量必须与字段数量一致。");
        return index.Directions.Select(value=>value?.ToUpperInvariant() switch{"ASC"=>"ASC","DESC"=>"DESC",_=>throw new InvalidOperationException("索引方向只接受ASC或DESC。")}).ToArray();
    }
    /// <summary>全ASC省略扩展属性，保持旧序列化／指纹；只在确有降序时保存完整位置数组。</summary>
    internal static IndexSchema Normalize(IndexSchema index)
    {
        var directions=Read(index);return index with{Directions=directions.Contains("DESC",StringComparer.Ordinal)?Array.AsReadOnly(directions):null};
    }
}
