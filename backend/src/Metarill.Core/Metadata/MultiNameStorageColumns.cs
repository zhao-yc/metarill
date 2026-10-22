using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Metarill.Core.Metadata;

/// <summary>普通冗余多值关系的隐式物理名称缓存；不回写元数据，不猜测页级覆盖。</summary>
public static class MultiNameStorageColumns
{
    public const string ReservedIdPrefix="__mt_auto_multi_name_";
    /// <summary>显式列始终优先，包括虚拟、大小写或错误类型；完整用途证明仍由生成器负责。</summary>
    public static IReadOnlyList<ColumnMetadata> Expand(TableMetadata table,IEnumerable<ColumnMetadata> declared)
    {
        var columns=declared.ToArray();
        if(table.IsRongyu!=1||table.IsFunctionTable!=0||table.IsView!=0||table.Extra.GetValueOrDefault("scopeKind").ToString()=="global"||Protected(table))return columns;
        var result=columns.ToList();
        foreach(var source in columns.Where(column=>column.IsVirtualColumn==0&&!Enabled(column,"isJoinColumn")&&column.ControlKind is "MultiSelectPage" or "MultiCheckBoxDatabase"&&column.LogicalType.Equals("string",StringComparison.OrdinalIgnoreCase)&&column.ColumnName.EndsWith("_ids",StringComparison.Ordinal)))
        {
            var label=Label(source);if(label is null or "id")continue;
            var stem=source.ColumnName[..^4];if(stem.Length==0)continue;
            var name=stem+"_"+(label.EndsWith('s')?label:label+"s");MetadataValidation.RequireIdentifier(name);
            if(result.Any(column=>column.ColumnName.Equals(name,StringComparison.OrdinalIgnoreCase)))continue;
            var id=ReservedIdPrefix+Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]{table.Id,source.Id,source.ColumnName,name}))));
            if(result.Any(column=>column.Id==id))throw new InvalidOperationException("多值派生名称标识与显式配置冲突。");
            result.Add(new(){Id=id,ConfigTableId=table.Id,YzProjectId=source.YzProjectId,Name=source.Name+"名称",SortNo=source.SortNo,ColumnName=name,ColumnType="nvarchar",LogicalType="string",ColumnLength="max",IsDbNotNull=0,Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement(stem+"."+label)}});
        }
        return result;
    }
    /// <summary>安全关系由原专用协调器管理，不能自动增加角色缓存或改变权限物理结构。</summary>
    private static bool Protected(TableMetadata table)=>table.TableName is "yz_manager" or "yz_role" or "yz_admin_type"||table.Id is "6031906809708544" or "1692532858499969026" or "1696799308939239425";
    /// <summary>只读取根定义的固定标量名称；坏配置留待正式用途门禁拒绝，不退化为猜列。</summary>
    private static string? Label(ColumnMetadata source)
    {
        try
        {
            if(source.ControlKind=="MultiCheckBoxDatabase"){DatabaseCheckboxDefinition.Parse(source.ControlDetail);return "name";}
            if(string.IsNullOrWhiteSpace(source.ControlDetail))return "name";
            using var document=JsonDocument.Parse(source.ControlDetail);var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||root.EnumerateObject().Select(field=>field.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=root.EnumerateObject().Count())return null;
            if(root.EnumerateObject().Any(field=>field.Name.Equals("nameField",StringComparison.OrdinalIgnoreCase)&&field.Name!="nameField"))return null;
            if(!root.TryGetProperty("nameField",out var value)||value.ValueKind==JsonValueKind.Null)return "name";
            if(value.ValueKind!=JsonValueKind.String)return null;
            var label=string.IsNullOrWhiteSpace(value.GetString())?"name":value.GetString()!;MetadataValidation.RequireIdentifier(label);return label;
        }
        catch(Exception error)when(error is JsonException or InvalidOperationException or ArgumentException or NotSupportedException){return null;}
    }
    /// <summary>联接来源不能成为新的物理ID关系。</summary>
    private static bool Enabled(ColumnMetadata column,string key)=>column.Extra.TryGetValue(key,out var value)&&(value.ValueKind==JsonValueKind.True||value.ToString()=="1");
}
