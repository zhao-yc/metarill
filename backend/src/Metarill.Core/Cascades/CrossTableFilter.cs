using System.Text.Json;
using Metarill.Core.Contracts;
namespace Metarill.Core.Cascades;

/// <summary>生成期固定的原跨表筛选键，名称只回显而不参与谓词。</summary>
public sealed record CrossTableFilterLevel(string IdKey,string NameKey);
/// <summary>最深已选层级与原ID文本；字符串物理列保留前导零比较语义。</summary>
public sealed record CrossTableFilterSelection(int Level,string Id);

/// <summary>原crossTable按最深非空级ID筛选来源列，不读目标、不展开后代或信任请求SQL。</summary>
public sealed class CrossTableFilter
{
    private readonly CrossTableFilterLevel[] levels;
    private readonly HashSet<string> allowed;
    /// <summary>复制固定键并拒绝重复或歧义，外部修改输入数组不能改变已生成查询合同。</summary>
    public CrossTableFilter(IReadOnlyList<CrossTableFilterLevel> levels)
    {
        this.levels=levels.ToArray();var keys=this.levels.SelectMany(level=>new[]{level.IdKey,level.NameKey}).ToArray();allowed=new(keys,StringComparer.Ordinal);
        if(this.levels.Length<2||keys.Any(string.IsNullOrWhiteSpace)||allowed.Count!=keys.Length)throw new ArgumentException("跨表筛选键无效或重复。");
    }
    /// <summary>仅解析实际采用的最深ID，未采用祖先与名称不改变谓词；未知/重复键仍拒绝。</summary>
    public CrossTableFilterSelection? Parse(JsonElement value)
    {
        if(value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined||value.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(value.GetString()))return null;
        if(value.ValueKind!=JsonValueKind.Object)throw new ArgumentException("跨表筛选必须为原层级对象。");
        var seen=new HashSet<string>(StringComparer.Ordinal);foreach(var field in value.EnumerateObject())if(!allowed.Contains(field.Name)||!seen.Add(field.Name))throw new ArgumentException("跨表筛选含重复或未开放字段。");
        for(var index=levels.Length-1;index>=0;index--)
        {
            if(!value.TryGetProperty(levels[index].IdKey,out var id)||id.ValueKind==JsonValueKind.Null||id.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(id.GetString()))continue;
            _=CascadeTreeId.Parse(id,false);return new(index,id.ToString());
        }
        return null;
    }
}
