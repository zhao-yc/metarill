using System.Collections.ObjectModel;
using System.Text.Json;

namespace Metarill.Core.Metadata;

public enum MetadataFieldStorage { Physical, Virtual, Join, RelationDisplay, Layout }

/// <summary>按完整表配置编译的字段归属；类型判断不再由DDL、仓储和生成器各自猜测。</summary>
public sealed class MetadataStoragePlan
{
    private readonly IReadOnlyDictionary<string,MetadataFieldStorage> kinds;
    internal MetadataStoragePlan(IReadOnlyList<ColumnMetadata> columns,Dictionary<string,MetadataFieldStorage> kinds)
    {this.kinds=new ReadOnlyDictionary<string,MetadataFieldStorage>(kinds);PhysicalColumns=Array.AsReadOnly(columns.Where(column=>kinds[column.Id]==MetadataFieldStorage.Physical).Select(Snapshot).ToArray());}
    /// <summary>明确复制字段值及扩展JSON；不通过反序列化让同名扩展键覆盖已验证属性。</summary>
    private static ColumnMetadata Snapshot(ColumnMetadata column)=>new()
    {
        Id=column.Id,Name=column.Name,YzProjectId=column.YzProjectId,SortNo=column.SortNo,ConfigTableId=column.ConfigTableId,ColumnName=column.ColumnName,ColumnType=column.ColumnType,ColumnLength=column.ColumnLength,LogicalType=column.LogicalType,IsDbNotNull=column.IsDbNotNull,IsVirtualColumn=column.IsVirtualColumn,Precision=column.Precision,Scale=column.Scale,ControlKind=column.ControlKind,ControlDetail=column.ControlDetail,DefaultValue=column.DefaultValue,LinkConfigTableId=column.LinkConfigTableId,
        Extra=column.Extra.ToDictionary(pair=>pair.Key,pair=>pair.Value.Clone(),StringComparer.Ordinal)
    };
    public IReadOnlyList<ColumnMetadata> PhysicalColumns{get;}
    /// <summary>只接受本计划中的稳定字段ID，不把未声明字段默认为物理列。</summary>
    public MetadataFieldStorage Kind(ColumnMetadata column)=>kinds.TryGetValue(column.Id,out var kind)?kind:throw new InvalidOperationException("字段不属于当前存储计划。");
    /// <summary>同一个固定判定供结构、索引、基础设施存储与生成器使用。</summary>
    public bool IsPhysical(ColumnMetadata column)=>Kind(column)==MetadataFieldStorage.Physical;
}

