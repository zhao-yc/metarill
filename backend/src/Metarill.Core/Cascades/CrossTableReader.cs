using System.Globalization;
using System.Text.Json;
using Metarill.Core.Contracts;
namespace Metarill.Core.Cascades;

/// <summary>跨表逐级计划只包含冻结键名，数据库目标与范围由生成的强类型委托决定。</summary>
public sealed record CrossTableLevel(string Key,string IdProperty);
/// <summary>原跨表候选四字段，不增加自关联树专用hasChildren。</summary>
public sealed record CrossTableOption(string Id,string Name,string Value,string Text);
/// <summary>跨表路径保留原levelKey，允许不同表出现相同数字ID。</summary>
public sealed record CrossTablePathNode(string Id,string Name,string LevelKey);
public sealed record CrossTablePath(IReadOnlyList<CrossTablePathNode> Path,string DisplayText);

/// <summary>固定表链的候选及路径；只沿父键逆查，不接受请求提供表、SQL、条件或排序。</summary>
public sealed class CrossTableReader
{
    private readonly CrossTableLevel[] levels;
    private readonly Action<int> authorize;
    private readonly Func<int,long,CancellationToken,Task<CascadeTreeNode?>> find;
    private readonly Func<int,long?,CancellationToken,Task<IReadOnlyList<CascadeTreeNode>>> children;
    private readonly string joiner;
    /// <summary>复制冻结层级，禁止重复键与歧义JSON字段；授权委托即使空请求也必须执行。</summary>
    public CrossTableReader(IReadOnlyList<CrossTableLevel> levels,Action<int> authorize,
        Func<int,long,CancellationToken,Task<CascadeTreeNode?>> find,
        Func<int,long?,CancellationToken,Task<IReadOnlyList<CascadeTreeNode>>> children,string joiner)
    {
        this.levels=levels.ToArray();this.authorize=authorize;this.find=find;this.children=children;this.joiner=joiner;
        if(this.levels.Length<2||this.levels.Any(level=>string.IsNullOrWhiteSpace(level.Key)||string.IsNullOrWhiteSpace(level.IdProperty))||this.levels.Select(level=>level.Key).Distinct(StringComparer.Ordinal).Count()!=this.levels.Length||this.levels.Select(level=>level.IdProperty).Distinct(StringComparer.Ordinal).Count()!=this.levels.Length)throw new ArgumentException("跨表级联层级无效或重复。");
    }
    /// <summary>原selectedPath允许全部已知层级ID；父缺省返回空，给出父时逐跳校验可读链及已选祖先一致性。</summary>
    public async Task<IReadOnlyList<CrossTableOption>> OptionsAsync(string? levelKey,JsonElement selectedPath,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();var index=Array.FindIndex(levels,level=>level.Key==levelKey);if(index<0)throw new ArgumentException("未知跨表级联层标识。");authorize(index);
        // 原Controller将省略/null转换为空对象；其它类型仍明确拒绝。
        if(selectedPath.ValueKind is not(JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined))throw new ArgumentException("selectedPath必须为对象。");
        var selected=new Dictionary<string,long?>(StringComparer.Ordinal);
        if(selectedPath.ValueKind==JsonValueKind.Object)foreach(var field in selectedPath.EnumerateObject())if(!levels.Any(level=>level.IdProperty==field.Name)||!selected.TryAdd(field.Name,CascadeTreeId.Parse(field.Value,true)))throw new ArgumentException("跨表级联选中路径含未知或重复字段。");
        long? parent=null;
        if(index>0)
        {
            parent=selected.GetValueOrDefault(levels[index-1].IdProperty);if(parent is null)return [];
            var path=await PathAsync(index-1,parent,cancellationToken);
            for(var ancestor=0;ancestor<index;ancestor++)if(selected.GetValueOrDefault(levels[ancestor].IdProperty) is{} supplied&&path.Path[ancestor].Id!=supplied.ToString(CultureInfo.InvariantCulture))throw new ArgumentException("跨表级联已选祖先与实际父链不一致。");
        }
        var rows=await children(index,parent,cancellationToken);
        if(rows.Any(row=>row.Id<=0||index>0&&Parent(row.ParentId)!=parent)||rows.Select(row=>row.Id).Distinct().Count()!=rows.Count)throw new InvalidOperationException("跨表候选超出固定父范围或主键重复。");
        return rows.Select(row=>new CrossTableOption(row.Id.ToString(CultureInfo.InvariantCulture),row.Name??"",row.Id.ToString(CultureInfo.InvariantCulture),row.Name??"")).ToArray();
    }
    /// <summary>完整叶子或中间父均按固定层号逆查；缺失或越权祖先整体拒绝，不返回部分路径。</summary>
    public async Task<CrossTablePath> PathAsync(int deepest,long? id,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();if(deepest<0||deepest>=levels.Length)throw new ArgumentException("跨表级联层号越界。");
        for(var index=0;index<=deepest;index++)authorize(index);
        if(id is null)return new([],"");var path=new CrossTablePathNode[deepest+1];var current=id.Value;
        for(var index=deepest;index>=0;index--)
        {
            cancellationToken.ThrowIfCancellationRequested();if(current<=0)throw new InvalidOperationException("跨表路径主键无效。");
            var row=await find(index,current,cancellationToken);if(row is null||row.Id!=current)throw new InvalidOperationException("跨表级联节点不存在、已删除或不在当前可读范围。");
            path[index]=new(row.Id.ToString(CultureInfo.InvariantCulture),row.Name??"",levels[index].Key);
            if(index>0)current=Parent(row.ParentId)??throw new InvalidOperationException("跨表级联父链缺失。");
        }
        return new(path,string.Join(joiner,path.Select(node=>node.Name).Where(name=>!string.IsNullOrWhiteSpace(name))));
    }
    /// <summary>物理父键必须为正整数文本；根层无需读取或解释父列。</summary>
    private static long? Parent(string? value)=>string.IsNullOrWhiteSpace(value)?null:long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var id)&&id>0?id:throw new InvalidOperationException("跨表级联父键无效。");
}
