using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Globalization;
using Metarill.Core.Metadata;
namespace Metarill.Core.Cascades;

/// <summary>共享槽位的冻结身份；范围指纹由发布编译器产生，不接收客户端目录。</summary>
public sealed record SharedCrossTableSlot(string IdColumn,string NameColumn,string LogicalType,string ScopeKey);
/// <summary>一个原级联控件，层级顺序仍是根到叶。</summary>
public sealed record SharedCrossTableBranch(string ControlId,IReadOnlyList<SharedCrossTableSlot> Levels);
/// <summary>最终组值与确实参与本次写入的字段；省略整组返回空字段集。</summary>
public sealed record SharedCrossTableResult(IReadOnlyDictionary<string,object?> Values,IReadOnlySet<string> Fields);

/// <summary>共享上级的级联先分别校验真实路径，再原子合并；不按控件顺序覆盖冲突选择。</summary>
public sealed class SharedCrossTableCoordinator
{
    public IReadOnlyList<SharedCrossTableBranch> Branches{get;}
    private readonly FrozenDictionary<string,SharedCrossTableSlot> slots;
    private readonly FrozenDictionary<string,int> uses;
    private readonly FrozenSet<string> fields;
    /// <summary>复制输入并证明公共槽是相同前缀路径；仅接受一个相连控件组。</summary>
    public SharedCrossTableCoordinator(IReadOnlyList<SharedCrossTableBranch> branches)
    {
        ArgumentNullException.ThrowIfNull(branches);var copy=branches.Select(b=>b with{Levels=Array.AsReadOnly(b.Levels.ToArray())}).OrderBy(b=>b.ControlId,StringComparer.Ordinal).ToArray();
        if(copy.Length<2||copy.Any(b=>string.IsNullOrWhiteSpace(b.ControlId)||b.Levels.Count<2)||copy.Select(b=>b.ControlId).Distinct(StringComparer.Ordinal).Count()!=copy.Length)throw new ArgumentException("共享级联组的控件标识或层级无效。",nameof(branches));
        var grouped=copy.SelectMany(b=>b.Levels).GroupBy(s=>s.IdColumn,StringComparer.Ordinal).ToArray();var all=new Dictionary<string,SharedCrossTableSlot>(StringComparer.Ordinal);
        foreach(var group in grouped)
        {
            var first=group.First();MetadataValidation.RequireIdentifier(first.IdColumn);MetadataValidation.RequireIdentifier(first.NameColumn);
            if(first.LogicalType is not("string" or "int64")||string.IsNullOrWhiteSpace(first.ScopeKey)||!first.IdColumn.EndsWith("_id",StringComparison.Ordinal)||first.NameColumn!=first.IdColumn[..^3]+"_name"||group.Any(s=>s!=first))throw new ArgumentException("共享ID、名称、类型或读取范围不一致。",nameof(branches));all.Add(group.Key,first);
            var paths=copy.Where(b=>b.Levels.Any(s=>s.IdColumn==group.Key)).Select(b=>string.Join("\0",b.Levels.TakeWhile(s=>s.IdColumn!=group.Key).Select(s=>s.IdColumn+"|"+s.ScopeKey))).Distinct(StringComparer.Ordinal).ToArray();if(paths.Length!=1)throw new ArgumentException("共享层的祖先路径不一致。",nameof(branches));
        }
        foreach(var b in copy)if(b.Levels.Select(s=>s.IdColumn).Distinct(StringComparer.Ordinal).Count()!=b.Levels.Count)throw new ArgumentException("同一控件不能重复同一个存储槽。",nameof(branches));
        if(all.Keys.Concat(all.Values.Select(s=>s.NameColumn)).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=all.Count*2)throw new ArgumentException("共享组字段存在歧义。",nameof(branches));
        var connected=new HashSet<string>(StringComparer.Ordinal){copy[0].ControlId};bool changed;do{changed=false;foreach(var b in copy.Where(b=>!connected.Contains(b.ControlId)))if(copy.Where(other=>connected.Contains(other.ControlId)).Any(other=>other.Levels.Any(s=>b.Levels.Any(t=>t.IdColumn==s.IdColumn))))changed|=connected.Add(b.ControlId);}while(changed);if(connected.Count!=copy.Length)throw new ArgumentException("不同独立级联不能混为一个共享组。",nameof(branches));
        Branches=Array.AsReadOnly(copy);slots=all.ToFrozenDictionary(StringComparer.Ordinal);uses=grouped.ToFrozenDictionary(g=>g.Key,g=>g.Count(),StringComparer.Ordinal);fields=all.Keys.Concat(all.Values.Select(s=>s.NameColumn)).ToFrozenSet(StringComparer.Ordinal);
    }
    /// <summary>对已捕获的原值和钩子后新值协调；回调必须使用当前事务／身份完成真正路径校验。</summary>
    public async Task<SharedCrossTableResult> NormalizeAsync(IReadOnlyDictionary<string,object?> current,IReadOnlyDictionary<string,object?> original,IReadOnlySet<string> submitted,bool creating,
        Func<string,IReadOnlyDictionary<string,object?>,CancellationToken,Task<IReadOnlyDictionary<string,object?>>> normalize,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(current);ArgumentNullException.ThrowIfNull(original);ArgumentNullException.ThrowIfNull(submitted);ArgumentNullException.ThrowIfNull(normalize);cancellationToken.ThrowIfCancellationRequested();
        if(current.Count!=fields.Count||fields.Any(f=>!current.ContainsKey(f))||!creating&&fields.Any(f=>!original.ContainsKey(f)))throw new ArgumentException("共享组快照必须含全部真实槽位。");
        var state=current.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);var requested=submitted.ToFrozenSet(StringComparer.Ordinal);var changes=slots.Keys.Where(id=>creating?!Empty(state[id]):!SameNode(state[id],original[id])).ToFrozenSet(StringComparer.Ordinal);var touched=new HashSet<string>(StringComparer.Ordinal);
        bool Needed(SharedCrossTableBranch b)=>creating||b.Levels.Any(s=>requested.Contains(s.IdColumn)||requested.Contains(s.NameColumn)||changes.Contains(s.IdColumn)||!creating&&!Equals(state[s.NameColumn],original[s.NameColumn]));
        var needed=Branches.Where(Needed).Select(b=>b.ControlId).ToHashSet(StringComparer.Ordinal);if(needed.Count==0)return Result();
        var protectedIds=changes.Where(id=>!Empty(state[id])).ToFrozenSet(StringComparer.Ordinal);
        // 显式改上级但没有同时选新下级时，旧下级不能把新上级归一化回去。
        foreach(var b in Branches)for(var index=0;index<b.Levels.Count;index++)if(changes.Contains(b.Levels[index].IdColumn)&&!b.Levels.Skip(index+1).Any(s=>protectedIds.Contains(s.IdColumn)))foreach(var child in b.Levels.Skip(index+1))Clear(child,protectedIds);
        var seeds=state.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);
        bool ChangedSelection(SharedCrossTableBranch b)=>b.Levels.Any(s=>!Empty(seeds[s.IdColumn])&&changes.Contains(s.IdColumn)&&(s==b.Levels[^1]||uses[s.IdColumn]==1));
        var authority=Branches.Where(ChangedSelection).ToArray();if(authority.Length==0)authority=Branches.Where(b=>needed.Contains(b.ControlId)&&b.Levels.Any(s=>!Empty(seeds[s.IdColumn]))).ToArray();var authorityIds=authority.Select(b=>b.ControlId).ToFrozenSet(StringComparer.Ordinal);
        var authoritative=new Dictionary<string,object?>(StringComparer.Ordinal);var shared=new Dictionary<string,(string? Id,object? Name)>(StringComparer.Ordinal);
        // 全部主动选择使用同一未改写快照；后校验的控件不能吃掉前一个显式输入。
        foreach(var b in authority){var values=await Read(b,seeds);Merge(b,values,authoritative,shared);}
        var protectedByAuthority=authoritative.Keys.Where(slots.ContainsKey).ToFrozenSet(StringComparer.Ordinal);
        foreach(var pair in authoritative.Where(x=>slots.ContainsKey(x.Key)&&uses[x.Key]>1&&!SameNode(state[x.Key],x.Value)))foreach(var b in Branches.Where(b=>!authorityIds.Contains(b.ControlId))){var index=Index(b,pair.Key);if(index>=0)foreach(var child in b.Levels.Skip(index+1))Clear(child,protectedByAuthority);}
        Apply(authoritative);
        var weakSeed=state.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);var weak=new Dictionary<string,object?>(StringComparer.Ordinal);
        foreach(var b in Branches.Where(b=>!authorityIds.Contains(b.ControlId)))if(needed.Contains(b.ControlId)||b.Levels.Any(s=>!SameNode(seeds[s.IdColumn],state[s.IdColumn])))Merge(b,await Read(b,weakSeed),weak,shared);
        Apply(weak);return Result();

        /// <summary>只有读取回调校验后的规范ID／名称可进入合并，不接收额外字段。</summary>
        async Task<IReadOnlyDictionary<string,object?>> Read(SharedCrossTableBranch b,IReadOnlyDictionary<string,object?> seed)
        {
            cancellationToken.ThrowIfCancellationRequested();var input=b.Levels.SelectMany(s=>new[]{s.IdColumn,s.NameColumn}).ToDictionary(f=>f,f=>seed[f],StringComparer.Ordinal);var value=await normalize(b.ControlId,new ReadOnlyDictionary<string,object?>(input),cancellationToken);if(value.Count!=input.Count||input.Keys.Any(f=>!value.ContainsKey(f)))throw new InvalidOperationException("共享级联路径回调返回未知或缺失字段。");
            foreach(var s in b.Levels){_=Key(s,value[s.IdColumn]);if(value[s.NameColumn] is not(null or string))throw new InvalidOperationException("共享级联名称必须为字符串。");}return value;
        }
        /// <summary>同一冻结槽必须指向同一实际节点，两个主动分支冲突时整组拒绝。</summary>
        void Merge(SharedCrossTableBranch b,IReadOnlyDictionary<string,object?> values,Dictionary<string,object?> target,Dictionary<string,(string? Id,object? Name)> known)
        {
            foreach(var s in b.Levels){if(uses[s.IdColumn]>1){var id=Key(s,values[s.IdColumn]);var name=values[s.NameColumn];if(known.TryGetValue(s.IdColumn,out var old)&&(old.Id!=id||!Equals(old.Name,name)))throw new InvalidOperationException("共享级联的多个主动路径不一致，未保存。");known[s.IdColumn]=(id,name);}target[s.IdColumn]=values[s.IdColumn];target[s.NameColumn]=values[s.NameColumn];}
        }
        /// <summary>只清没有被新的主动路径保护的下级，保持字符串空值／null与整数null的原合同。</summary>
        void Clear(SharedCrossTableSlot s,IReadOnlySet<string> protectedFields){if(protectedFields.Contains(s.IdColumn))return;var value=s.LogicalType=="string"&&state[s.IdColumn] is not null?(object)"":null;state[s.IdColumn]=value;state[s.NameColumn]=value is null?null:"";touched.Add(s.IdColumn);touched.Add(s.NameColumn);}
        /// <summary>结果收集后才写本地组状态，不在任一路径回调里写共享实体。</summary>
        void Apply(IReadOnlyDictionary<string,object?> values){foreach(var (key,value) in values){state[key]=value;touched.Add(key);}}
        /// <summary>返回独立不可变快照，不能由调用方回写冻结规则或上一笔结果。</summary>
        SharedCrossTableResult Result()=>new(new ReadOnlyDictionary<string,object?>(state),touched.ToFrozenSet(StringComparer.Ordinal));
    }
    /// <summary>仅比较节点含义；null和空字符串都代表未选中，实际输出仍保留各自形状。</summary>
    private static bool SameNode(object? left,object? right)=>Empty(left)&&Empty(right)||Equals(left,right);
    /// <summary>不将0、空白或任意对象当作空关联。</summary>
    private static bool Empty(object? value)=>value is null or "";
    /// <summary>定位冻结层，不从请求字段猜层号。</summary>
    private static int Index(SharedCrossTableBranch b,string id){for(var i=0;i<b.Levels.Count;i++)if(b.Levels[i].IdColumn==id)return i;return -1;}
    /// <summary>真实路径的回调结果必须满足原正整数ID合同。</summary>
    private static string? Key(SharedCrossTableSlot slot,object? value)
    {
        if(Empty(value))return null;if(slot.LogicalType=="int64"&&value is long number&&number>0)return number.ToString(CultureInfo.InvariantCulture);if(slot.LogicalType=="string"&&value is string text&&long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var key)&&key>0&&text==key.ToString(CultureInfo.InvariantCulture))return text;throw new InvalidOperationException("共享级联返回了非规范ID。");
    }
}
