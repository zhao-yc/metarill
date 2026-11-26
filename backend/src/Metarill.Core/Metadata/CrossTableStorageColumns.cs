using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Metarill.Core.Metadata;

/// <summary>跨表级联各级ID/名称的共同定义；是否物理由统一存储计划决定，不写元数据或执行DDL。</summary>
public static class CrossTableStorageColumns
{
    public const string ReservedIdPrefix="__mt_auto_cross_";
    /// <summary>页面可改变分隔符或显式声明默认属性，实际层级仍使用表级固定定义。</summary>
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
        RemoveDefaultLevelProperties(baseline);
        RemoveDefaultLevelProperties(effective);
        return JsonNode.DeepEquals(baseline, effective);
    }

    /// <summary>原页面以列前缀生成 JSON 属性。空回退和显式同名不会改变实际字段。</summary>
    private static void RemoveDefaultLevelProperties(JsonObject definition)
    {
        if (definition["levels"] is not JsonArray levels)
            return;

        foreach (var level in levels.OfType<JsonObject>())
        {
            var prefix = level["columnPrefix"] ?? level["table"];
            if (prefix is not JsonValue value || !value.TryGetValue<string>(out var name) || string.IsNullOrEmpty(name))
                continue;

            RemoveDefaultProperty(level, "idPropertyName", MetadataNames.Camel(name + "_id"));
            RemoveDefaultProperty(level, "namePropertyName", MetadataNames.Camel(name + "_name"));
        }
    }

    /// <summary>只归一原契约的默认值，保留不同映射、大小写和非字符串供差异比较。</summary>
    private static void RemoveDefaultProperty(JsonObject level, string property, string defaultName)
    {
        var node = level[property];
        if (node is null || node is JsonValue value && value.TryGetValue<string>(out var name) && name == defaultName)
            level.Remove(property);
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
    /// <summary>逐层解析目标键，校验唯一索引后才允许绑定。</summary>
    public static ColumnMetadata? AlternateLevelKey(MetadataCatalog catalog, TableMetadata target, JsonElement level)
    {
        var name = Text(level, "idField", "id");
        if (name == "id")
            return null;
        var (key, _) = CascadeTreeStorageColumns.ResolveNodeKey(catalog, target, name);
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

    /// <summary>识别冗余表中声明使用文本业务键的父层；候选和缓存仍须验证完整发布路径。</summary>
    public static bool DeclaresStoredTextKey(MetadataCatalog catalog,TableMetadata table,ColumnMetadata slot)
    {
        if(table.IsRongyu!=1||slot.LogicalType!="string")return false;
        foreach(var control in catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id&&column.ControlKind=="CascadeTree"))
        {
            var prefixes=LevelPrefixes(control);
            var index=prefixes.TakeWhile(prefix=>prefix+"_id"!=slot.ColumnName).Count();
            if(index==prefixes.Count)continue;
            using var json=JsonDocument.Parse(control.ControlDetail);
            var level=json.RootElement.GetProperty("levels")[index];
            var tableName=Text(level,"table","");
            var key=Text(level,"idField","id");
            if(key!="id"&&catalog.ConfigTables.SingleOrDefault(candidate=>candidate.TableName==tableName) is {} target&&
                catalog.ConfigColumns.Any(column=>column.ConfigTableId==target.Id&&column.ColumnName==key&&column.LogicalType=="string"))
                return true;
        }
        return false;
    }

    /// <summary>只识别实际发布页用作父参数的文本级联层位，路径由数据层完整复核。</summary>
    public static bool DeclaresStoredTextParent(MetadataCatalog catalog,TableMetadata table,PageMetadata page)
    {
        if(table.IsRongyu!=1||page.ConfigTableId!=table.Id)return false;
        var declared=catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id).ToArray();
        if(!declared.Any(column=>column.ControlKind=="CascadeTree"))return false;
        var columns=RelationStorageColumns.Expand(table,declared);
        var textKeys=columns.Where(column=>DeclaresStoredTextKey(catalog,table,column))
            .Select(column=>column.ColumnName).ToHashSet(StringComparer.Ordinal);
        if(textKeys.Count==0)return false;
        var condition=FixedConditionPlan.Compile(page.WhereSql,columns.Select(column=>column.ColumnName));
        return condition.Terms.Any(term=>textKeys.Contains(term.Field)&&term.Values.Any(value=>
            value.Kind==FixedConditionValueKind.Variable&&MultiTargetParentScope.IsParameter(value.Text)));
    }

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
