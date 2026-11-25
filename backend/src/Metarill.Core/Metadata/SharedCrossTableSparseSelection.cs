using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Metarill.Core.Cascades;
namespace Metarill.Core.Metadata;

/// <summary>同一共享组的一个原控件；槽位固定为根到叶，不保留可变元数据。</summary>
public sealed record SharedCrossTableSelectionBranch(string SourceColumnId,IReadOnlyList<CrossTableSelectionSlot> Slots);
/// <summary>原始稀疏记录中确实保存的节点，字段身份用于区分不同表中的相同数字ID。</summary>
public sealed record SharedCrossTableStoredNode(string ColumnName,long Id);
/// <summary>实际存储槽携带完全相同的文本键或规范整数文本。</summary>
public sealed record SharedCrossTableStoredKey(string ColumnName,string Id);

/// <summary>共享前缀整组编码：保留独立已选末端，删除可由这些末端推导的共同祖先，不逐控件覆盖。</summary>
public sealed class SharedCrossTableSparseSelection
{
    public IReadOnlyList<SharedCrossTableSelectionBranch> Branches{get;}
    public IReadOnlyList<CrossTableSelectionSlot> Slots{get;}
    private readonly FrozenDictionary<string,FrozenSet<string>> ancestors;
    public IReadOnlySet<string> TextKeySlots { get; }
    /// <summary>复制分支与槽位，祖先集合仅来自已经证明相同的公共前缀。</summary>
    private SharedCrossTableSparseSelection(SharedCrossTableSelectionBranch[] branches,IEnumerable<string>? textKeySlots=null)
    {
        Branches=Array.AsReadOnly(branches);Slots=Array.AsReadOnly(branches.SelectMany(b=>b.Slots).DistinctBy(s=>s.ColumnName).OrderBy(s=>s.ColumnName,StringComparer.Ordinal).ToArray());
        TextKeySlots=(textKeySlots??[]).ToFrozenSet(StringComparer.Ordinal);
        if(TextKeySlots.Any(field=>!Slots.Any(slot=>slot.ColumnName==field&&slot.LogicalType=="string")))
            throw new ArgumentException("共享文本业务键要求同层文本存储位。",nameof(textKeySlots));
        ancestors=Slots.ToFrozenDictionary(s=>s.ColumnName,s=>branches.Where(b=>b.Slots.Any(v=>v.ColumnName==s.ColumnName)).SelectMany(b=>b.Slots.TakeWhile(v=>v.ColumnName!=s.ColumnName)).Select(v=>v.ColumnName).ToFrozenSet(StringComparer.Ordinal),StringComparer.Ordinal);
    }
    /// <summary>只签发与指定控件相连的共享组；独立控件继续由原单控件计划负责。</summary>
    public static SharedCrossTableSparseSelection Compile(TableMetadata table,string sourceColumnId,IEnumerable<ColumnMetadata> declared)
    {
        ArgumentNullException.ThrowIfNull(table);ArgumentNullException.ThrowIfNull(declared);if(table.IsRongyu!=0||table.IsFunctionTable!=0||table.IsView!=0)throw new InvalidOperationException("共享稀疏存储要求非冗余实体表。");
        var all=RelationStorageColumns.Expand(table,declared).ToArray();if(all.Any(c=>c.ConfigTableId!=table.Id)||all.Select(c=>c.Id).Distinct(StringComparer.Ordinal).Count()!=all.Length||all.Select(c=>c.ColumnName).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=all.Length)throw new InvalidOperationException("共享稀疏字段归属、ID或列名不唯一。");
        var trees=all.Where(c=>c.ControlKind=="CascadeTree").ToArray();var layouts=trees.ToDictionary(c=>c.Id,c=>CrossTableStorageColumns.LevelPrefixes(c),StringComparer.Ordinal);var raw=trees.ToDictionary(c=>c.Id,RawPrefixes,StringComparer.Ordinal);var source=trees.SingleOrDefault(c=>c.Id==sourceColumnId)??throw new InvalidOperationException("共享稀疏来源控件不存在。");if(layouts[source.Id].Count<2)throw new InvalidOperationException("共享稀疏来源必须是完整固定跨表控件。");
        var group=new List<ColumnMetadata>{source};var remaining=trees.Where(c=>c.Id!=source.Id).ToList();bool changed;do{changed=false;foreach(var other in remaining.ToArray())if(group.Any(c=>raw[c.Id].Overlaps(raw[other.Id]))){group.Add(other);remaining.Remove(other);changed=true;}}while(changed);if(group.Count<2)throw new InvalidOperationException("当前控件不属于共享稀疏组。");
        var members=group.Select(c=>c.Id).ToHashSet(StringComparer.Ordinal);var proofs=new Dictionary<string,(string Identity,string Ancestors)>(StringComparer.Ordinal);var branches=new List<SharedCrossTableSelectionBranch>();
        foreach(var control in group.OrderBy(c=>c.Id,StringComparer.Ordinal))
        {
            if(layouts[control.Id].Count<2)throw new InvalidOperationException("共享同伴缺少完整固定跨表定义。");if(control.IsVirtualColumn!=0||Enabled(control,"isJoinColumn"))throw new InvalidOperationException("共享稀疏来源不能为虚拟或联接字段。");using var json=JsonDocument.Parse(control.ControlDetail);var levels=json.RootElement.GetProperty("levels").EnumerateArray().ToArray();var prefixes=layouts[control.Id];var slots=new List<CrossTableSelectionSlot>();var path=new List<string>();
            for(var index=0;index<prefixes.Count;index++)
            {
                var prefix=prefixes[index];var name=prefix+"_id";var column=all.SingleOrDefault(c=>c.ColumnName==name)??throw new InvalidOperationException("共享稀疏缺少真实ID位。");var type=column.LogicalType.ToLowerInvariant();
                if(column.IsVirtualColumn!=0||Enabled(column,"isJoinColumn")||type is not("string" or "int64")||column.IsDbNotNull!=0||!string.IsNullOrWhiteSpace(column.DefaultValue)||column.ControlKind.Length>0&&!(column.ControlKind=="CascadeTree"&&members.Contains(column.Id)))throw new InvalidOperationException("共享稀疏ID须为可空、无默认值、无其它独立控件的物理标量。");
                var level=levels[index];var identity=JsonSerializer.Serialize(new[]{level.GetProperty("table").GetString(),Text(level,"nameField","name"),index==0?"":Text(level,"parentField",""),Text(level,"idField","id")});var previous=string.Join("\0",path);if(proofs.TryGetValue(name,out var proof)&&(proof.Identity!=identity||proof.Ancestors!=previous))throw new InvalidOperationException("共享稀疏槽的目标、节点键、名称、父键或祖先不一致。");proofs[name]=(identity,previous);path.Add(name+"|"+identity);slots.Add(new(column.Id,name,type));
            }
            branches.Add(new(control.Id,Array.AsReadOnly(slots.ToArray())));
        }
        return new(branches.ToArray());
    }
    /// <summary>按表收集真正共享的组；独立控件不改变其原存储证明，坏共享同伴仍整体拒绝。</summary>
    public static IReadOnlyList<SharedCrossTableSparseSelection> CompileAll(TableMetadata table,IEnumerable<ColumnMetadata> declared)
    {
        ArgumentNullException.ThrowIfNull(table);ArgumentNullException.ThrowIfNull(declared);if(table.IsRongyu!=0||table.IsFunctionTable!=0||table.IsView!=0)return [];var columns=RelationStorageColumns.Expand(table,declared).ToArray();var trees=columns.Where(c=>c.ControlKind=="CascadeTree").ToArray();var raw=trees.ToDictionary(c=>c.Id,RawPrefixes,StringComparer.Ordinal);var seen=new HashSet<string>(StringComparer.Ordinal);var result=new List<SharedCrossTableSparseSelection>();foreach(var tree in trees.OrderBy(c=>c.Id,StringComparer.Ordinal))if(!seen.Contains(tree.Id)&&CrossTableStorageColumns.LevelPrefixes(tree).Count>0&&trees.Any(other=>other.Id!=tree.Id&&raw[tree.Id].Overlaps(raw[other.Id]))){var group=Compile(table,tree.Id,columns);seen.UnionWith(group.Branches.Select(b=>b.SourceColumnId));result.Add(group);}return Array.AsReadOnly(result.ToArray());
    }
    /// <summary>文本槽位来自已发布目标键证明，每个公共槽只能有一个键类型。</summary>
    public SharedCrossTableSparseSelection WithTextKeys(IEnumerable<string> textKeySlots)
    {
        ArgumentNullException.ThrowIfNull(textKeySlots);
        return new(Branches.ToArray(),textKeySlots);
    }

