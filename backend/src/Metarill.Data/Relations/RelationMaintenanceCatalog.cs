using System.Collections.ObjectModel;
using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;
using Metarill.Data.Schema;
namespace Metarill.Data.Relations;

public sealed record MaintenanceTableOption(string Id,string Value,string Name,string Text,string RawName,string TableName);
public sealed record MaintenanceColumnResult(string ColumnName,string ColumnFullName,string RelationColumnName,string TargetTableName,string TargetColumnName,int AffectedRows,string? DisplayColumnName=null,string? TargetDisplayColumnName=null);
public sealed record MaintenanceResult(string ConfigTableId,string Name,string TableName,int JoinColumnCount,int AffectedRows,IReadOnlyList<MaintenanceColumnResult> Details);
internal sealed record MaintenanceField(string Name,string Path,string Relation,string TargetTable,string TargetColumn,string? DisplayOf=null);
internal sealed record MaintenanceMultiple(string Foreign,int MaximumItems,IReadOnlyList<string> Caches);
internal sealed record MaintenanceTable(MaintenanceTableOption Option,string ProjectType,bool Deleted,IReadOnlyList<MaintenanceField> Fields,IReadOnlyList<MaintenanceMultiple> Multiples,MySqlNonRedundantReadPlan? Read,string? Error);

