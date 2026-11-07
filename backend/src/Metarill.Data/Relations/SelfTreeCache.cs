using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;
namespace Metarill.Data.Relations;

/// <summary>R1自树只缓存真实叶子名称；固定控制定义同时约束传播及维护，路径展示仍由原树读取器完成。</summary>
public static class SelfTreeCache
{
    /// <summary>只读取发布目录，返回精确字段ID；逻辑名称须取得单值匹配证明，不接受请求SQL或动态页面。</summary>
    public static RelationCacheDefinition Compile(MetadataCatalog catalog,string sourceTableId,string controlId)
    {
        var source=catalog.ConfigTables.Single(t=>t.Id==sourceTableId);var sourceColumns=RelationStorageColumns.Expand(source,catalog.ConfigColumns.Where(c=>c.ConfigTableId==source.Id));var foreign=sourceColumns.Single(c=>c.Id==controlId);var sourceStorage=MetadataStoragePolicy.ForTable(catalog,source);
        if(source.IsRongyu!=1||source.IsView!=0||source.IsFunctionTable!=0||GlobalCrossTableTarget.Declared(source)||foreign.ControlKind!="CascadeTree"||foreign.LogicalType is not("string" or "int64")||!Scalar(sourceStorage,foreign)||!foreign.ColumnName.EndsWith("_id",StringComparison.Ordinal))throw Invalid();
        using var json=JsonDocument.Parse(foreign.ControlDetail);var root=json.RootElement;Object(root);if(Text(root,"sourceMode","")!="selfTree"||!root.TryGetProperty("selfTree",out var tree))throw Invalid();Object(tree);
        if(Extra(root)||Extra(tree)||Text(tree,"idField","id")!="id"||Text(tree,"rootMode","blank")!="blank"||Text(tree,"filterMatchMode","selfAndDescendants") is not("exact" or "selfAndDescendants"))throw Invalid();
        var target=catalog.ConfigTables.Single(t=>t.TableName==Text(tree,"table",""));if(target.IsRongyu is not(0 or 1)||target.IsView!=0||target.IsFunctionTable!=0||GlobalCrossTableTarget.Declared(target)||foreign.LinkConfigTableId.Length>0&&foreign.LinkConfigTableId!=target.Id)throw Invalid();
        var columns=RelationStorageColumns.Expand(target,catalog.ConfigColumns.Where(c=>c.ConfigTableId==target.Id));var storage=MetadataStoragePolicy.ForTable(catalog,target);var value=columns.Single(c=>c.ColumnName==Text(tree,"nameField","name"));var parent=columns.Single(c=>c.ColumnName==Text(tree,"parentField",""));
        var derived=!storage.IsPhysical(value);
        if(value.Id==parent.Id||value.LogicalType!="string"||(!derived&&!Scalar(storage,value))||derived&&(target.IsRongyu!=0||value.IsVirtualColumn!=0||storage.Kind(value)!=MetadataFieldStorage.Join)||parent.LogicalType is not("string" or "int64")||!Scalar(storage,parent)||parent.ColumnName is "id" or "yz_project_id" or "insert_manager_id" or "update_manager_id"||parent.LinkConfigTableId.Length>0&&parent.LinkConfigTableId!=target.Id)throw Invalid();
        // 派生叶子只接受同一匹配编译器证明的非凭据单值字符串，不把任意逻辑列当成物理缓存值。
        if(derived){var read=NonRedundantReads.CompileMatch(catalog,target.Id,value.ColumnName);if(read.OutputTypes[value.ColumnName]!="string")throw Invalid();}
        var name=foreign.ColumnName[..^3]+"_name";var cache=sourceColumns.Single(c=>c.ColumnName==name);if(cache.Id==foreign.Id||cache.LogicalType!="string"||cache.IsVirtualColumn!=0||!sourceStorage.IsPhysical(cache)||cache.ControlKind is not("" or "TextArea")||cache.LinkConfigTableId.Length>0||cache.Extra.GetValueOrDefault("joinTable").ToString().Length>0||cache.Extra.GetValueOrDefault("linkConfigPageId").ToString().Length>0)throw Invalid();
        if(Enabled(cache)){var path=cache.Extra.GetValueOrDefault("columnFullName").ToString();if(path.Length>0&&path!=foreign.ColumnName[..^3]+"."+value.ColumnName&&path!=foreign.ColumnName+"."+value.ColumnName)throw Invalid();}
        return new(source.Id,foreign.Id,target.Id,value.Id,cache.Id,false);
    }
    /// <summary>普通关系不变；只有明确selfTree声明可以采用固定_name而非目标字段后缀。</summary>
    internal static bool Require(MetadataCatalog catalog,TableMetadata source,ColumnMetadata foreign,TableMetadata target,ColumnMetadata value,ColumnMetadata cache)
    {
        if(foreign.ControlKind!="CascadeTree")return false;using var json=JsonDocument.Parse(foreign.ControlDetail);Object(json.RootElement);if(Text(json.RootElement,"sourceMode","crossTable")!="selfTree")return false;var proof=Compile(catalog,source.Id,foreign.Id);if(proof.TargetTableId!=target.Id||proof.ValueColumnId!=value.Id||proof.CacheColumnId!=cache.Id)throw Invalid();return true;
    }
    /// <summary>结构、ID及名称必须物理且非凭据；数组和联接不充当树拓扑。</summary>
    private static bool Scalar(MetadataStoragePlan storage,ColumnMetadata column)=>storage.IsPhysical(column)&&column.IsVirtualColumn==0&&!Enabled(column)&&!ExactDataManageValues.IsSensitiveField(column.ColumnName)&&column.ControlKind is not("FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "MultiList" or "CheckBoxList");
    /// <summary>固定定义保留排序与外观配置，不忽略附加查询或在线页映射。</summary>
    private static bool Extra(JsonElement value)=>new[]{"whereSql","where","sql","pageId"}.Any(k=>value.TryGetProperty(k,out _));
    /// <summary>重复键不能由JSON后值覆盖改变目标或字段。</summary>
    private static void Object(JsonElement value){if(value.ValueKind!=JsonValueKind.Object||value.EnumerateObject().Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=value.EnumerateObject().Count())throw Invalid();}
    /// <summary>只读取文本，拒绝把数值或对象隐式解释为标识符。</summary>
    private static string Text(JsonElement value,string key,string fallback)=>!value.TryGetProperty(key,out var item)||item.ValueKind==JsonValueKind.Null?fallback:item.ValueKind==JsonValueKind.String?string.IsNullOrWhiteSpace(item.GetString())?fallback:item.GetString()!:throw Invalid();
    /// <summary>联接标记沿既有true／1合同。</summary>
    private static bool Enabled(ColumnMetadata column)=>column.Extra.TryGetValue("isJoinColumn",out var value)&&(value.ValueKind==JsonValueKind.True||value.ToString()=="1");
    /// <summary>不将未知定义降级为普通外键。</summary>
    private static NotSupportedException Invalid()=>new("自树叶子名称缓存尚未取得固定物理字段证明。");
}
