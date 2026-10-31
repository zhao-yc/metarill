using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
namespace Metarill.Data.Relations;

/// <summary>生成器只提供已证明关系的稳定字段ID，不提供SQL、物理名或请求可改写的计划。</summary>
public sealed record RelationCacheDefinition(string SourceTableId,string ForeignColumnId,string TargetTableId,string ValueColumnId,string CacheColumnId,bool Multiple)
{
    /// <summary>级联缓存必须绑定来源控制列，普通关系维持原六参数构造和JSON形状。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CrossTableColumnId{get;init;}
    /// <summary>重复表按稳定层标识区分角色，不按表名合并。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CrossTableLevelKey{get;init;}
    /// <summary>只为全局跨表层固定已发布目标页，普通关系JSON形状保持。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CrossTablePageId{get;init;}
    /// <summary>共享缓存去重后仍保留当前控制列全部全局层的冻结目标页，不从另一控件推断范围。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string,string>? CrossTableGlobalPages{get;init;}

}
internal sealed record RelationTable(string Name,string ProjectType,bool Deleted);
internal sealed record RelationCache(RelationTable Source,string Foreign,string ForeignType,RelationTable Target,string Value,string ValueType,string Cache,bool Multiple,int MaximumItems);

/// <summary>编译后只保留不可变标识符／类型与依赖，不再保存外部可变元数据。</summary>
public sealed class RelationMutationPlan
{
    internal IReadOnlyList<RelationCache> Bindings{get;}
    public string Fingerprint{get;}
    private RelationMutationPlan(string project,RelationCache[] bindings){Bindings=Array.AsReadOnly(bindings);Fingerprint=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{schemaVersion=1,project,provider="mysql",bindings}))));}
    /// <summary>同项目物理ID、项目归属、非虚拟缓存及准确路径必须在执行前完整证明。</summary>
    public static RelationMutationPlan Compile(MetadataCatalog catalog,IReadOnlyList<RelationCacheDefinition> definitions)
    {
        MetadataValidation.Validate(catalog);if(catalog.DatabaseProvider!="mysql")throw new NotSupportedException("该方言尚未完成关系传播验收。");
        var tables=catalog.ConfigTables.ToDictionary(table=>table.Id,StringComparer.Ordinal);var columns=tables.Values.ToDictionary(table=>table.Id,table=>RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id)).ToDictionary(column=>column.Id,StringComparer.Ordinal),StringComparer.Ordinal);var result=new List<RelationCache>();
        RelationTable Table(string id,bool globalTarget=false)
        {
            var table=tables[id];if(table.IsFunctionTable!=0||table.IsView!=0||!globalTarget&&table.Extra.GetValueOrDefault("scopeKind").ToString()=="global"||table.YzProjectId.Length>0&&table.YzProjectId!=catalog.YzProjectId)throw new InvalidOperationException("传播表必须是同一发布项目的普通实体表。");
            var plan=MetadataStoragePolicy.ForTable(catalog,table);var key=columns[id].Values.SingleOrDefault(column=>column.ColumnName=="id");var project=columns[id].Values.SingleOrDefault(column=>column.ColumnName=="yz_project_id");
            if(key is null||key.LogicalType!="int64"||key.IsDbNotNull!=1||!plan.IsPhysical(key)||Enabled(key)||!globalTarget&&project is null||project is not null&&(project.LogicalType is not("int64" or "string")||!plan.IsPhysical(project)||Enabled(project)))throw new InvalidOperationException("传播表缺少真实主键或项目归属。");
            var deleted=columns[id].Values.SingleOrDefault(column=>column.ColumnName=="delete_no");if(deleted is not null&&(!plan.IsPhysical(deleted)||Enabled(deleted)||deleted.LogicalType!="datetime"||deleted.IsDbNotNull!=0))throw new InvalidOperationException("传播软删除定义不明确。");return new(table.TableName,globalTarget?"":project!.LogicalType,deleted is not null);
        }
        foreach(var definition in definitions)
        {
            if(!tables.TryGetValue(definition.SourceTableId,out var source)||!tables.TryGetValue(definition.TargetTableId,out var target))throw new InvalidOperationException("传播关系引用了未发布的表。");
            var foreign=columns[source.Id].GetValueOrDefault(definition.ForeignColumnId);var value=columns[target.Id].GetValueOrDefault(definition.ValueColumnId);var cache=columns[source.Id].GetValueOrDefault(definition.CacheColumnId);
            if(foreign is null||value is null||cache is null||foreign.Id==cache.Id||source.IsRongyu!=1)throw new InvalidOperationException("传播缓存必须属于冗余消费者的独立字段。");
            var storage=MetadataStoragePolicy.ForTable(catalog,source);var targetStorage=MetadataStoragePolicy.ForTable(catalog,target);
            if(!storage.IsPhysical(foreign)||Enabled(foreign)||!storage.IsPhysical(cache)||cache.IsVirtualColumn!=0||!targetStorage.IsPhysical(value)||value.IsVirtualColumn!=0)throw new InvalidOperationException("传播只写已声明物理缓存，关系ID与目标值不能虚构。");
            if(value.ControlKind is "FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase"||value.LogicalType is not("string" or "int64" or "int32" or "decimal" or "bool" or "date" or "datetime" or "time")||cache.LogicalType!= (definition.Multiple?"string":value.LogicalType)||foreign.LogicalType is not("string" or "int64"))throw new InvalidOperationException("传播缓存与目标值类型不一致。");
            if(target.TableName=="yz_manager"&&value.ColumnName is "password" or "salt" or "old_password")throw new InvalidOperationException("账号凭据不能成为名称传播值。");
            var suffix=definition.Multiple?"_ids":"_id";if(!foreign.ColumnName.EndsWith(suffix,StringComparison.Ordinal)||definition.Multiple&&(foreign.LogicalType!="string"||value.LogicalType!="string"||foreign.ControlKind is not("MultiSelectPage" or "MultiCheckBoxDatabase")))throw new InvalidOperationException("传播外键形状与声明不一致。");
            if(foreign.LinkConfigTableId.Length>0&&foreign.LinkConfigTableId!=target.Id)throw new InvalidOperationException("传播外键目标与发布关系不一致。");
            var stem=foreign.ColumnName[..^suffix.Length];var expected=stem+"_"+(definition.Multiple&&!value.ColumnName.EndsWith('s')?value.ColumnName+"s":value.ColumnName);
            var cross=CrossTableCacheProof.Require(catalog,source,foreign,target,value,cache,definition,definitions);
            var selfTree=!cross&&SelfTreeCache.Require(catalog,source,foreign,target,value,cache);
            if((!cross&&!selfTree&&cache.ColumnName!=expected)||cache.ControlKind is not("" or "TextArea")||cache.LinkConfigTableId.Length>0||cache.Extra.GetValueOrDefault("joinTable").ToString().Length>0||cache.Extra.GetValueOrDefault("linkConfigPageId").ToString().Length>0)throw new InvalidOperationException("传播字段不是同一关系的名称辅助列。");
            if(Enabled(cache)){var path=cache.Extra.GetValueOrDefault("columnFullName").ToString();if(!(selfTree&&path.Length==0)&&path!=stem+"."+value.ColumnName&&path!=foreign.ColumnName+"."+value.ColumnName)throw new InvalidOperationException("传播缓存的联接路径不一致。");}
            foreach(var field in new[]{foreign,value,cache})if(field.YzProjectId.Length>0&&field.YzProjectId!=catalog.YzProjectId)throw new InvalidOperationException("传播字段超出发布项目。");
            var global=GlobalCrossTableTarget.Declared(target);
            if(global)
            {
                if(cross){if(definition.Multiple)throw new InvalidOperationException("跨表缓存不能冒充多值。");}
                else if(definition.Multiple){var proof=GlobalMultipleRelationTarget.RequireCached(catalog,source,foreign,target);if(proof.Name.Id!=value.Id)throw new InvalidOperationException("全局多值缓存名称与固定关系声明不一致。");}else _=GlobalSingleRelationTarget.RequireCached(catalog,source,foreign,target);
                // 普通单值可引用已证明的全局名称副本；跨表和多值仍保持原物理终点门槛。
                var published=!cross&&!definition.Multiple?GlobalPhysicalRelationValue.IsPublishedScalar(catalog,targetStorage,target,value):GlobalPhysicalRelationValue.IsScalar(targetStorage,target,value);
                if(!published)throw new NotSupportedException("全局缓存终点必须为非凭据物理标量或已证明的全局单值名称缓存。");
            }
            result.Add(new(Table(source.Id),foreign.ColumnName,foreign.LogicalType,Table(target.Id,global),value.ColumnName,value.LogicalType,cache.ColumnName,definition.Multiple,foreign.ControlKind=="MultiSelectPage"?200:5000));
        }
        var sorted=result.Distinct().OrderBy(binding=>binding.Source.Name,StringComparer.Ordinal).ThenBy(binding=>binding.Cache,StringComparer.Ordinal).ToArray();if(sorted.GroupBy(binding=>(binding.Source.Name,binding.Foreign)).Any(group=>group.Select(binding=>binding.Target).Distinct().Count()!=1))throw new InvalidOperationException("同一外键不能绑定不同传播目标。");if(sorted.GroupBy(binding=>(binding.Source.Name,binding.Cache)).Any(group=>group.Count()!=1))throw new InvalidOperationException("同一缓存存在多个传播定义。");
        // 拒绝字段级循环；普通自关联的物理名称不是派生列，不属于此循环。
        var byCache=sorted.ToDictionary(binding=>binding.Source.Name+"."+binding.Cache,StringComparer.Ordinal);var active=new HashSet<string>(StringComparer.Ordinal);var visited=new HashSet<string>(StringComparer.Ordinal);
        void Visit(string key){if(visited.Contains(key))return;if(!active.Add(key))throw new InvalidOperationException("传播派生字段存在循环依赖。");var binding=byCache[key];var dependency=binding.Target.Name+"."+binding.Value;if(byCache.ContainsKey(dependency))Visit(dependency);active.Remove(key);visited.Add(key);}
        foreach(var key in byCache.Keys)Visit(key);return new(catalog.YzProjectId,sorted);
    }
    /// <summary>沿既有1/true标志，不能把普通文本当作受管联接。</summary>
    private static bool Enabled(ColumnMetadata column)=>column.Extra.TryGetValue("isJoinColumn",out var value)&&(value.ValueKind==JsonValueKind.True||value.ToString()=="1");
}