/// <summary>从发布目录冻结原维护下拉、按钮约束和整表回填计划；HTTP只能选择已登记表ID。</summary>
public sealed class RelationMaintenanceCatalog
{
    public string ProjectId{get;}
    public string Fingerprint{get;}
    public IReadOnlyList<MaintenanceTableOption> Tables{get;}
    public IReadOnlySet<string> AdminTypeIds{get;}
    public IReadOnlyDictionary<string,IReadOnlyList<string>> Buttons{get;}
    internal IReadOnlyDictionary<string,MaintenanceTable> Plans{get;}
    internal RelationMutationPlan Propagation{get;}
    private RelationMaintenanceCatalog(MetadataCatalog catalog,RelationCacheDefinition[] definitions)
    {
        MetadataValidation.Validate(catalog);if(catalog.DatabaseProvider!="mysql")throw new NotSupportedException("维护回填尚未完成该方言验收。");ProjectId=catalog.YzProjectId;
        if(!long.TryParse(ProjectId,out var project)||project<=0)throw new InvalidOperationException("维护发布项目标识无效。");
        Propagation=RelationMutationPlan.Compile(catalog,definitions);AdminTypeIds=catalog.YzAdminTypes.Select(item=>item.Id).ToFrozenSet(StringComparer.Ordinal);
        Buttons=new ReadOnlyDictionary<string,IReadOnlyList<string>>(new[]{"tables","backfill_join_columns"}.ToDictionary(action=>action,action=>(IReadOnlyList<string>)Array.AsReadOnly(catalog.ConfigButtons.Where(item=>item.TryGetProperty("sql",out var sql)&&sql.GetString()=="/admin/sys/system_maintenance/"+action).Select(item=>item.GetProperty("id").ToString()).Distinct(StringComparer.Ordinal).ToArray()),StringComparer.Ordinal));
        var plans=new Dictionary<string,MaintenanceTable>(StringComparer.Ordinal);
        foreach(var table in catalog.ConfigTables.Where(table=>table.IsRongyu==1&&(table.YzProjectId.Length==0||table.YzProjectId==ProjectId)).OrderBy(table=>table.Name,StringComparer.Ordinal).ThenBy(table=>table.Id,StringComparer.Ordinal))
        {
            var display=table.Name+(table.TableName.Length>0?"（"+table.TableName+"）":"");var option=new MaintenanceTableOption(table.Id,table.Id,display,display,table.Name,table.TableName);
            try
            {
                var storage=MetadataStoragePolicy.ForTable(catalog,table);var all=storage.PhysicalColumns.ToDictionary(column=>column.ColumnName,StringComparer.Ordinal);var bindings=definitions.Where(item=>item.SourceTableId==table.Id).ToArray();
                var fields=RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id)).Where(column=>Enabled(column)||bindings.Any(item=>item.CacheColumnId==column.Id)).OrderBy(column=>column.ColumnName,StringComparer.Ordinal).ToArray();
                var companionPaths=new Dictionary<string,string>(StringComparer.Ordinal);var companions=new Dictionary<string,string>(StringComparer.Ordinal);
                foreach(var field in fields.Where(field=>Enabled(field)&&field.ColumnName.EndsWith("_id",StringComparison.Ordinal)))
                {
                    var displayField="name";if(field.ControlKind is "List" or "SelectPage"&&!string.IsNullOrWhiteSpace(field.ControlDetail)){using var detail=JsonDocument.Parse(field.ControlDetail);if(detail.RootElement.TryGetProperty("nameField",out var declaredName))displayField=declaredName.GetString()??"name";}
                    if(displayField=="id")continue;MetadataValidation.RequireIdentifier(displayField);var auxiliary=field.ColumnName[..^3]+"_"+displayField;var path=field.Extra.GetValueOrDefault("columnFullName").ToString();var dot=path.LastIndexOf('.');
                    if(!all.ContainsKey(auxiliary)||fields.Any(item=>item.ColumnName==auxiliary))continue;if(dot<0||!path[(dot+1)..].EndsWith("_id",StringComparison.Ordinal))throw new NotSupportedException("联接ID的辅助显示列缺少明确来源。");
                    var targetDisplay=path[..(path.Length-3)]+"_"+displayField;companionPaths.Add(auxiliary,targetDisplay);companions.Add(auxiliary,field.ColumnName);
                }
                fields=fields.Concat(companionPaths.Keys.Select(name=>all[name])).OrderBy(field=>field.ColumnName,StringComparer.Ordinal).ToArray();
                if(fields.Any(field=>!storage.IsPhysical(field)))throw new NotSupportedException("回填列必须全部具有真实物理存储，未执行部分回填。");
                if(fields.Length==0){plans.Add(table.Id,new(option,"",false,[],[],null,null));continue;}
                if(table.IsFunctionTable!=0||table.IsView!=0||table.Extra.GetValueOrDefault("scopeKind").ToString()=="global")throw new NotSupportedException("维护回填要求同项目普通实体表。");
                if(!all.TryGetValue("yz_project_id",out var projectColumn)||projectColumn.LogicalType is not("int64" or "string")||Enabled(projectColumn))throw new InvalidOperationException("维护表缺少真实项目列。");
                foreach(var field in fields)if(field.ColumnName is "id" or "yz_project_id" or "delete_no" or "insert_time" or "update_time" or "insert_manager_id" or "update_manager_id"||ExactDataManageValues.IsSensitiveField(field.ColumnName)||field.ColumnName=="salt"||field.ControlKind=="FileLibrary"||field.IsVirtualColumn!=0)throw new NotSupportedException("维护回填不能覆盖身份、审计或凭据字段。");
                var read=MySqlNonRedundantReadCompiler.CompileRefresh(catalog,table.Id,fields.Select(column=>column.ColumnName).ToArray(),bindings,companionPaths);var results=new List<MaintenanceField>();
                if(read.MultiProjections.Keys.Any(name=>!bindings.Any(binding=>all.Values.Any(column=>column.Id==binding.CacheColumnId&&column.ColumnName==name))))throw new NotSupportedException("多值回填缺少已证明的数组预算，未执行部分回填。");
                foreach(var field in fields)
                {
                    if(!read.Origins.TryGetValue(field.ColumnName,out var origin)||read.Types[field.ColumnName]!=field.LogicalType)throw new NotSupportedException("回填列与最终派生标量类型不一致。");
                    var binding=bindings.SingleOrDefault(item=>item.CacheColumnId==field.Id);var foreign=binding is null?"":all.Values.Single(column=>column.Id==binding.ForeignColumnId).ColumnName;var path=binding is null?companionPaths.GetValueOrDefault(field.ColumnName)??field.Extra.GetValueOrDefault("columnFullName").ToString():foreign+"."+origin.Column;
                    if(foreign.Length==0){var segment=path.Split('.')[0];foreign=all.ContainsKey(segment+"_id")?segment+"_id":segment;}
                    results.Add(new(field.ColumnName,path,foreign,origin.Table,origin.Column,companions.GetValueOrDefault(field.ColumnName)));
                }
                var required=read.PhysicalReferences.Concat(fields.Select(field=>new MySqlPhysicalReference(table.TableName,MySqlSchemaPlanner.CompileColumn(field)))).DistinctBy(item=>(item.Table,item.Column.Name)).ToArray();
                read=new(read.From,read.ProjectId,read.Scope,read.Expressions.ToDictionary(),read.Types.ToDictionary(),read.Outputs.ToArray(),read.ArrayNamePredicates.ToDictionary(),read.MultiProjections.ToDictionary(),read.ArrayIdPredicates.ToDictionary(),Array.AsReadOnly(required),read.Origins);
                var multiples=bindings.GroupBy(item=>item.ForeignColumnId).Select(group=>new MaintenanceMultiple(all.Values.Single(column=>column.Id==group.Key).ColumnName,all.Values.Single(column=>column.Id==group.Key).ControlKind=="MultiSelectPage"?200:5000,Array.AsReadOnly(group.Select(item=>all.Values.Single(column=>column.Id==item.CacheColumnId).ColumnName).ToArray()))).ToArray();
                plans.Add(table.Id,new(option,projectColumn.LogicalType,all.ContainsKey("delete_no"),Array.AsReadOnly(results.ToArray()),Array.AsReadOnly(multiples),read,null));
            }
            catch(Exception error)when(error is InvalidOperationException or ArgumentException or NotSupportedException or JsonException){plans.Add(table.Id,new(option,"",false,[],[],null,"当前表回填计划未通过发布校验："+error.Message));}
        }
        Plans=new ReadOnlyDictionary<string,MaintenanceTable>(plans);Tables=Array.AsReadOnly(plans.Values.Select(item=>item.Option).ToArray());
        Fingerprint=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{catalog,definitions}))));
    }
    /// <summary>输入只在编译时使用；后续编辑或重排目录不会改变当前宿主的维护权威。</summary>
    public static RelationMaintenanceCatalog Compile(MetadataCatalog catalog,IReadOnlyList<RelationCacheDefinition>? multiple=null)=>new(catalog,(multiple??[]).ToArray());
    /// <summary>沿现有扩展开关，不把任意非空字符串当作联接声明。</summary>
    private static bool Enabled(ColumnMetadata column)=>column.Extra.TryGetValue("isJoinColumn",out var value)&&(value.ValueKind==JsonValueKind.True||value.ToString()=="1");
}