    /// <summary>仅供SQL投影和原始位验证取得分支身份；多个分支不能分别打包后写同一个实体。</summary>
    public CrossTableSparseSelection BranchSelection(string sourceColumnId)
    {
        var branch=Branches.SingleOrDefault(b=>b.SourceColumnId==sourceColumnId)??throw new ArgumentException("控件不属于本共享组。",nameof(sourceColumnId));
        var selection=CrossTableSparseSelection.FromSharedBranch(branch);
        return TextKeySlots.Count==0?selection:selection.WithTextKeys(branch.Slots.Select(slot=>TextKeySlots.Contains(slot.ColumnName)).ToArray());
    }
    /// <summary>输入须先通过真实目标范围与父子路径校验；此处验证共同ID一致并计算最小的独立末端集合。</summary>
    public IReadOnlyDictionary<string,object?> PackValidatedPaths(IReadOnlyDictionary<string,IReadOnlyList<long>> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        RequireNumericPlan();
        return PackValidatedKeys(paths.ToDictionary(pair=>pair.Key,
            pair=>(IReadOnlyList<string>)(pair.Value?.Select(id=>id.ToString(CultureInfo.InvariantCulture)).ToArray()??
                throw new ArgumentException("共享已验证路径无效。",nameof(paths))),StringComparer.Ordinal));
    }

