using System.Globalization;
using System.Text.Json;
using Metarill.Core.Contracts;
namespace Metarill.Core.Cascades;

/// <summary>跨表逐级计划只包含冻结键名，数据库目标与范围由生成的强类型委托决定。</summary>
public sealed record CrossTableLevel(string Key,string IdProperty)
{
    public CrossTableLevel(string key,string idProperty,bool textKey):this(key,idProperty)=>TextKey=textKey;
    public bool TextKey{get;init;}
}
/// <summary>原跨表候选四字段，不增加自关联树专用hasChildren。</summary>
public sealed record CrossTableOption(string Id,string Name,string Value,string Text);
/// <summary>跨表路径保留原levelKey，允许不同表出现相同数字ID。</summary>
public sealed record CrossTablePathNode(string Id,string Name,string LevelKey);
public sealed record CrossTablePath(IReadOnlyList<CrossTablePathNode> Path,string DisplayText);

/// <summary>固定表链的候选及路径；只沿父键逆查，不接受请求提供表、SQL、条件或排序。</summary>
public sealed partial class CrossTableReader
{
    private readonly CrossTableLevel[] levels;
    private readonly Action<int> authorize;
    private readonly Func<int,string,CancellationToken,Task<TextCascadeTreeNode?>> find;
    private readonly Func<int,string?,CancellationToken,Task<IReadOnlyList<TextCascadeTreeNode>>> children;
    private readonly string joiner;
    private readonly Func<int,IReadOnlyList<string>,CancellationToken,Task<IReadOnlyList<TextCascadeTreeNode>>>? findMany;
    /// <summary>复制冻结层级，禁止重复键与歧义JSON字段；授权委托即使空请求也必须执行。</summary>
    public CrossTableReader(IReadOnlyList<CrossTableLevel> levels,Action<int> authorize,
        Func<int,long,CancellationToken,Task<CascadeTreeNode?>> find,
        Func<int,long?,CancellationToken,Task<IReadOnlyList<CascadeTreeNode>>> children,string joiner)
        :this(levels,authorize,find,children,joiner,null){}
    /// <summary>附加固定层批读委托；保留旧构造签名及单路径、待写来源覆盖行为。</summary>
    public CrossTableReader(IReadOnlyList<CrossTableLevel> levels,Action<int> authorize,
        Func<int,long,CancellationToken,Task<CascadeTreeNode?>> find,
        Func<int,long?,CancellationToken,Task<IReadOnlyList<CascadeTreeNode>>> children,string joiner,
        Func<int,IReadOnlyList<long>,CancellationToken,Task<IReadOnlyList<CascadeTreeNode>>>? findMany)
        :this(levels,authorize,
            async (index,key,ct)=>await find(index,Long(key),ct) is {} node?Adapt(node):null,
            async (index,key,ct)=>(await children(index,key is null?null:Long(key),ct)).Select(Adapt).ToArray(),
            joiner,findMany is null?null:async (index,keys,ct)=>
                (await findMany(index,keys.Select(Long).ToArray(),ct)).Select(Adapt).ToArray()){}

    /// <summary>按固定层识别整数或文本键，混合链共用原候选、授权及父链验证。</summary>
    public static CrossTableReader WithTextKeys(IReadOnlyList<CrossTableLevel> levels,Action<int> authorize,
        Func<int,string,CancellationToken,Task<TextCascadeTreeNode?>> find,
        Func<int,string?,CancellationToken,Task<IReadOnlyList<TextCascadeTreeNode>>> children,string joiner,
        Func<int,IReadOnlyList<string>,CancellationToken,Task<IReadOnlyList<TextCascadeTreeNode>>>? findMany)=>
        new(levels,authorize,find,children,joiner,findMany);

