using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
namespace Metarill.Data.Relations;

/// <summary>级联固定名称缓存使用控制列及层标识证明，不能把任意标量副本冒充普通名称辅助列。</summary>
internal static class CrossTableCacheProof
{
    /// <summary>完整检查固定链，返回所选层是否为物理字符串名称缓存；不读取业务库或接受SQL。</summary>
    internal static bool Require(MetadataCatalog catalog,TableMetadata source,ColumnMetadata foreign,TableMetadata target,ColumnMetadata value,ColumnMetadata cache,RelationCacheDefinition definition,IReadOnlyList<RelationCacheDefinition> definitions)
    {
        if(definition.CrossTableColumnId is null&&definition.CrossTableLevelKey is null){if(definition.CrossTablePageId is not null)throw new InvalidOperationException("目标页不能脱离完整跨表控制列及层位。");return false;}
        if(definition.Multiple||string.IsNullOrWhiteSpace(definition.CrossTableColumnId)||string.IsNullOrWhiteSpace(definition.CrossTableLevelKey))throw new InvalidOperationException("跨表名称缓存缺少完整控制列及层标识。");
        var control=catalog.ConfigColumns.SingleOrDefault(x=>x.ConfigTableId==source.Id&&x.Id==definition.CrossTableColumnId);if(control is null||control.ControlKind!="CascadeTree"||control.IsVirtualColumn!=0||control.LogicalType is not("string" or "int64")||Enabled(control)||!MetadataStoragePolicy.ForTable(catalog,source).IsPhysical(control))throw new InvalidOperationException("跨表名称缓存控制列不是来源的真实级联ID。");
        using var json=JsonDocument.Parse(control.ControlDetail);var root=json.RootElement;Object(root);if(Text(root,"sourceMode","crossTable")!="crossTable"||Extra(root)||!root.TryGetProperty("levels",out var levels)||levels.ValueKind!=JsonValueKind.Array||levels.GetArrayLength()<2)throw new InvalidOperationException("跨表名称缓存必须使用完整固定层级。");
        var keys=new HashSet<string>(StringComparer.Ordinal);var prefixes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var found=false;var last="";TableMetadata? previous=null;
        foreach(var level in levels.EnumerateArray())
        {
            Object(level);if(Extra(level)||Text(level,"idField","id")!="id")throw new InvalidOperationException("跨表名称缓存不接受额外查询范围或非id主键。");var tableName=Text(level,"table","");var prefix=Text(level,"columnPrefix",tableName);var key=Text(level,"key",tableName);var field=Text(level,"nameField","name");MetadataValidation.RequireIdentifier(tableName);MetadataValidation.RequireIdentifier(prefix);MetadataValidation.RequireIdentifier(field);if(!keys.Add(key)||!prefixes.Add(prefix)||prefix is "yz_project" or "insert_manager" or "update_manager" or "insert_user" or "update_user")throw new InvalidOperationException("跨表名称缓存层标识或前缀重复。");
            var node=catalog.ConfigTables.SingleOrDefault(x=>x.TableName==tableName&&(x.YzProjectId.Length==0||x.YzProjectId==catalog.YzProjectId))??throw new InvalidOperationException("跨表名称缓存目标不属于发布项目。");var storage=MetadataStoragePolicy.ForTable(catalog,node);var columns=RelationStorageColumns.Expand(node,catalog.ConfigColumns.Where(x=>x.ConfigTableId==node.Id));var name=columns.SingleOrDefault(x=>x.ColumnName==field);if(node.IsRongyu!=1||node.IsFunctionTable!=0||node.IsView!=0||name is null||name.LogicalType!="string"||!storage.IsPhysical(name)||Enabled(name)||ExactDataManageValues.IsSensitiveField(name.ColumnName)||name.ControlKind is "FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "CheckBoxList" or "MultiList")throw new InvalidOperationException("跨表名称缓存终点必须为普通同项目物理字符串。");
            if(GlobalCrossTableTarget.Declared(node))
            {
                // 每个全局层必须由同一控制列的精确缓存定义绑定唯一页；不能通过另一来源或请求补页。
                if(source.YzProjectId!=catalog.YzProjectId||GlobalCrossTableTarget.Declared(source))throw new InvalidOperationException("全局跨表缓存来源必须明确发布为普通项目实体。");
                var pages=definitions.Where(item=>item.SourceTableId==source.Id&&item.CrossTableColumnId==control.Id&&item.CrossTableLevelKey==key&&item.TargetTableId==node.Id&&item.ValueColumnId==name.Id).Select(item=>item.CrossTablePageId).Distinct(StringComparer.Ordinal).ToArray();
                if(pages.Length!=1||string.IsNullOrWhiteSpace(pages[0]))throw new InvalidOperationException("全局跨表缓存缺少唯一冻结目标页。");
                var page=catalog.ConfigPages.SingleOrDefault(item=>item.Id==pages[0])??throw new InvalidOperationException("全局跨表缓存目标页未发布。");GlobalCrossTableTarget.Require(catalog,node,page);
                if(!GlobalPhysicalRelationValue.IsScalar(storage,node,name))throw new InvalidOperationException("全局跨表名称必须为非凭据物理标量。");
            }
            else if(key==definition.CrossTableLevelKey&&definition.CrossTablePageId is not null)throw new InvalidOperationException("普通跨表层不能冒用全局目标页证明。");
            if(previous is not null){var parent=columns.SingleOrDefault(x=>x.ColumnName==Text(level,"parentField",""));if(parent is null||parent.ColumnName is "id" or "yz_project_id" or "insert_manager_id" or "update_manager_id" or "insert_user_id" or "update_user_id"||parent.ControlKind is "FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "CheckBoxList" or "MultiList"||parent.LogicalType is not("string" or "int64")||!storage.IsPhysical(parent)||Enabled(parent)||parent.LinkConfigTableId.Length>0&&parent.LinkConfigTableId!=previous.Id)throw new InvalidOperationException("跨表名称缓存父键不是已声明物理关系。");}
            if(key==definition.CrossTableLevelKey){if(foreign.Id!=control.Id&&foreign.ControlKind.Length>0||foreign.ColumnName!=prefix+"_id"||cache.ColumnName!=prefix+"_name"||target.Id!=node.Id||value.Id!=name.Id)throw new InvalidOperationException("跨表名称缓存字段与固定层声明不一致。");found=true;}
            previous=node;last=prefix;
        }
        if(!found||control.ColumnName!=last+"_id")throw new InvalidOperationException("跨表名称缓存层位或末级列不匹配。");return true;
    }
    /// <summary>重复JSON键不可依赖后值覆盖。</summary>
    private static void Object(JsonElement value){if(value.ValueKind!=JsonValueKind.Object||value.EnumerateObject().Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()!=value.EnumerateObject().Count())throw new InvalidOperationException("跨表名称缓存定义必须是无重复键对象。");}
    /// <summary>固定查询以外的范围必须独立证明，不静默忽略。</summary>
    private static bool Extra(JsonElement value)=>new[]{"whereSql","where","sql","pageId","filterMatchMode"}.Any(x=>value.TryGetProperty(x,out _));
    /// <summary>结构文本不接受数值或对象转换。</summary>
    private static string Text(JsonElement value,string key,string fallback)=>!value.TryGetProperty(key,out var item)||item.ValueKind==JsonValueKind.Null?fallback:item.ValueKind==JsonValueKind.String?string.IsNullOrWhiteSpace(item.GetString())?fallback:item.GetString()!:throw new InvalidOperationException("跨表名称缓存配置必须为文本。");
    /// <summary>只识别现有明确联接标记。</summary>
    private static bool Enabled(ColumnMetadata column)=>column.Extra.TryGetValue("isJoinColumn",out var value)&&(value.ValueKind==JsonValueKind.True||value.ToString()=="1");
}
