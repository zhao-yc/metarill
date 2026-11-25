using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Metarill.Core.Metadata;

/// <summary>原selfTree的隐式受管名称；冗余物理存储、非冗余只读派生，共用确定性定义且不回写配置。</summary>
public static class CascadeTreeStorageColumns
{
    public const string ReservedIdPrefix="__mt_auto_cascade_name_";
    /// <summary>显式配置优先，实体表的物理selfTree叶子可派生名称，是否落库由统一存储计划决定，绝不增加页面字段。</summary>
    public static IReadOnlyList<ColumnMetadata> Expand(TableMetadata table,IEnumerable<ColumnMetadata> declared)
    {
        var columns=declared.ToArray();
        if(table.IsRongyu is not(0 or 1)||table.IsFunctionTable!=0||table.IsView!=0)return columns;
        var result=columns.ToList();
        foreach(var source in columns.Where(column=>IsPhysicalSelfTreeKey(column,allowAlternate:true)))
        {
            var name=source.ColumnName[..^3]+"_name";MetadataValidation.RequireIdentifier(name);
            // 包括虚拟、错误类型和大小写冲突的显式列都不覆盖，正式生成继续暴露其缺口。
            if(result.Any(column=>column.ColumnName.Equals(name,StringComparison.OrdinalIgnoreCase)))continue;
            var id=ReservedIdPrefix+Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]{table.Id,source.Id,source.ColumnName}))));
            if(result.Any(column=>column.Id==id))throw new InvalidOperationException("级联派生列标识与显式字段冲突。");
            result.Add(new(){Id=id,ConfigTableId=table.Id,YzProjectId=source.YzProjectId,Name=source.Name+"名称",SortNo=source.SortNo,ColumnName=name,ColumnType="nvarchar",LogicalType="string",ColumnLength=string.Equals(source.LogicalType,"string",StringComparison.OrdinalIgnoreCase)&&!string.IsNullOrWhiteSpace(source.ColumnLength)?source.ColumnLength:"50",IsDbNotNull=0});
        }
        return CrossTableStorageColumns.Expand(table,result.ToArray());
    }
    /// <summary>非冗余实体的自树叶名来自表级外键定义，不能把普通字符串或虚拟列当成逻辑范围。</summary>
    public static bool IsLogicalNameField(TableMetadata table, IEnumerable<ColumnMetadata> declared, string field)
    {
        if (table.IsRongyu != 0 || table.IsFunctionTable != 0 || table.IsView != 0 ||
            !field.EndsWith("_name", StringComparison.Ordinal))
            return false;
        var columns = declared.Where(column => column.ConfigTableId == table.Id).ToArray();
        return columns.Any(column => column.ColumnName == field && column.LogicalType == "string" && column.IsVirtualColumn == 0) &&
            columns.Any(column => column.ColumnName == field[..^5] + "_id" && IsPhysicalSelfTreeKey(column,allowAlternate:true));
    }

    /// <summary>结构扩展和原父范围只识别表级声明的实际自树外键。</summary>
    internal static bool IsPhysicalSelfTreeKey(ColumnMetadata column)=>IsPhysicalSelfTreeKey(column,allowAlternate:false);

    private static bool IsPhysicalSelfTreeKey(ColumnMetadata column,bool allowAlternate)=>
        column.ControlKind=="CascadeTree"&&column.IsVirtualColumn==0&&!Enabled(column,"isJoinColumn")&&
        (string.Equals(column.LogicalType,"string",StringComparison.OrdinalIgnoreCase)||string.Equals(column.LogicalType,"int64",StringComparison.OrdinalIgnoreCase))&&
        column.ColumnName.EndsWith("_id",StringComparison.Ordinal)&&
        column.ColumnName is not("yz_project_id" or "insert_manager_id" or "update_manager_id" or "insert_user_id" or "update_user_id")&&
        SelfTree(column.ControlDetail,allowAlternate);

    /// <summary>只识别无重复键的全局selfTree形状，未知/损坏定义不猜列；页级覆盖不能改变表结构。</summary>
    private static bool SelfTree(string json,bool allowAlternate)
    {
        try
        {
            using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
            if(!Object(root)||Text(root,"sourceMode","")!="selfTree"||!root.TryGetProperty("selfTree",out var tree)||!Object(tree))return false;
            MetadataValidation.RequireIdentifier(Text(tree,"table",""));MetadataValidation.RequireIdentifier(Text(tree,"parentField",""));MetadataValidation.RequireIdentifier(Text(tree,"nameField","name"));
            var key=Text(tree,"idField","id");
            MetadataValidation.RequireIdentifier(key);
            return (allowAlternate||key=="id")&&Text(tree,"rootMode","blank")=="blank";
        }
        catch(Exception error)when(error is JsonException or InvalidOperationException or ArgumentException){return false;}
    }
    /// <summary>树节点键必须来自目标实体；业务键要求非空整型和与项目范围相符的唯一索引。</summary>
    public static (ColumnMetadata Key,string? IndexId) ResolveNodeKey(MetadataCatalog catalog,TableMetadata table,string name)
    {
        var storage=MetadataStoragePolicy.ForTable(catalog,table);
        var key=storage.PhysicalColumns.SingleOrDefault(column=>column.ColumnName==name);
        if(key is not {LogicalType:"int64",IsDbNotNull:1}||!GlobalPhysicalRelationValue.IsScalar(storage,table,key))
            throw new NotSupportedException("自树节点键必须为真实非空的整数列。");
        if(key.ColumnName=="id")return(key,null);

        var project=storage.PhysicalColumns.SingleOrDefault(column=>column.ColumnName=="yz_project_id");
        foreach(var index in catalog.ConfigIndexes.OrderBy(index=>Value(index,"id"),StringComparer.Ordinal))
        {
            if(Value(index,"configTableId")!=table.Id||Value(index,"id") is not {Length:>0} id||
                index.TryGetProperty("isEnabled",out _)&&Value(index,"isEnabled") is not ("1" or "true" or "True")||
                Value(index,"isUnique") is not ("1" or "true" or "True"))
                continue;
            var fields=catalog.ConfigIndexColumns.Where(item=>Value(item,"configIndexId")==id)
                .Select(item=>Value(item,"configColumnId")).ToArray();
            if(fields.Length==1&&fields[0]==key.Id||fields.Length==2&&project is not null&&
                fields.Contains(project.Id,StringComparer.Ordinal)&&fields.Contains(key.Id,StringComparer.Ordinal))
                return(key,id);
        }
        throw new NotSupportedException("自树业务键需要单列唯一索引或项目与业务键的联合唯一索引。");
    }

    /// <summary>识别已声明的业务键；旧主键配置及无效表级定义维持原判断。</summary>
    public static bool DeclaresAlternateNodeKey(ColumnMetadata column)=>
        IsPhysicalSelfTreeKey(column,allowAlternate:true)&&!IsPhysicalSelfTreeKey(column);

    /// <summary>只在运行时目录保留原自树定义实际使用的唯一索引。</summary>
    public static IReadOnlySet<string> RequiredNodeIndexes(MetadataCatalog catalog)
    {
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var column in catalog.ConfigColumns.Where(DeclaresAlternateNodeKey))
        {
            using var definition=JsonDocument.Parse(column.ControlDetail);
            var tree=definition.RootElement.GetProperty("selfTree");
            var key=Text(tree,"idField","id");
            var table=catalog.ConfigTables.SingleOrDefault(candidate=>candidate.TableName==Text(tree,"table",""));
            if(table is null)continue;
            try
            {
                if(ResolveNodeKey(catalog,table,key).IndexId is {} indexId)ids.Add(indexId);
            }
            catch(NotSupportedException)
            {
                // 不完整定义保留给固定读取计划报告，发布目录不补推断索引。
            }
        }
        return ids;
    }

    private static string Value(JsonElement element,string field)=>element.TryGetProperty(field,out var value)?value.ToString():"";

    /// <summary>不接受JSON最后值覆盖，避免结构与读取计划解释不同的级联定义。</summary>
    private static bool Object(JsonElement value)=>value.ValueKind==JsonValueKind.Object&&value.EnumerateObject().Select(field=>field.Name).Distinct(StringComparer.Ordinal).Count()==value.EnumerateObject().Count();
    /// <summary>只读取文本配置，错误类型不能被转成SQL标识符。</summary>
    private static string Text(JsonElement value,string key,string fallback)=>!value.TryGetProperty(key,out var item)||item.ValueKind==JsonValueKind.Null?fallback:item.ValueKind==JsonValueKind.String?string.IsNullOrWhiteSpace(item.GetString())?fallback:item.GetString()!:throw new InvalidOperationException("级联配置应为文本。");
    /// <summary>联接声明不是本表物理叶子，不能借辅助列扩展改变存储归属。</summary>
    private static bool Enabled(ColumnMetadata column,string key)=>column.Extra.TryGetValue(key,out var value)&&(value.ValueKind==JsonValueKind.True||value.ToString()=="1");
}