    private CrossTableReader(IReadOnlyList<CrossTableLevel> levels,Action<int> authorize,
        Func<int,string,CancellationToken,Task<TextCascadeTreeNode?>> find,
        Func<int,string?,CancellationToken,Task<IReadOnlyList<TextCascadeTreeNode>>> children,string joiner,
        Func<int,IReadOnlyList<string>,CancellationToken,Task<IReadOnlyList<TextCascadeTreeNode>>>? findMany)
    {
        this.levels=levels.ToArray();this.authorize=authorize;this.find=find;this.children=children;this.joiner=joiner;this.findMany=findMany;
        if(this.levels.Length<2||this.levels.Any(level=>string.IsNullOrWhiteSpace(level.Key)||string.IsNullOrWhiteSpace(level.IdProperty))||this.levels.Select(level=>level.Key).Distinct(StringComparer.Ordinal).Count()!=this.levels.Length||this.levels.Select(level=>level.IdProperty).Distinct(StringComparer.Ordinal).Count()!=this.levels.Length)throw new ArgumentException("跨表级联层级无效或重复。");
    }
    /// <summary>原selectedPath允许全部已知层级ID；父缺省返回空，给出父时逐跳校验可读链及已选祖先一致性。</summary>
    public async Task<IReadOnlyList<CrossTableOption>> OptionsAsync(string? levelKey,JsonElement selectedPath,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();var index=Array.FindIndex(levels,level=>level.Key==levelKey);if(index<0)throw new ArgumentException("未知跨表级联层标识。");authorize(index);
        // 原Controller将省略/null转换为空对象；其它类型仍明确拒绝。
        if(selectedPath.ValueKind is not(JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined))throw new ArgumentException("selectedPath必须为对象。");
        var selected=new Dictionary<string,string?>(StringComparer.Ordinal);
        if(selectedPath.ValueKind==JsonValueKind.Object)foreach(var field in selectedPath.EnumerateObject())if(levels.SingleOrDefault(level=>level.IdProperty==field.Name) is not {} entry||!selected.TryAdd(field.Name,entry.TextKey?CascadeTreeId.ParseText(field.Value,true):CascadeTreeId.Parse(field.Value,true)?.ToString(CultureInfo.InvariantCulture)))throw new ArgumentException("跨表级联选中路径含未知或重复字段。");
        string? parent=null;
        if(index>0)
        {
            parent=selected.GetValueOrDefault(levels[index-1].IdProperty);if(parent is null)return [];
            var path=await PathByKeyAsync(index-1,parent,cancellationToken);
            for(var ancestor=0;ancestor<index;ancestor++)if(selected.GetValueOrDefault(levels[ancestor].IdProperty) is{} supplied&&path.Path[ancestor].Id!=supplied)throw new ArgumentException("跨表级联已选祖先与实际父链不一致。");
        }
        var rows=await children(index,parent,cancellationToken);
        if(rows.Any(row=>!ValidKey(row.Key,levels[index].TextKey)||index>0&&Parent(index-1,row.ParentKey)!=parent)||rows.Select(row=>row.Key).Distinct(StringComparer.Ordinal).Count()!=rows.Count)throw new InvalidOperationException("跨表候选超出固定父范围或主键重复。");
        return rows.Select(row=>new CrossTableOption(row.Key,row.Name??"",row.Key,row.Name??"")).ToArray();
    }
    /// <summary>完整叶子或中间父均按固定层号逆查；缺失或越权祖先整体拒绝，不返回部分路径。</summary>
    public Task<CrossTablePath> PathAsync(int deepest,long? id,CancellationToken cancellationToken=default)=>
        PathByKeyAsync(deepest,id?.ToString(CultureInfo.InvariantCulture),cancellationToken);

    /// <summary>按层验证业务键，不将文本键转为数字或修剪。</summary>
    public async Task<CrossTablePath> PathByKeyAsync(int deepest,string? id,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();if(deepest<0||deepest>=levels.Length)throw new ArgumentException("跨表级联层号越界。");
        for(var index=0;index<=deepest;index++)authorize(index);
        if(id is null)return new([],"");var path=new CrossTablePathNode[deepest+1];var current=id;
        for(var index=deepest;index>=0;index--)
        {
            cancellationToken.ThrowIfCancellationRequested();if(!ValidKey(current,levels[index].TextKey))throw new InvalidOperationException("跨表路径主键无效。");
            var row=await find(index,current,cancellationToken);if(row is null||row.Key!=current)throw new InvalidOperationException("跨表级联节点不存在、已删除或不在当前可读范围。");
            path[index]=new(row.Key,row.Name??"",levels[index].Key);
            if(index>0)current=Parent(index-1,row.ParentKey)??throw new InvalidOperationException("跨表级联父链缺失。");
        }
        return new(path,string.Join(joiner,path.Select(node=>node.Name).Where(name=>!string.IsNullOrWhiteSpace(name))));
    }
    /// <summary>整数层沿用旧物理父键的前导零解析，文本层保留原字节。</summary>
    private string? Parent(int level,string? value)=>string.IsNullOrWhiteSpace(value)?null:
        levels[level].TextKey?value:long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var id)&&id>0
            ?id.ToString(CultureInfo.InvariantCulture):throw new InvalidOperationException("跨表级联父键无效。");

    internal static bool ValidKey(string key,bool text)=>text?!string.IsNullOrWhiteSpace(key):
        long.TryParse(key,NumberStyles.None,CultureInfo.InvariantCulture,out var value)&&value>0&&key==value.ToString(CultureInfo.InvariantCulture);

    internal static long Long(string key)=>long.Parse(key,NumberStyles.None,CultureInfo.InvariantCulture);
    internal static TextCascadeTreeNode Adapt(CascadeTreeNode row)=>new(row.Id.ToString(CultureInfo.InvariantCulture),row.Id,row.Name,row.ParentId);
}
