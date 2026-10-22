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
        foreach(var source in columns.Where(column=>column.ControlKind=="CascadeTree"&&column.IsVirtualColumn==0&&!Enabled(column,"isJoinColumn")&&(string.Equals(column.LogicalType,"string",StringComparison.OrdinalIgnoreCase)||string.Equals(column.LogicalType,"int64",StringComparison.OrdinalIgnoreCase))))
        {
            if(!source.ColumnName.EndsWith("_id",StringComparison.Ordinal)||source.ColumnName is "yz_project_id" or "insert_manager_id" or "update_manager_id" or "insert_user_id" or "update_user_id"||!SelfTree(source.ControlDetail))continue;
            var name=source.ColumnName[..^3]+"_name";MetadataValidation.RequireIdentifier(name);
            // 包括虚拟、错误类型和大小写冲突的显式列都不覆盖，正式生成继续暴露其缺口。
            if(result.Any(column=>column.ColumnName.Equals(name,StringComparison.OrdinalIgnoreCase)))continue;
            var id=ReservedIdPrefix+Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]{table.Id,source.Id,source.ColumnName}))));
            if(result.Any(column=>column.Id==id))throw new InvalidOperationException("级联派生列标识与显式字段冲突。");
            result.Add(new(){Id=id,ConfigTableId=table.Id,YzProjectId=source.YzProjectId,Name=source.Name+"名称",SortNo=source.SortNo,ColumnName=name,ColumnType="nvarchar",LogicalType="string",ColumnLength=string.Equals(source.LogicalType,"string",StringComparison.OrdinalIgnoreCase)&&!string.IsNullOrWhiteSpace(source.ColumnLength)?source.ColumnLength:"50",IsDbNotNull=0});
        }
        return CrossTableStorageColumns.Expand(table,result.ToArray());
    }
    /// <summary>只识别无重复键的全局selfTree形状，未知/损坏定义不猜列；页级覆盖不能改变表结构。</summary>
    private static bool SelfTree(string json)
    {
        try
        {
            using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
            if(!Object(root)||Text(root,"sourceMode","")!="selfTree"||!root.TryGetProperty("selfTree",out var tree)||!Object(tree))return false;
            MetadataValidation.RequireIdentifier(Text(tree,"table",""));MetadataValidation.RequireIdentifier(Text(tree,"parentField",""));MetadataValidation.RequireIdentifier(Text(tree,"nameField","name"));
            return Text(tree,"idField","id")=="id"&&Text(tree,"rootMode","blank")=="blank";
        }
        catch(Exception error)when(error is JsonException or InvalidOperationException or ArgumentException){return false;}
    }
    /// <summary>不接受JSON最后值覆盖，避免结构与读取计划解释不同的级联定义。</summary>
    private static bool Object(JsonElement value)=>value.ValueKind==JsonValueKind.Object&&value.EnumerateObject().Select(field=>field.Name).Distinct(StringComparer.Ordinal).Count()==value.EnumerateObject().Count();
    /// <summary>只读取文本配置，错误类型不能被转成SQL标识符。</summary>
    private static string Text(JsonElement value,string key,string fallback)=>!value.TryGetProperty(key,out var item)||item.ValueKind==JsonValueKind.Null?fallback:item.ValueKind==JsonValueKind.String?string.IsNullOrWhiteSpace(item.GetString())?fallback:item.GetString()!:throw new InvalidOperationException("级联配置应为文本。");
    /// <summary>联接声明不是本表物理叶子，不能借辅助列扩展改变存储归属。</summary>
    private static bool Enabled(ColumnMetadata column,string key)=>column.Extra.TryGetValue(key,out var value)&&(value.ValueKind==JsonValueKind.True||value.ToString()=="1");
}
