using System.Collections.ObjectModel;
using System.Globalization;

namespace Metarill.Core.Metadata;

/// <summary>固定层级的独立存储位；仅持有冻结标量，不公开可变元数据对象。</summary>
public sealed record CrossTableSelectionSlot(string ColumnId,string ColumnName,string LogicalType);
/// <summary>同一数字ID在不同目标表中通过层号区分，不能仅靠ID推断所选层级。</summary>
public sealed record CrossTableSelectedNode(int Level,long Id);

/// <summary>非冗余跨表选择采用稀疏ID位：只存最深所选节点，其余ID位和所有名称均不缓存路径。</summary>
public sealed class CrossTableSparseSelection
{
    private CrossTableSparseSelection(string sourceColumnId,CrossTableSelectionSlot[] slots)
    {SourceColumnId=sourceColumnId;Slots=Array.AsReadOnly(slots);}
    public string SourceColumnId{get;}
    public IReadOnlyList<CrossTableSelectionSlot> Slots{get;}
    /// <summary>只能由已证明的整组计划创建分支读取身份；共享写入必须仍调用整组编码。
    /// </summary>
    internal static CrossTableSparseSelection FromSharedBranch(SharedCrossTableSelectionBranch branch)=>new(branch.SourceColumnId,branch.Slots.ToArray());

    /// <summary>与结构扩展共用固定层级，显式虚拟、联接、独立控件或非空约束均不能被自动列覆盖。</summary>
    public static CrossTableSparseSelection Compile(TableMetadata table,string sourceColumnId,IEnumerable<ColumnMetadata> declared)
    {
        ArgumentNullException.ThrowIfNull(table);ArgumentNullException.ThrowIfNull(declared);
        if(table.IsRongyu!=0||table.IsFunctionTable!=0||table.IsView!=0)throw new InvalidOperationException("稀疏级联存储仅适用于非冗余实体表。");
        var columns=RelationStorageColumns.Expand(table,declared).ToArray();
        if(columns.Any(column=>column.ConfigTableId!=table.Id)||columns.Select(column=>column.Id).Distinct(StringComparer.Ordinal).Count()!=columns.Length||columns.Select(column=>column.ColumnName).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=columns.Length)throw new InvalidOperationException("稀疏级联字段归属、ID或列名不唯一。");
        var source=columns.SingleOrDefault(column=>column.Id==sourceColumnId)??throw new InvalidOperationException("稀疏级联来源字段不存在。");
        if(source.ControlKind!="CascadeTree"||source.IsVirtualColumn!=0||Enabled(source,"isJoinColumn"))throw new InvalidOperationException("稀疏级联来源须为实体级联ID。");
        var prefixes=CrossTableStorageColumns.LevelPrefixes(source);if(prefixes.Count<2)throw new InvalidOperationException("稀疏级联须有完整固定跨表层级。");
        var slots=new List<CrossTableSelectionSlot>();
        foreach(var prefix in prefixes)
        {
            var column=columns.SingleOrDefault(column=>column.ColumnName==prefix+"_id")??throw new InvalidOperationException("稀疏级联缺少明确的层级ID位。");
            var type=column.LogicalType.ToLowerInvariant();
            if(column.IsVirtualColumn!=0||Enabled(column,"isJoinColumn")||type is not("string" or "int64")||column.IsDbNotNull!=0||!string.IsNullOrWhiteSpace(column.DefaultValue)||column.Id!=source.Id&&!string.IsNullOrWhiteSpace(column.ControlKind))throw new InvalidOperationException("稀疏级联各ID位须为可空、无默认值的字符串／int64物理列，且无独立控件。");
            slots.Add(new(column.Id,column.ColumnName,type));
        }
        var names=slots.Select(slot=>slot.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var other in columns.Where(column=>column.ControlKind=="CascadeTree"&&column.Id!=source.Id))
            if(CrossTableStorageColumns.LevelPrefixes(other).Any(prefix=>names.Contains(prefix+"_id")))throw new InvalidOperationException("不同跨表级联共享ID位仍需聚合存储证明。");
        return new(source.Id,slots.ToArray());
    }

    /// <summary>调用者须先通过固定目标范围校验完整路径；此方法只编码已验证路径，不校验数据库父子关系。</summary>
    public IReadOnlyDictionary<string,object?> PackValidatedPath(IReadOnlyList<long> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if(path.Count>Slots.Count||path.Any(id=>id<=0))throw new ArgumentException("已验证级联路径长度或主键无效。",nameof(path));
        var values=new Dictionary<string,object?>(StringComparer.Ordinal);
        for(var index=0;index<Slots.Count;index++)values.Add(Slots[index].ColumnName,index==path.Count-1?Slots[index].LogicalType=="string"?path[index].ToString(CultureInfo.InvariantCulture):(object)path[index]:null);
        return new ReadOnlyDictionary<string,object?>(values);
    }

    /// <summary>仅接受本计划全部ID位；多位有值是坏历史数据，不能擅自选最深位或在读取时修补。</summary>
    public CrossTableSelectedNode? ReadStored(IReadOnlyDictionary<string,object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if(values.Count!=Slots.Count||values.Keys.Any(key=>!Slots.Any(slot=>slot.ColumnName==key)))throw new ArgumentException("级联存储值必须完整且仅含本计划ID位。",nameof(values));
        CrossTableSelectedNode? result=null;
        for(var index=0;index<Slots.Count;index++)
        {
            var slot=Slots[index];var value=values[slot.ColumnName];if(value is null||slot.LogicalType=="string"&&value is "")continue;
            long id;
            if(slot.LogicalType=="int64"&&value is long number&&number>0)id=number;
            else if(slot.LogicalType=="string"&&value is string text&&long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var parsed)&&parsed>0&&text==parsed.ToString(CultureInfo.InvariantCulture))id=parsed;
            else throw new InvalidOperationException("稀疏级联已存主键类型或规范格式无效。");
            if(result is not null)throw new InvalidOperationException("稀疏级联多层同时有值，无法无歧义恢复所选节点。");result=new(index,id);
        }
        return result;
    }
    /// <summary>联接开关仅沿元数据整数／布尔合同解释，不执行配置文本。</summary>
    private static bool Enabled(ColumnMetadata column,string key)=>column.Extra.TryGetValue(key,out var value)&&(value.ValueKind==System.Text.Json.JsonValueKind.True||value.ToString()=="1");
}
