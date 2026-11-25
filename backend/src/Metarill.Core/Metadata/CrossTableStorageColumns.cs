using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Metarill.Core.Metadata;

/// <summary>跨表级联各级ID/名称的共同定义；是否物理由统一存储计划决定，不写元数据或执行DDL。</summary>
public static class CrossTableStorageColumns
{
    public const string ReservedIdPrefix="__mt_auto_cross_";
    /// <summary>页面只可改变路径显示分隔符，生成和文件授权继续使用同一固定层级定义。</summary>
    public static bool KeepsDefinition(string tableDetail, string? pageDetail)
    {
        if (string.IsNullOrWhiteSpace(pageDetail))
            return true;
        if (JsonNode.Parse(tableDetail) is not JsonObject baseline ||
            JsonNode.Parse(pageDetail) is not JsonObject effective)
            return false;

        if (effective["displayJoiner"] is { } joiner && joiner.GetValueKind() != JsonValueKind.String)
            return false;
        baseline.Remove("displayJoiner");
        effective.Remove("displayJoiner");
        return JsonNode.DeepEquals(baseline, effective);
    }

    /// <summary>沿原columnPrefix派生缺失列，显式声明包括错误类型和虚拟列均不覆盖。</summary>
    internal static IReadOnlyList<ColumnMetadata> Expand(TableMetadata table,IReadOnlyList<ColumnMetadata> columns)
    {
        if(table.IsRongyu is not(0 or 1)||table.IsFunctionTable!=0||table.IsView!=0)return columns;var result=columns.ToList();
        foreach(var source in columns.Where(column=>column.ControlKind=="CascadeTree"&&column.IsVirtualColumn==0&&!Enabled(column,"isJoinColumn")&&column.ColumnName.EndsWith("_id",StringComparison.Ordinal)&&(column.LogicalType.Equals("string",StringComparison.OrdinalIgnoreCase)||column.LogicalType.Equals("int64",StringComparison.OrdinalIgnoreCase))))
        {
            var prefixes=LevelPrefixes(source);if(prefixes.Count==0)continue;
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
    /// <summary>逐层解析目标键。数值键沿用原正整数协议，校验唯一索引后才允许绑定。</summary>
    public static ColumnMetadata? AlternateLevelKey(MetadataCatalog catalog, TableMetadata target, JsonElement level)
    {
        var name = Text(level, "idField", "id");
        if (name == "id")
            return null;
        var (key, _) = CascadeTreeStorageColumns.ResolveNodeKey(catalog, target, name);
        if (key.LogicalType != "int64")
            throw new NotSupportedException("跨表级联文本业务键尚未接通。");
        return key;
    }

    /// <summary>精简运行时目录保留各层实际引用的业务键索引，由完整计划验证坏定义。</summary>
    public static IReadOnlySet<(string TableId,string IndexId)> RequiredNodeIndexes(MetadataCatalog catalog)
    {
        var result = new HashSet<(string TableId,string IndexId)>();
        foreach (var control in catalog.ConfigColumns.Where(column => column.ControlKind == "CascadeTree"))
        {
            if (LevelPrefixes(control).Count == 0)
                continue;
            using var json = JsonDocument.Parse(control.ControlDetail);
            foreach (var level in json.RootElement.GetProperty("levels").EnumerateArray())
            {
                var target = catalog.ConfigTables.SingleOrDefault(table => table.TableName == Text(level, "table", ""));
                if (target is null)
                    continue;
                try
                {
                    if (AlternateLevelKey(catalog, target, level) is { } alternate &&
                        CascadeTreeStorageColumns.ResolveNodeKey(catalog, target, alternate.ColumnName).IndexId is { } index)
                        result.Add((target.Id,index));
                }
                catch (NotSupportedException)
                {
                    // 不产生推断索引，读取与存储计划仍会报告无效业务键。
                }
            }
        }
        return result;
    }

    /// <summary>在完整固定跨表定义中识别其逻辑 ID 或名称。</summary>
    public static bool ContainsPathField(ColumnMetadata source,string field)=>
        LevelPrefixes(source).Any(prefix=>field==prefix+"_id"||field==prefix+"_name");

    /// <summary>固定条件在非冗余表上引用跨表层位时，按逻辑路径比较，原稀疏存储位不作为值。</summary>
    public static bool IsLogicalPathField(TableMetadata table,IEnumerable<ColumnMetadata> columns,string field)=>
        table.IsRongyu==0&&columns.Any(source=>source.ConfigTableId==table.Id&&source.ControlKind=="CascadeTree"&&
            source.IsVirtualColumn==0&&!Enabled(source,"isJoinColumn")&&ContainsPathField(source,field));

    /// <summary>只使用全局固定层级的结构形状；页覆盖不能改变表结构，系统列前缀不作托管列。</summary>
    internal static IReadOnlyList<string> LevelPrefixes(ColumnMetadata source)
    {
        try
        {
            using var json=JsonDocument.Parse(source.ControlDetail);var root=json.RootElement;if(!Object(root)||Text(root,"sourceMode","crossTable")!="crossTable"||!root.TryGetProperty("levels",out var levels)||levels.ValueKind!=JsonValueKind.Array||levels.GetArrayLength()<2)return [];
            var result=new List<string>();var keys=new HashSet<string>(StringComparer.Ordinal);var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var level in levels.EnumerateArray())
            {
                if(!Object(level))return [];var table=Text(level,"table","");MetadataValidation.RequireIdentifier(table);var prefix=Text(level,"columnPrefix",table);MetadataValidation.RequireIdentifier(prefix);MetadataValidation.RequireIdentifier(Text(level,"nameField","name"));MetadataValidation.RequireIdentifier(Text(level,"idField","id"));if(!keys.Add(Text(level,"key",table))||!names.Add(prefix)||prefix is "yz_project" or "insert_manager" or "update_manager" or "insert_user" or "update_user")return [];
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
