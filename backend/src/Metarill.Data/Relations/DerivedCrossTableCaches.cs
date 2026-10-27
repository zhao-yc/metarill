using Metarill.Core.Metadata;
using Metarill.Core.Security;
namespace Metarill.Data.Relations;

/// <summary>已证明的跨表、自树及多选派生缓存进入完整维护／混合队列，物理传播器不处理逻辑字段。</summary>
public static class DerivedCrossTableCaches
{
    /// <summary>逐项验证来源、固定层与R0派生终点；只编译，不连接业务库。</summary>
    public static void Validate(MetadataCatalog catalog,IReadOnlyList<RelationCacheDefinition> derived,IReadOnlyList<RelationCacheDefinition> physical)
    {
        MetadataValidation.Validate(catalog);if(catalog.DatabaseProvider!="mysql")throw new NotSupportedException("派生跨表缓存尚未完成此方言验收。");var all=physical.Concat(derived).ToArray();
        foreach(var d in derived)
        {
            var source=catalog.ConfigTables.SingleOrDefault(t=>t.Id==d.SourceTableId)??throw new InvalidOperationException("跨表来源未发布。");var target=catalog.ConfigTables.SingleOrDefault(t=>t.Id==d.TargetTableId)??throw new InvalidOperationException("派生目标未发布。");
            if(source.IsRongyu!=1||target.IsRongyu!=0||GlobalCrossTableTarget.Declared(source)||GlobalCrossTableTarget.Declared(target)||source.IsView!=0||source.IsFunctionTable!=0||target.IsView!=0||target.IsFunctionTable!=0)throw new InvalidOperationException("派生跨表缓存要求普通R1来源及R0单值目标。");
            var sourceStorage=MetadataStoragePolicy.ForTable(catalog,source);var targetStorage=MetadataStoragePolicy.ForTable(catalog,target);var columns=RelationStorageColumns.Expand(source,catalog.ConfigColumns.Where(c=>c.ConfigTableId==source.Id));var foreign=columns.SingleOrDefault(c=>c.Id==d.ForeignColumnId);var cache=columns.SingleOrDefault(c=>c.Id==d.CacheColumnId);var value=catalog.ConfigColumns.SingleOrDefault(c=>c.ConfigTableId==target.Id&&c.Id==d.ValueColumnId);
            if(foreign is null||cache is null||value is null||foreign.Id==cache.Id||!sourceStorage.IsPhysical(foreign)||Enabled(foreign)||!sourceStorage.IsPhysical(cache)||cache.IsVirtualColumn!=0||cache.LogicalType!="string"||foreign.LogicalType is not("string" or "int64")||targetStorage.IsPhysical(value)||value.IsVirtualColumn!=0||targetStorage.Kind(value)!=MetadataFieldStorage.Join)throw new InvalidOperationException("派生跨表关系ID、缓存或逻辑名称不明确。");
            if(cache.ControlKind is not("" or "TextArea")||cache.LinkConfigTableId.Length>0||cache.Extra.GetValueOrDefault("joinTable").ToString().Length>0||cache.Extra.GetValueOrDefault("linkConfigPageId").ToString().Length>0||ExactDataManageValues.IsSensitiveField(cache.ColumnName)||foreign.LinkConfigTableId.Length>0&&foreign.LinkConfigTableId!=target.Id)throw new InvalidOperationException("派生跨表缓存不属于同一固定关系。");
            foreach(var table in new[]{source,target})
            {
                if(table.YzProjectId.Length>0&&table.YzProjectId!=catalog.YzProjectId)throw new InvalidOperationException("派生跨表实体超出发布项目。");var storage=MetadataStoragePolicy.ForTable(catalog,table);var members=RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(c=>c.ConfigTableId==table.Id));var key=members.SingleOrDefault(c=>c.ColumnName=="id");var project=members.SingleOrDefault(c=>c.ColumnName=="yz_project_id");var deleted=members.SingleOrDefault(c=>c.ColumnName=="delete_no");if(key is null||key.LogicalType!="int64"||key.IsDbNotNull!=1||!storage.IsPhysical(key)||Enabled(key)||project is null||project.LogicalType is not("int64" or "string")||!storage.IsPhysical(project)||Enabled(project)||deleted is not null&&(deleted.LogicalType!="datetime"||deleted.IsDbNotNull!=0||!storage.IsPhysical(deleted)||Enabled(deleted)))throw new InvalidOperationException("派生跨表实体缺少真实身份或活动范围。");
            }
            foreach(var field in new[]{foreign,cache,value})if(field.YzProjectId.Length>0&&field.YzProjectId!=catalog.YzProjectId)throw new InvalidOperationException("派生跨表字段超出发布项目。");
            var cross=CrossTableCacheProof.Require(catalog,source,foreign,target,value,cache,d,all);var self=!cross&&!d.Multiple&&SelfTreeCache.Require(catalog,source,foreign,target,value,cache);if(d.Multiple)DerivedMultipleCacheProof.Require(catalog,source,foreign,target,value,cache);else if(!cross&&!self)throw new InvalidOperationException("逻辑缓存缺少跨表层或固定自树证明。");
            if(Enabled(cache)){var stem=foreign.ColumnName[..(foreign.ColumnName.Length-(d.Multiple?4:3))];var path=cache.Extra.GetValueOrDefault("columnFullName").ToString();if(!(self&&path.Length==0)&&path!=stem+"."+value.ColumnName&&path!=foreign.ColumnName+"."+value.ColumnName)throw new InvalidOperationException("派生级联缓存路径与固定定义不一致。");}
        }
        if(all.GroupBy(d=>(d.SourceTableId,d.CacheColumnId)).Any(g=>g.Distinct().Count()!=1))throw new InvalidOperationException("同一缓存存在相互冲突的物理／派生绑定。");
    }
    /// <summary>只沿原明确联接标志，不把任意文本解释为关系。</summary>
    private static bool Enabled(ColumnMetadata c)=>c.Extra.TryGetValue("isJoinColumn",out var value)&&(value.ToString()=="1"||value.ValueKind==System.Text.Json.JsonValueKind.True);
}
