using System.Text.Json;
using Metarill.Core.Security;
namespace Metarill.Core.Metadata;

/// <summary>全局关系所有读取／缓存用途共用物理终点证明，不能把凭据或派生缓存伪装成字典值。</summary>
public static class GlobalPhysicalRelationValue
{
    /// <summary>只认可所属目标表的非凭据物理标量；数组、文件、虚拟值及已有联接缓存均拒绝。</summary>
    public static bool IsScalar(MetadataStoragePlan storage,TableMetadata target,ColumnMetadata column)=>column.ConfigTableId==target.Id&&storage.IsPhysical(column)&&column.IsVirtualColumn==0&&!(column.Extra.TryGetValue("isJoinColumn",out var cached)&&(cached.ValueKind==JsonValueKind.True||cached.ToString()=="1"))&&!ExactDataManageValues.IsSensitiveField(column.ColumnName)&&column.ControlKind is not("FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "CheckBoxList" or "MultiList" or "CascadeTree")&&column.LogicalType is not("json" or "binary");
}
