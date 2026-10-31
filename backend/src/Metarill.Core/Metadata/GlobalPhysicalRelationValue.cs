using System.Text.Json;
using Metarill.Core.Security;
namespace Metarill.Core.Metadata;

/// <summary>全局关系所有读取／缓存用途共用物理终点证明，不能把凭据或派生缓存伪装成字典值。</summary>
public static class GlobalPhysicalRelationValue
{
    /// <summary>只认可所属目标表的非凭据物理标量；数组、文件、虚拟值及已有联接缓存均拒绝。</summary>
    public static bool IsScalar(MetadataStoragePlan storage,TableMetadata target,ColumnMetadata column)=>column.ConfigTableId==target.Id&&storage.IsPhysical(column)&&column.IsVirtualColumn==0&&!(column.Extra.TryGetValue("isJoinColumn",out var cached)&&(cached.ValueKind==JsonValueKind.True||cached.ToString()=="1"))&&!ExactDataManageValues.IsSensitiveField(column.ColumnName)&&column.ControlKind is not("FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "CheckBoxList" or "MultiList" or "CascadeTree")&&column.LogicalType is not("json" or "binary");
    /// <summary>物理值保持原合同；名称缓存只有逐层证明同目录全局来源后才可作为单值全局目标。</summary>
    public static bool IsPublishedScalar(MetadataCatalog catalog,MetadataStoragePlan storage,TableMetadata target,ColumnMetadata column)
        =>IsPublishedScalar(catalog,storage,target,column,new HashSet<string>(StringComparer.Ordinal));
    /// <summary>显式传递本次证明栈，限制缓存链深度并拒绝循环；不保存请求状态或引入静态可变上下文。</summary>
    internal static bool IsPublishedScalar(MetadataCatalog catalog,MetadataStoragePlan storage,TableMetadata target,ColumnMetadata column,ISet<string> resolving)
    {
        if(IsScalar(storage,target,column))return true;
        if(column.ConfigTableId!=target.Id||!storage.IsPhysical(column)||column.IsVirtualColumn!=0||column.LogicalType!="string"||column.ControlKind is not("" or "TextArea")||ExactDataManageValues.IsSensitiveField(column.ColumnName)||!column.Extra.TryGetValue("isJoinColumn",out var cached)||!(cached.ValueKind==JsonValueKind.True||cached.ToString()=="1"))return false;
        var key=target.Id+":"+column.Id;
        if(resolving.Count>=16||!resolving.Add(key))throw new NotSupportedException("全局名称缓存依赖存在循环或超过16层。");
        try
        {
            // 该证明继续要求无项目列的R1全局来源、真实物理List、固定全局目标及精确名称副本。
            _=GlobalCachedRelationSource.RequireCache(catalog,target,column,resolving);return true;
        }
        finally{resolving.Remove(key);}
    }

}
