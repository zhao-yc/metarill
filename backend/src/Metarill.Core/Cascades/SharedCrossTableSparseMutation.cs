using System.Collections.Frozen;
using System.Collections.ObjectModel;
using Metarill.Core.Metadata;
namespace Metarill.Core.Cascades;

/// <summary>一次共享R0变更的原始／逻辑状态；每个请求和组分别创建，不跨请求缓存目标路径。</summary>
public sealed class SharedCrossTableSparseMutation
{
    private readonly SharedCrossTableSparseSelection selection;
    private readonly SharedCrossTableCoordinator coordinator;
    private readonly IReadOnlyDictionary<string,object?> raw;
    private readonly FrozenSet<string> fields;
    private IReadOnlyDictionary<string,object?>? logical,expected,packed;
    private bool hydrated,normalized,writesSelection;
    /// <summary>省略组在持久化阶段恢复原始位；逻辑值只用于校验，不冒充Entity的已存值。</summary>
    public IReadOnlyDictionary<string,object?> OriginalStoredIds{get;}
    /// <summary>复制原始组快照并证明协调器与存储计划属于完全相同的字段和类型。</summary>
    public SharedCrossTableSparseMutation(SharedCrossTableSparseSelection selection,SharedCrossTableCoordinator coordinator,IReadOnlyDictionary<string,object?> original)
    {
        ArgumentNullException.ThrowIfNull(selection);ArgumentNullException.ThrowIfNull(coordinator);ArgumentNullException.ThrowIfNull(original);this.selection=selection;this.coordinator=coordinator;
        var branches=coordinator.Branches.ToDictionary(b=>b.ControlId,StringComparer.Ordinal);if(branches.Count!=selection.Branches.Count)throw new ArgumentException("共享协调和存储组的控件不一致。");foreach(var branch in selection.Branches){if(!branches.TryGetValue(branch.SourceColumnId,out var other)||branch.Slots.Count!=other.Levels.Count||branch.Slots.Where((s,i)=>s.ColumnName!=other.Levels[i].IdColumn||s.LogicalType!=other.Levels[i].LogicalType).Any())throw new ArgumentException("共享协调和存储层位不一致。");}
        fields=selection.Slots.SelectMany(s=>new[]{s.ColumnName,s.ColumnName[..^3]+"_name"}).ToFrozenSet(StringComparer.Ordinal);RequireFields(original);raw=Freeze(original);OriginalStoredIds=IdValues(raw);
    }
    /// <summary>部分请求合并之前恢复逻辑路径，实际选择位只读取一次；目标回调必须加入当前事务持锁。</summary>
    public async Task<IReadOnlyDictionary<string,object?>> HydrateAsync(Func<string,IReadOnlyDictionary<string,object?>,CancellationToken,Task<IReadOnlyDictionary<string,object?>>> readOriginal,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();if(normalized)throw new InvalidOperationException("共享变更已经归一化，不能再次恢复原值。");logical??=await ReadLogicalAsync(raw,readOriginal,token);hydrated=true;return logical;
    }
    /// <summary>钩子后的真实变化在原逻辑路径上合并；整组省略不读取，明确全清不要求旧目标仍然存在。</summary>
    public async Task<SharedCrossTableResult> NormalizeAsync(IReadOnlyDictionary<string,object?> current,IReadOnlySet<string> submitted,bool creating,
        Func<string,IReadOnlyDictionary<string,object?>,CancellationToken,Task<IReadOnlyDictionary<string,object?>>> readCurrent,
        Func<string,IReadOnlyDictionary<string,object?>,CancellationToken,Task<IReadOnlyDictionary<string,object?>>> readOriginal,CancellationToken token=default,bool restoreForDependencies=false)
    {
        ArgumentNullException.ThrowIfNull(submitted);ArgumentNullException.ThrowIfNull(readCurrent);ArgumentNullException.ThrowIfNull(readOriginal);token.ThrowIfCancellationRequested();RequireFields(current);if(normalized)throw new InvalidOperationException("共享变更不能重复执行归一化。");var baseline=hydrated?logical!:raw;var needed=creating||fields.Any(f=>submitted.Contains(f)||!Equals(current[f],baseline[f]));
        if(!needed)
        {
            // 其它选择器校验依赖逻辑祖先时只恢复和固定预期，不把省略的原始关系重新编码。
            if(restoreForDependencies){logical??=await ReadLogicalAsync(raw,readOriginal,token);expected=Canonical(logical);packed=IdValues(raw);hydrated=true;}normalized=true;return new(restoreForDependencies?logical!:Freeze(current),FrozenSet<string>.Empty);
        }
        var merged=current.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);var original=raw;
        if(!creating&&!selection.Slots.All(s=>submitted.Contains(s.ColumnName)&&Empty(current[s.ColumnName])))
        {
            logical??=await ReadLogicalAsync(raw,readOriginal,token);original=logical;foreach(var field in fields)if(!submitted.Contains(field)&&Equals(current[field],baseline[field]))merged[field]=logical[field];
        }
        var result=await coordinator.NormalizeAsync(merged,original,submitted,creating,readCurrent,token);expected=Canonical(result.Values);writesSelection=true;normalized=true;return result;
    }
    /// <summary>只打包一次整组预期路径；未触及的组返回null，不能把原始空祖先位当作逻辑值再编码。</summary>
    public IReadOnlyDictionary<string,object?>? Pack()
    {
        if(!normalized)throw new InvalidOperationException("共享变更须先归一化再编码。");if(!writesSelection)return null;if(packed is not null)return packed;
        // 只有已归一化且确实写入的组才打包，预期快照缺失时明确拒绝，不能带空状态继续。
        var expectedValues=expected??throw new InvalidOperationException("共享变更缺少归一化预期，不能打包。");var paths=new Dictionary<string,IReadOnlyList<long>>(StringComparer.Ordinal);
        foreach(var branch in selection.Branches){var path=new List<long>();var empty=false;foreach(var slot in branch.Slots){var id=SharedCrossTableSparseSelection.ParseStoredId(slot,expectedValues[slot.ColumnName]);if(id is null){empty=true;continue;}if(empty)throw new InvalidOperationException("共享逻辑路径存在空缺层级。");path.Add(id.Value);}paths.Add(branch.SourceColumnId,path);}return packed=selection.PackValidatedPaths(paths);
    }
    /// <summary>正常DML后仅核验；原始位及实际完整路径均须与同笔写入的预期相符，不补写修复。</summary>
    public async Task VerifyStoredAsync(IReadOnlyDictionary<string,object?> stored,
        Func<string,IReadOnlyDictionary<string,object?>,CancellationToken,Task<IReadOnlyDictionary<string,object?>>> readStored,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(readStored);token.ThrowIfCancellationRequested();if(!normalized)throw new InvalidOperationException("共享变更尚未归一化。");var actual=IdValues(stored);_=selection.ReadStored(actual);var wanted=packed??IdValues(raw);if(selection.Slots.Any(s=>!Equals(actual[s.ColumnName],wanted[s.ColumnName])))throw new InvalidOperationException("共享稀疏ID在存储后发生变化，未提交。");
        if(expected is null)return;if(packed is null)throw new InvalidOperationException("共享预期路径尚未整组打包。");var current=await ReadLogicalAsync(stored,readStored,token);if(fields.Any(f=>!Equals(current[f],expected[f])))throw new InvalidOperationException("共享实际路径或名称与写入预期不一致，未提交。");
    }
    /// <summary>只沿真实独立末端恢复路径；共同物理选择位不重复查询，短控件祖先从另一分支补齐。</summary>
    private async Task<IReadOnlyDictionary<string,object?>> ReadLogicalAsync(IReadOnlyDictionary<string,object?> source,
        Func<string,IReadOnlyDictionary<string,object?>,CancellationToken,Task<IReadOnlyDictionary<string,object?>>> read,CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(read);var selected=selection.ReadBranchSelections(IdValues(source));var visited=new HashSet<string>(StringComparer.Ordinal);var result=fields.ToDictionary(f=>f,_=>(object?)null,StringComparer.Ordinal);
        foreach(var branch in selection.Branches)
        {
            token.ThrowIfCancellationRequested();if(selected[branch.SourceColumnId] is not{} node||!visited.Add(branch.Slots[node.Level].ColumnName))continue;var names=branch.Slots.SelectMany(s=>new[]{s.ColumnName,s.ColumnName[..^3]+"_name"}).ToArray();var input=names.ToDictionary(f=>f,f=>source.TryGetValue(f,out var value)?value:null,StringComparer.Ordinal);var values=await read(branch.SourceColumnId,new ReadOnlyDictionary<string,object?>(input),token);if(values.Count!=names.Length||names.Any(f=>!values.ContainsKey(f)))throw new InvalidOperationException("共享路径回调返回未知或缺失字段。");
            for(var i=0;i<branch.Slots.Count;i++)
            {
                var slot=branch.Slots[i];var id=SharedCrossTableSparseSelection.ParseStoredId(slot,values[slot.ColumnName]);var name=slot.ColumnName[..^3]+"_name";if(values[name] is not(null or string)||i<=node.Level&&id is null||i>node.Level&&id is not null||i==node.Level&&id!=node.Id)throw new InvalidOperationException("共享路径回调没有保持完整的实际选择层位。");if(id is null)continue;if(result[slot.ColumnName] is not null&&(!Equals(result[slot.ColumnName],values[slot.ColumnName])||!Equals(result[name],values[name])))throw new InvalidOperationException("共享已存独立末端的实际祖先路径冲突。");result[slot.ColumnName]=values[slot.ColumnName];result[name]=values[name];
            }
        }
        return Freeze(result);
    }
    /// <summary>只取得固定ID位，名称不属于原始存储合同。</summary>
    private IReadOnlyDictionary<string,object?> IdValues(IReadOnlyDictionary<string,object?> values)
    {
        if(selection.Slots.Any(s=>!values.ContainsKey(s.ColumnName)))throw new ArgumentException("共享原始ID快照不完整。");return new ReadOnlyDictionary<string,object?>(selection.Slots.ToDictionary(s=>s.ColumnName,s=>values[s.ColumnName],StringComparer.Ordinal));
    }
    /// <summary>逻辑未选统一为空，与R0 SQL投影和原单控件稀疏合同保持一致。</summary>
    private IReadOnlyDictionary<string,object?> Canonical(IReadOnlyDictionary<string,object?> values)
    {
        var result=values.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);foreach(var slot in selection.Slots)if(Empty(result[slot.ColumnName])){result[slot.ColumnName]=null;result[slot.ColumnName[..^3]+"_name"]=null;}return Freeze(result);
    }
    /// <summary>只接收本固定组的完整字段，回调不能扩展业务写入白名单。</summary>
    private void RequireFields(IReadOnlyDictionary<string,object?> values){ArgumentNullException.ThrowIfNull(values);if(values.Count!=fields.Count||fields.Any(f=>!values.ContainsKey(f)))throw new ArgumentException("共享变更快照必须完整且仅含本组字段。");}
    /// <summary>每个状态都复制成只读快照，后续实体赋值不改变原值或预期。</summary>
    private static IReadOnlyDictionary<string,object?> Freeze(IReadOnlyDictionary<string,object?> values)=>new ReadOnlyDictionary<string,object?>(values.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal));
    /// <summary>不把0、空白或任意对象当作未选。</summary>
    private static bool Empty(object? value)=>value is null or "";
}
