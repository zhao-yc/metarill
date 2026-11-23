using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
namespace Metarill.Core.Metadata;

/// <summary>全局关系所有读取／缓存用途共用物理终点证明，不能把凭据或派生缓存伪装成字典值。</summary>
public static class GlobalPhysicalRelationValue
{
    /// <summary>只认可所属目标表的非凭据物理标量；数组、文件、虚拟值及已有联接缓存均拒绝。</summary>
    public static bool IsScalar(MetadataStoragePlan storage,TableMetadata target,ColumnMetadata column)=>column.ConfigTableId==target.Id&&storage.IsPhysical(column)&&column.IsVirtualColumn==0&&!(column.Extra.TryGetValue("isJoinColumn",out var cached)&&(cached.ValueKind==JsonValueKind.True||cached.ToString()=="1"))&&!ExactDataManageValues.IsSensitiveField(column.ColumnName)&&column.ControlKind is not("FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "CheckBoxList" or "MultiList" or "CascadeTree")&&column.LogicalType is not("json" or "binary");
    /// <summary>保留已有的整数名称副本证明入口。</summary>
    public static bool IsPublishedIntegerCache(MetadataCatalog catalog,MetadataStoragePlan storage,TableMetadata target,ColumnMetadata column)
        =>column.LogicalType is "int32" or "int64"&&IsPublishedTypedCache(catalog,storage,target,column);

    /// <summary>有类型的物理名称副本必须通过全局 List 上游、真实字段及同类型终点证明。</summary>
    public static bool IsPublishedTypedCache(MetadataCatalog catalog,MetadataStoragePlan storage,TableMetadata target,ColumnMetadata column)
        =>target.IsRongyu==1&&GlobalCrossTableTarget.Declared(target)&&MultiValueLabel.IsTyped(column.LogicalType)&&
          storage.IsPhysical(column)&&!IsScalar(storage,target,column)&&IsPublishedScalar(catalog,storage,target,column);

    /// <summary>物理值保持原合同；名称缓存须逐层证明属于同一发布目录。</summary>
    public static bool IsPublishedScalar(MetadataCatalog catalog,MetadataStoragePlan storage,TableMetadata target,ColumnMetadata column)
        =>IsPublishedScalar(catalog,storage,target,column,new HashSet<string>(StringComparer.Ordinal));
    /// <summary>实际来源页已固定时，派生名称按该页逐段证明目标和可见范围。</summary>
    public static bool IsPublishedScalar(MetadataCatalog catalog,MetadataStoragePlan storage,TableMetadata target,
        ColumnMetadata column,PageMetadata? sourcePage)
        =>IsPublishedScalar(catalog,storage,target,column,new HashSet<string>(StringComparer.Ordinal),sourcePage,out _);

    /// <summary>显式传递本次证明栈，限制缓存链深度并拒绝循环；不保存请求状态或引入静态可变上下文。</summary>
    internal static bool IsPublishedScalar(MetadataCatalog catalog,MetadataStoragePlan storage,TableMetadata target,ColumnMetadata column,ISet<string> resolving)
        =>IsPublishedScalar(catalog,storage,target,column,resolving,null,out _);

    /// <summary>需要重算副本时，在原发布证明栈中同时确认逻辑名称的实际页面范围。</summary>
    internal static bool IsPublishedScalar(MetadataCatalog catalog,MetadataStoragePlan storage,TableMetadata target,
        ColumnMetadata column,ISet<string> resolving,PageMetadata? page,out bool nestedScope)
    {
        nestedScope=false;
        if(IsScalar(storage,target,column))return true;
        if(target.IsRongyu==0&&column.ConfigTableId==target.Id&&!storage.IsPhysical(column))
        {
            var derivedKey=target.Id+":"+column.Id;
            if(resolving.Count>=16||!resolving.Add(derivedKey))throw new NotSupportedException("全局名称依赖存在循环或超过16层。");
            try{nestedScope=GlobalDerivedRelationSource.RequireName(catalog,target,column,resolving,page);return true;}
            finally{resolving.Remove(derivedKey);}
        }
        if(column.ConfigTableId!=target.Id||!storage.IsPhysical(column)||column.IsVirtualColumn!=0||(column.LogicalType!="string"&&!MultiValueLabel.IsTyped(column.LogicalType))||column.ControlKind is not("" or "TextArea")||ExactDataManageValues.IsSensitiveField(column.ColumnName)||!column.Extra.TryGetValue("isJoinColumn",out var cached)||!(cached.ValueKind==JsonValueKind.True||cached.ToString()=="1"))return false;
        var key=target.Id+":"+column.Id;
        if(resolving.Count>=16||!resolving.Add(key))throw new NotSupportedException("全局名称缓存依赖存在循环或超过16层。");
        try
        {
            // 物理外键、已发布目标页和精确名称副本由同一个证明核对。
            _=GlobalCachedRelationSource.RequireCache(catalog,target,column,resolving);return true;
        }
        finally{resolving.Remove(key);}
    }

}
