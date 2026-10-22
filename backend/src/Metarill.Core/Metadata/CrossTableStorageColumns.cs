using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Metarill.Core.Metadata;

/// <summary>跨表级联各级物理ID/名称的共同定义；只扩展计划，不写元数据或执行DDL。</summary>
public static class CrossTableStorageColumns
{
    public const string ReservedIdPrefix="__mt_auto_cross_";
    /// <summary>沿原columnPrefix派生缺失列，显式声明包括错误类型和虚拟列均不覆盖。</summary>
    internal static IReadOnlyList<ColumnMetadata> Expand(TableMetadata table,IReadOnlyList<ColumnMetadata> columns)
    {
        if(table.IsRongyu!=1||table.IsFunctionTable!=0||table.IsView!=0)return columns;var result=columns.ToList();
        foreach(var source in columns.Where(column=>column.ControlKind=="CascadeTree"&&column.IsVirtualColumn==0&&!Enabled(column,"isJoinColumn")&&column.ColumnName.EndsWith("_id",StringComparison.Ordinal)&&(column.LogicalType.Equals("string",StringComparison.OrdinalIgnoreCase)||column.LogicalType.Equals("int64",StringComparison.OrdinalIgnoreCase))))
        {
            var prefixes=Prefixes(source);if(prefixes.Count==0)continue;
            foreach(var prefix in prefixes)foreach(var suffix in new[]{"_id","_name"})
            {
                var name=prefix+suffix;if(result.Any(column=>column.ColumnName.Equals(name,StringComparison.OrdinalIgnoreCase)))continue;
                var id=ReservedIdPrefix+Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]{table.Id,source.Id,source.ColumnName,name}))));if(result.Any(column=>column.Id==id))throw new InvalidOperationException("跨表派生字段标识冲突。");
                var logical=suffix=="_id"?source.LogicalType.ToLowerInvariant():"string";var length=source.LogicalType.Equals("string",StringComparison.OrdinalIgnoreCase)&&!string.IsNullOrWhiteSpace(source.ColumnLength)?source.ColumnLength:"50";
                result.Add(new(){Id=id,ConfigTableId=table.Id,YzProjectId=source.YzProjectId,ColumnName=name,Name=prefix+(suffix=="_id"?"ID":"名称"),LogicalType=logical,ColumnType=logical=="int64"?"bigint":"nvarchar",ColumnLength=logical=="string"?length:"",SortNo=source.SortNo,IsDbNotNull=0});
            }
        }
        return result.ToArray();
    }
    /// <summary>只使用全局固定层级的结构形状；页覆盖不能改变表结构，系统列前缀不作托管列。</summary>
    private static IReadOnlyList<string> Prefixes(ColumnMetadata source)
    {
        try
        {
            using var json=JsonDocument.Parse(source.ControlDetail);var root=json.RootElement;if(!Object(root)||Text(root,"sourceMode","crossTable")!="crossTable"||!root.TryGetProperty("levels",out var levels)||levels.ValueKind!=JsonValueKind.Array||levels.GetArrayLength()<2)return [];
            var result=new List<string>();var keys=new HashSet<string>(StringComparer.Ordinal);var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var level in levels.EnumerateArray())
            {
                if(!Object(level))return [];var table=Text(level,"table","");MetadataValidation.RequireIdentifier(table);var prefix=Text(level,"columnPrefix",table);MetadataValidation.RequireIdentifier(prefix);MetadataValidation.RequireIdentifier(Text(level,"nameField","name"));if(Text(level,"idField","id")!="id"||!keys.Add(Text(level,"key",table))||!names.Add(prefix)||prefix is "yz_project" or "insert_manager" or "update_manager" or "insert_user" or "update_user")return [];
                if(result.Count>0)MetadataValidation.RequireIdentifier(Text(level,"parentField",""));result.Add(prefix);
            }
            return result[^1]+"_id"==source.ColumnName?result:[];
        }
        catch(Exception error)when(error is JsonException or ArgumentException or InvalidOperationException){return [];}
    }
    /// <summary>重复键不能依赖最后值覆盖影响存储计划。</summary>
    private static bool Object(JsonElement value)=>value.ValueKind==JsonValueKind.Object&&value.EnumerateObject().Select(field=>field.Name).Distinct(StringComparer.Ordinal).Count()==value.EnumerateObject().Count();
    /// <summary>不把非文本元数据转换为标识符。</summary>
    private static string Text(JsonElement value,string key,string fallback)=>!value.TryGetProperty(key,out var item)||item.ValueKind==JsonValueKind.Null?fallback:item.ValueKind==JsonValueKind.String?string.IsNullOrWhiteSpace(item.GetString())?fallback:item.GetString()!:throw new ArgumentException("级联配置应为文本。");
    /// <summary>联接/虚拟来源不能派生实体列。</summary>
    private static bool Enabled(ColumnMetadata column,string key)=>column.Extra.TryGetValue(key,out var value)&&(value.ValueKind==JsonValueKind.True||value.ToString()=="1");
}
