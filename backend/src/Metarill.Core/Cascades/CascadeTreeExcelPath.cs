using Metarill.Core.Contracts;
namespace Metarill.Core.Cascades;

/// <summary>沿原Excel完整路径格式反查叶子；不把数字名称误当ID，不猜重复路径。</summary>
public static class CascadeTreeExcelPath
{
    /// <summary>不能无损表示的名称拒绝导出，避免重新导入时指向另一个节点。</summary>
    public static string Format(CascadeTreePath? path,string joiner)
    {
        if(string.IsNullOrEmpty(joiner)||path is null||path.Path.Count==0)throw new InvalidOperationException("级联路径无效，不能导出为可反查的完整路径。");
        if(path.Path.Any(node=>string.IsNullOrWhiteSpace(node.Name)||node.Name!=node.Name.Trim()||node.Name.Contains(joiner,StringComparison.Ordinal)))throw new InvalidOperationException("级联名称包含空白或路径分隔符，不能安全导出，请调整名称或分隔符。");
        return string.Join(joiner,path.Path.Select(node=>node.Name));
    }
    /// <summary>原分隔规则按完整分隔符切割、去首尾空白/空段，每一层都要求唯一可见候选。</summary>
    public static async Task<long> ResolveAsync(string text,string joiner,Func<string,long?,CancellationToken,Task<IReadOnlyList<CascadeTreeNode>>> find,CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrEmpty(joiner))throw new InvalidOperationException("级联分隔符未配置。");
        var parts=text.Split(joiner,StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(parts.Length is 0 or >SelfTreeReader.MaximumDepth)throw PageImportException.Input(PageImportError.Relation);
        long? parent=null;var visited=new HashSet<long>();
        foreach(var part in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();var rows=await find(part,parent,cancellationToken);
            if(rows.Count!=1||rows[0].Id<=0||!visited.Add(rows[0].Id))throw PageImportException.Input(PageImportError.Relation);
            parent=rows[0].Id;
        }
        return parent!.Value;
    }
}