    /// <summary>完整路径统一比较原键，最深独立节点才按各槽的物理类型落库。</summary>
    public IReadOnlyDictionary<string,object?> PackValidatedKeys(IReadOnlyDictionary<string,IReadOnlyList<string>> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);if(paths.Count!=Branches.Count||Branches.Any(b=>!paths.ContainsKey(b.SourceColumnId)))throw new ArgumentException("共享编码必须完整且仅含本组全部控件路径。",nameof(paths));var values=new Dictionary<string,string>(StringComparer.Ordinal);var selected=new HashSet<string>(StringComparer.Ordinal);
        foreach(var branch in Branches)
        {
            var path=paths[branch.SourceColumnId];if(path is null||path.Count>branch.Slots.Count)throw new ArgumentException("共享已验证路径长度或ID无效。",nameof(paths));for(var index=0;index<path.Count;index++){var name=branch.Slots[index].ColumnName;if(!CrossTableReader.ValidKey(path[index],TextKeySlots.Contains(name)))throw new ArgumentException("共享已验证路径键无效。",nameof(paths));if(values.TryGetValue(name,out var id)&&id!=path[index])throw new InvalidOperationException("共享路径包含不同的共同祖先，未编码。");values[name]=path[index];}if(path.Count>0)selected.Add(branch.Slots[path.Count-1].ColumnName);
        }
        var frontier=selected.Where(name=>!selected.Any(other=>other!=name&&ancestors[other].Contains(name))).ToHashSet(StringComparer.Ordinal);var result=Slots.ToDictionary(s=>s.ColumnName,s=>frontier.Contains(s.ColumnName)?s.LogicalType=="string"?(object?)values[s.ColumnName]:CrossTableReader.Long(values[s.ColumnName]):null,StringComparer.Ordinal);return new ReadOnlyDictionary<string,object?>(result);
    }
    /// <summary>恢复独立的已存节点；同一路径多层同时有值属于歧义历史数据，必须拒绝而不是静默去重。</summary>
    public IReadOnlyList<SharedCrossTableStoredNode> ReadStored(IReadOnlyDictionary<string,object?> values)
    {
        RequireNumericPlan();
        return Array.AsReadOnly(ReadStoredKeys(values).Select(node=>new SharedCrossTableStoredNode(node.ColumnName,CrossTableReader.Long(node.Id))).ToArray());
    }

    /// <summary>沿实际槽类型恢复键，原始祖先和后代并存仍是歧义数据。</summary>
    public IReadOnlyList<SharedCrossTableStoredKey> ReadStoredKeys(IReadOnlyDictionary<string,object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);if(values.Count!=Slots.Count||Slots.Any(s=>!values.ContainsKey(s.ColumnName)))throw new ArgumentException("共享存储快照必须完整且仅含本组ID位。",nameof(values));var result=new List<SharedCrossTableStoredKey>();foreach(var slot in Slots)if(ParseStoredKey(slot,values[slot.ColumnName]) is{} id)result.Add(new(slot.ColumnName,id));foreach(var node in result)if(result.Any(other=>other!=node&&ancestors[other.ColumnName].Contains(node.ColumnName)))throw new InvalidOperationException("共享稀疏记录同时保存了祖先与后代，无法无歧义恢复。");return Array.AsReadOnly(result.ToArray());
    }
    /// <summary>从完整快照验证后返回每个控件自己的已存末端；空分支的共同祖先须由组内其它路径恢复。</summary>
    public IReadOnlyDictionary<string,CrossTableSelectedNode?> ReadBranchSelections(IReadOnlyDictionary<string,object?> values)
    {
        RequireNumericPlan();
        return new ReadOnlyDictionary<string,CrossTableSelectedNode?>(ReadBranchKeys(values).ToDictionary(pair=>pair.Key,
            pair=>pair.Value is { } node?new CrossTableSelectedNode(node.Level,CrossTableReader.Long(node.Id)):null,StringComparer.Ordinal));
    }
    /// <summary>每个分支仅消费本组已验证的末端，公共祖先由真实路径恢复。</summary>
    public IReadOnlyDictionary<string,CrossTableSelectedKey?> ReadBranchKeys(IReadOnlyDictionary<string,object?> values)
    {
        var nodes=ReadStoredKeys(values).ToDictionary(n=>n.ColumnName,n=>n.Id,StringComparer.Ordinal);return new ReadOnlyDictionary<string,CrossTableSelectedKey?>(Branches.ToDictionary(b=>b.SourceColumnId,b=>b.Slots.Select((s,index)=>(s,index)).Where(x=>nodes.ContainsKey(x.s.ColumnName)).Select(x=>new CrossTableSelectedKey(x.index,nodes[x.s.ColumnName])).SingleOrDefault(),StringComparer.Ordinal));
    }
    /// <summary>空文本与null仍代表未选，类型和规范正整数不依赖数据库隐式转换。</summary>
    internal string? ParseStoredKey(CrossTableSelectionSlot slot,object? value)
    {
        if(value is null||slot.LogicalType=="string"&&value is "")return null;
        if(slot.LogicalType=="int64"&&value is long number&&number>0)return number.ToString(CultureInfo.InvariantCulture);
        if(slot.LogicalType=="string"&&value is string text&&CrossTableReader.ValidKey(text,TextKeySlots.Contains(slot.ColumnName)))return text;
        throw new InvalidOperationException("共享稀疏主键类型或规范格式无效。");
    }
    private void RequireNumericPlan()
    {
        if(TextKeySlots.Count>0)throw new InvalidOperationException("共享混合级联须使用逐层键接口。");
    }
    /// <summary>先定位共享原始字段，定义损坏的同伴不能因完整解析失败而脱离本组。</summary>
    private static HashSet<string> RawPrefixes(ColumnMetadata source)
    {
        try{using var json=JsonDocument.Parse(source.ControlDetail);if(!json.RootElement.TryGetProperty("levels",out var levels)||levels.ValueKind!=JsonValueKind.Array)return new(StringComparer.OrdinalIgnoreCase);return levels.EnumerateArray().Where(l=>l.ValueKind==JsonValueKind.Object).Select(l=>Text(l,"columnPrefix",Text(l,"table",""))).Where(p=>p.Length>0).ToHashSet(StringComparer.OrdinalIgnoreCase);}catch(Exception error)when(error is JsonException or InvalidOperationException){return new(StringComparer.OrdinalIgnoreCase);}
    }
    /// <summary>结构文本使用与固定层级相同的默认值，不接受任意对象转换。</summary>
    private static string Text(JsonElement value,string key,string fallback)=>!value.TryGetProperty(key,out var field)||field.ValueKind==JsonValueKind.Null?fallback:field.GetString() is{Length:>0} text?text:fallback;
    /// <summary>只沿原整数／布尔开关语义读取联接标志。</summary>
    private static bool Enabled(ColumnMetadata column,string key)=>column.Extra.TryGetValue(key,out var value)&&(value.ValueKind==JsonValueKind.True||value.ToString()=="1");
}