/// <summary>移植原非冗余字段归属：仅真实业务列、所选级联节点ID和基础审计ID持久化。</summary>
public static class MetadataStoragePolicy
{
    private static readonly HashSet<string> AuditNames=new(StringComparer.OrdinalIgnoreCase){"insert_manager_real_name","update_manager_real_name","insert_user_name","update_user_name"};
    private static readonly HashSet<string> RelationKinds=new(StringComparer.Ordinal){"List","SelectPage","MultiSelectPage","CascadeTree","MultiCheckBoxDatabase"};
    /// <summary>冗余模式继续沿原虚拟标志；非冗余只在实体表生效，不修改输入元数据。</summary>
    public static MetadataStoragePlan Compile(TableMetadata table,IEnumerable<ColumnMetadata> tableColumns)
    {
        ArgumentNullException.ThrowIfNull(table);ArgumentNullException.ThrowIfNull(tableColumns);var columns=RelationStorageColumns.Expand(table,tableColumns).ToArray();
        if(columns.Any(column=>column.ConfigTableId!=table.Id)||columns.Select(column=>column.Id).Distinct(StringComparer.Ordinal).Count()!=columns.Length)throw new InvalidOperationException("存储计划的字段归属不一致或ID重复。");
        var nonredundant=table.IsRongyu==0&&table.IsFunctionTable==0&&table.IsView==0;var managed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(nonredundant)
        {
            // 固定跨表各级ID属于同一稀疏选择；显式关联目标不能再把其名称误作普通业务别名冲突。
            var shared=SharedCrossTableSparseSelection.CompileAll(table,columns).SelectMany(group=>group.Branches.Select(branch=>(branch.SourceColumnId,Group:group))).ToDictionary(item=>item.SourceColumnId,item=>item.Group,StringComparer.Ordinal);
            var sparseSlots=columns.Where(column=>column.ControlKind=="CascadeTree"&&column.IsVirtualColumn==0&&!Enabled(column,"isJoinColumn")&&CrossTableStorageColumns.LevelPrefixes(column).Count>0)
                .SelectMany(column=>shared.TryGetValue(column.Id,out var group)?group.BranchSelection(column.Id).Slots:CrossTableSparseSelection.Compile(table,column.Id,columns).Slots).Select(slot=>slot.ColumnId).ToHashSet(StringComparer.Ordinal);
            foreach(var relation in columns.Where(column=>column.IsVirtualColumn==0&&!Enabled(column,"isJoinColumn")&&IsRelationColumn(column)))
            {
                if(relation.ControlKind=="CascadeTree")
                {
                    foreach(var name in CascadeNames(relation))if(!name.Equals(relation.ColumnName,StringComparison.OrdinalIgnoreCase))managed.Add(name);
                }
                else if(!sparseSlots.Contains(relation.Id))
                {
                    var auxiliary=Auxiliary(relation);if(auxiliary.Length==0)continue;
                    foreach(var candidate in columns.Where(column=>column.ColumnName.Equals(auxiliary,StringComparison.OrdinalIgnoreCase)&&column.IsVirtualColumn==0&&!Enabled(column,"isJoinColumn")&&!AuditNames.Contains(column.ColumnName)))throw new InvalidOperationException("关联显示别名与真实业务字段冲突，请调整列名或明确声明联接字段。");
                }
            }
            foreach(var column in columns.Where(column=>column.ColumnName is "insert_user_name" or "update_user_name"&&column.IsVirtualColumn==0&&!Enabled(column,"isJoinColumn")))
            {
                var idName=column.ColumnName.Replace("_name","_id",StringComparison.Ordinal);
                if(!columns.Any(relation=>relation.ColumnName==idName&&IsRelationColumn(relation)&&Auxiliary(relation)==column.ColumnName))throw new InvalidOperationException("非冗余App审计姓名缺少明确的用户关系配置。");
            }
        }
        var kinds=new Dictionary<string,MetadataFieldStorage>(StringComparer.Ordinal);
        foreach(var column in columns)
        {
            var kind=column.IsVirtualColumn!=0?MetadataFieldStorage.Virtual:!nonredundant?MetadataFieldStorage.Physical:
                Enabled(column,"isJoinColumn")?MetadataFieldStorage.Join:column.ControlKind is "Title" or "Tab"?MetadataFieldStorage.Layout:
                AuditNames.Contains(column.ColumnName)||managed.Contains(column.ColumnName)?MetadataFieldStorage.RelationDisplay:MetadataFieldStorage.Physical;
            kinds.Add(column.Id,kind);
        }
        return new(columns,kinds);
    }
    /// <summary>按表筛选后一次编译，避免每个调用点使用不同虚拟或派生字段规则。</summary>
    public static MetadataStoragePlan ForTable(MetadataCatalog catalog,TableMetadata table)=>Compile(table,catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id));
    /// <summary>原始业务_id本身不是关系，必须有明确控件、关联表或关联页声明。</summary>
    public static bool IsRelationColumn(ColumnMetadata column)=>RelationKinds.Contains(column.ControlKind)||column.LinkConfigTableId.Length>0||Text(column,"joinTable").Length>0||Text(column,"linkConfigPageId").Length>0;
    /// <summary>原多值名称仅追加s，已有s后缀保持；id展示不创建辅助列。</summary>
    private static string Auxiliary(ColumnMetadata column)
    {
        var name="name";if(column.ControlKind is "List" or "SelectPage" or "MultiSelectPage"){var declared=Value(Definition(column),"nameField");if(declared.Length>0)name=declared;}
        if(name=="id")return "";MetadataValidation.RequireIdentifier(name);
        if(column.ColumnName.EndsWith("_id",StringComparison.Ordinal))return column.ColumnName[..^3]+"_"+name;
        if(column.ColumnName.EndsWith("_ids",StringComparison.Ordinal))return column.ColumnName[..^4]+"_"+(name.EndsWith('s')?name:name+"s");return "";
    }
    /// <summary>级联名称只读派生；selfTree只存节点ID，有效跨表按层级稀疏存所选ID，逻辑祖先仍从目标恢复。</summary>
    private static IEnumerable<string> CascadeNames(ColumnMetadata column)
    {
        var definition=Definition(column);var mode=Value(definition,"sourceMode");
        if(mode=="selfTree")
        {
            if(!column.ColumnName.EndsWith("_id",StringComparison.Ordinal))throw new InvalidOperationException("自树级联字段必须以_id结尾。");yield return column.ColumnName;yield return column.ColumnName[..^3]+"_name";yield break;
        }
        if(mode is not("" or "crossTable"))throw new InvalidOperationException("未知级联来源模式。");
        if(!definition.TryGetProperty("levels",out var levels)||levels.ValueKind!=JsonValueKind.Array||levels.GetArrayLength()==0)throw new InvalidOperationException("跨表级联必须声明层级。");
        var prefixes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);string? leaf=null;
        // 有完整结构证明的跨表按层级稀疏保存所选ID，名称和祖先路径仍为派生值。
        var sparse=CrossTableStorageColumns.LevelPrefixes(column).Count>0;
        foreach(var level in levels.EnumerateArray())
        {
            if(level.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("级联层级必须是对象。");var prefix=Value(level,"columnPrefix");if(prefix.Length==0)prefix=Value(level,"table");MetadataValidation.RequireIdentifier(prefix);if(!prefixes.Add(prefix))throw new InvalidOperationException("级联层级前缀重复。");leaf=prefix+"_id";if(!sparse)yield return leaf;yield return prefix+"_name";
        }
        if(leaf!=column.ColumnName)throw new InvalidOperationException("级联末级前缀必须和源字段一致。");
    }
    /// <summary>仅解析结构声明，不读取/执行SQL；损坏或重复键不能退化成持久化普通字段。</summary>
    private static JsonElement Definition(ColumnMetadata column)
    {
        if(string.IsNullOrWhiteSpace(column.ControlDetail))return JsonSerializer.SerializeToElement(new{});
        try{using var document=JsonDocument.Parse(column.ControlDetail);var keys=new HashSet<string>(StringComparer.Ordinal);if(document.RootElement.ValueKind!=JsonValueKind.Object||document.RootElement.EnumerateObject().Any(property=>!keys.Add(property.Name)))throw new JsonException();return document.RootElement.Clone();}catch(JsonException){throw new InvalidOperationException("关系存储声明必须为无重复键的JSON对象。");}
    }
    /// <summary>结构键只接受原字符串值，不能解释为任意表达式。</summary>
    private static string Value(JsonElement value,string key)=>value.TryGetProperty(key,out var item)&&item.ValueKind==JsonValueKind.String?item.GetString()!:"";
    /// <summary>读取兼容扩展字段，空值沿原空文本语义。</summary>
    private static string Text(ColumnMetadata column,string key)=>column.Extra.TryGetValue(key,out var value)&&value.ValueKind!=JsonValueKind.Null?value.ToString():"";
    /// <summary>沿元数据开关的整数1/布尔true合同，不把任意文本当作启用。</summary>
    private static bool Enabled(ColumnMetadata column,string key)=>column.Extra.TryGetValue(key,out var value)&&(value.ValueKind==JsonValueKind.True||value.ToString()=="1");
}
