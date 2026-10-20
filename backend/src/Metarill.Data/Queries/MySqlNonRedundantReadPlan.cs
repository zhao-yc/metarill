using System.Collections.ObjectModel;
using System.Text.Json;
using Metarill.Core.Metadata;

namespace Metarill.Data.Queries;

/// <summary>只由已发布元数据编译的单值读取计划，不提供SQL反序列化或任意表达式入口。</summary>
public sealed class MySqlNonRedundantReadPlan
{
    internal MySqlNonRedundantReadPlan(string from, string projectId, string scope, Dictionary<string,string> expressions,
        Dictionary<string,string> types, string[] outputs)
    { From=from; ProjectId=projectId; Scope=scope; Expressions=new ReadOnlyDictionary<string,string>(expressions); Types=new ReadOnlyDictionary<string,string>(types); Outputs=Array.AsReadOnly(outputs); }
    internal string From { get; }
    internal string Scope { get; }
    internal string ProjectId { get; }
    internal IReadOnlyDictionary<string,string> Expressions { get; }
    internal IReadOnlyDictionary<string,string> Types { get; }
    public IReadOnlyList<string> Outputs { get; }
    /// <summary>筛选和排序仅能引用本计划明确编译的字段，标识符不是客户端SQL。</summary>
    internal string Expression(string name)=>Expressions.TryGetValue(name,out var expression)?expression:throw new InvalidOperationException("读取字段未编译："+name);
}

/// <summary>首批实现单值关系与联接链；多值、级联和虚拟表达式继续由完整生成门槛阻止。</summary>
public static class MySqlNonRedundantReadCompiler
{
    /// <summary>输出由生成用途指定；依赖独立编译，不擅自扩成整表读取或读取权限。</summary>
    public static MySqlNonRedundantReadPlan Compile(MetadataCatalog catalog,string tableId,IReadOnlyList<string> outputNames,IReadOnlyList<string>? dependencies=null)
    {
        ArgumentNullException.ThrowIfNull(catalog); ArgumentNullException.ThrowIfNull(outputNames);
        MetadataValidation.Validate(catalog);
        if(!long.TryParse(catalog.YzProjectId,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var projectId)||projectId<=0)throw new InvalidOperationException("发布项目标识必须为正整数。");
        if(catalog.DatabaseProvider!="mysql")throw new NotSupportedException("当前固定读取执行仅完成MySQL适配。");
        if(outputNames.Count==0||outputNames.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=outputNames.Count)throw new InvalidOperationException("输出字段不能为空或重复。");
        var builder=new Builder(catalog,tableId);
        return builder.Compile(outputNames,dependencies??[]);
    }
    /// <summary>构造时消费元数据，最终只保留不可变的表达式文本、类型和项目范围。</summary>
    private sealed class Builder
    {
        private readonly MetadataCatalog catalog;
        private readonly Dictionary<string,TableMetadata> tables;
        private readonly Dictionary<string,Dictionary<string,ColumnMetadata>> columns;
        private readonly Dictionary<string,MetadataStoragePlan> storage;
        private readonly TableMetadata root;
        private readonly Dictionary<string,string> aliases=new(StringComparer.Ordinal);
        private readonly List<string> joins=[];
        private readonly HashSet<string> resolving=new(StringComparer.Ordinal);
        internal Builder(MetadataCatalog catalog,string tableId)
        {
            this.catalog=catalog;
            tables=catalog.ConfigTables.Where(table=>(table.YzProjectId.Length==0?catalog.YzProjectId:table.YzProjectId)==catalog.YzProjectId).ToDictionary(table=>table.Id,StringComparer.Ordinal);
            root=tables.GetValueOrDefault(tableId)??throw new InvalidOperationException("源表不属于当前项目。");
            if(root.IsRongyu!=0||root.IsFunctionTable!=0||root.IsView!=0)throw new InvalidOperationException("固定非冗余读取源必须是非冗余实体表。");
            if(tables.Values.GroupBy(table=>table.TableName,StringComparer.OrdinalIgnoreCase).Any(group=>group.Count()!=1))throw new InvalidOperationException("项目表名重复。");
            if(catalog.ConfigColumns.Any(column=>tables.ContainsKey(column.ConfigTableId)&&column.YzProjectId.Length>0&&column.YzProjectId!=catalog.YzProjectId))throw new InvalidOperationException("字段归属与发布项目不一致。");
            columns=tables.Values.ToDictionary(table=>table.Id,table=>catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id).ToDictionary(column=>column.ColumnName,StringComparer.OrdinalIgnoreCase),StringComparer.Ordinal);
            storage=tables.Values.ToDictionary(table=>table.Id,table=>MetadataStoragePolicy.ForTable(catalog,table),StringComparer.Ordinal);
        }
        /// <summary>主键作为稳定排序依赖；根及每个目标分别保留软删和项目范围。</summary>
        internal MySqlNonRedundantReadPlan Compile(IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies)
        {
            var id=Column(root,"id");if(id.LogicalType!="int64"||id.IsDbNotNull!=1||!storage[root.Id].IsPhysical(id))throw new InvalidOperationException("读取源缺少非空int64物理主键。");
            var expressions=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var types=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var name in outputs.Concat(dependencies).Append("id").Distinct(StringComparer.OrdinalIgnoreCase))
            {var resolved=Resolve(root,"b",name);expressions.Add(name,resolved.Sql);types.Add(name,resolved.Type);}
            var scope=Scope(root,"b");return new(" FROM "+Q(root.TableName)+" b"+string.Concat(joins),catalog.YzProjectId,scope,expressions,types,outputs.ToArray());
        }
        /// <summary>仅遍历真正的字段依赖，允许自关联但拒绝递归字段循环。</summary>
        private (string Sql,string Type) Resolve(TableMetadata table,string alias,string name)
        {
            MetadataValidation.RequireIdentifier(name);var key=table.Id+":"+name;
            if(!resolving.Add(key))throw new InvalidOperationException("非冗余字段存在循环依赖。");
            try
            {
                var column=columns[table.Id].GetValueOrDefault(name);
                if(column is not null)
                {
                    var kind=storage[table.Id].Kind(column);
                    if(kind==MetadataFieldStorage.Physical)return(alias+"."+Q(column.ColumnName),column.LogicalType);
                    if(kind==MetadataFieldStorage.Join)
                    {
                        var path=Text(column,"columnFullName").Split('.');if(path.Length<2||path.Any(string.IsNullOrWhiteSpace))throw new InvalidOperationException("联接字段缺少有效来源路径。");
                        var current=table;var currentAlias=alias;
                        foreach(var segment in path[..^1])
                        {MetadataValidation.RequireIdentifier(segment);var relation=columns[current.Id].GetValueOrDefault(segment+"_id")??columns[current.Id].GetValueOrDefault(segment)??throw new InvalidOperationException("联接路径关系不存在。");var target=Target(relation);var id=Resolve(current,currentAlias,relation.ColumnName);currentAlias=Join(currentAlias,relation,target,id.Sql);current=target;}
                        return Resolve(current,currentAlias,path[^1]);
                    }
                    if(kind is MetadataFieldStorage.Virtual or MetadataFieldStorage.Layout)throw new NotSupportedException("当前单值计划不支持虚拟或布局读取字段。");
                    if(name is "insert_manager_real_name" or "update_manager_real_name")
                    {var relation=Column(table,name.Replace("_real_name","_id",StringComparison.Ordinal));var target=tables.Values.SingleOrDefault(item=>item.TableName=="yz_manager")??throw new InvalidOperationException("缺少固定管理员基础设施元数据。");ValidateTarget(target);var id=Resolve(table,alias,relation.ColumnName);return Resolve(target,Join(alias,relation,target,id.Sql),"real_name");}
                }
                foreach(var relation in columns[table.Id].Values.Where(MetadataStoragePolicy.IsRelationColumn))
                {
                    var definition=Definition(relation);var display=Value(definition,"nameField");if(display.Length==0)display="name";
                    var auxiliary=relation.ColumnName.EndsWith("_id",StringComparison.Ordinal)&&display!="id"?relation.ColumnName[..^3]+"_"+display:"";
                    if(!name.Equals(auxiliary,StringComparison.OrdinalIgnoreCase))continue;
                    var target=Target(relation);var id=Resolve(table,alias,relation.ColumnName);return Resolve(target,Join(alias,relation,target,id.Sql),display);
                }
                throw new NotSupportedException("字段不是已支持的物理列或单值派生关系："+name);
            }
            finally{resolving.Remove(key);}
        }
        /// <summary>按原优先级解析明确单值关系，所有目标必须在同项目内。</summary>
        private TableMetadata Target(ColumnMetadata relation)
        {
            if(!MetadataStoragePolicy.IsRelationColumn(relation)||relation.ColumnName.EndsWith("_ids",StringComparison.Ordinal)||relation.ControlKind is "CascadeTree" or "MultiSelectPage" or "MultiCheckBoxDatabase")throw new NotSupportedException("当前路径仅支持明确单值关系。");
            var definition=Definition(relation);TableMetadata? target=null;
            var id=Value(definition,"tableId");if(id.Length>0)target=tables.GetValueOrDefault(id);
            var pageId=Value(definition,"pageId");if(target is null&&pageId.Length>0)target=PageTarget(pageId);
            if(target is null&&relation.LinkConfigTableId.Length>0)target=tables.GetValueOrDefault(relation.LinkConfigTableId);
            if(target is null&&Text(relation,"linkConfigPageId").Length>0)target=PageTarget(Text(relation,"linkConfigPageId"));
            if(target is null){var name=Value(definition,"table");if(name.Length==0)name=Text(relation,"joinTable");if(name.Length==0&&relation.ColumnName.EndsWith("_id",StringComparison.Ordinal))name=relation.ColumnName[..^3];target=tables.Values.SingleOrDefault(table=>table.TableName==name);}
            if(target is null)throw new InvalidOperationException("关联目标缺失或不是同项目实体表。");
            ValidateTarget(target);return target;
        }
        /// <summary>固定审计目标与业务关系使用相同实体主键约束，不能因固定表名绕过唯一性前提。</summary>
        private void ValidateTarget(TableMetadata target)
        {
            if(target.IsFunctionTable!=0||target.IsView!=0)throw new InvalidOperationException("关联目标必须为同项目实体表。");
            var key=Column(target,"id");if(key.LogicalType!="int64"||key.IsDbNotNull!=1||!storage[target.Id].IsPhysical(key))throw new InvalidOperationException("关联目标必须使用非空int64物理主键。");
        }
        /// <summary>同一路径复用别名，不同角色到同表和自关联分别命名。</summary>
        private string Join(string ownerAlias,ColumnMetadata relation,TableMetadata target,string source)
        {
            var path=ownerAlias+":"+relation.Id;
            if(aliases.TryGetValue(path,out var existing))return existing;
            var alias="r"+aliases.Count;aliases.Add(path,alias);var scope=Scope(target,alias);
            joins.Add(" LEFT JOIN "+Q(target.TableName)+" "+alias+" ON "+alias+"."+Q("id")+"="+source+(scope.Length==0?"":" AND "+scope));return alias;
        }
        /// <summary>范围字段必须是真实物理列；项目值固定绑定，不让筛选覆盖。</summary>
        private string Scope(TableMetadata table,string alias)
        {
            var predicates=new List<string>();foreach(var name in new[]{"delete_no","yz_project_id"})
            {
                if(!columns[table.Id].TryGetValue(name,out var column))continue;
                if(!storage[table.Id].IsPhysical(column))throw new InvalidOperationException("范围字段必须为物理列。");
                if(name=="delete_no"&&column.LogicalType!="datetime"||name=="yz_project_id"&&column.LogicalType is not("int64" or "string"))throw new InvalidOperationException("范围字段类型不匹配。");
                if(name=="yz_project_id"&&(!long.TryParse(catalog.YzProjectId,out var project)||project<=0))throw new InvalidOperationException("项目标识无效。");
                predicates.Add(alias+"."+Q(name)+(name=="delete_no"?" IS NULL":column.LogicalType=="int64"?"=@project_scope_number":"=@project_scope_text"));
            }
            return string.Join(" AND ",predicates);
        }
        /// <summary>选择页只提供目标归属，页面授权由既有HTTP入口建立。</summary>
        private TableMetadata? PageTarget(string pageId)=>catalog.ConfigPages.FirstOrDefault(page=>page.Id==pageId) is {} page?tables.GetValueOrDefault(page.ConfigTableId):null;
        /// <summary>读取固定元数据字段，不猜测缺失物理字段。</summary>
        private ColumnMetadata Column(TableMetadata table,string name)=>columns[table.Id].GetValueOrDefault(name)??throw new InvalidOperationException("缺少字段："+name);
    }
    /// <summary>控件定义仅作结构解析；旧式或重复JSON键必须明确拒绝。</summary>
    private static JsonElement Definition(ColumnMetadata column)
    {
        if(string.IsNullOrWhiteSpace(column.ControlDetail))return JsonSerializer.SerializeToElement(new{});
        using var document=JsonDocument.Parse(column.ControlDetail);var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||root.EnumerateObject().Select(property=>property.Name).Distinct(StringComparer.Ordinal).Count()!=root.EnumerateObject().Count())throw new InvalidOperationException("控件定义必须是无重复键JSON对象。");
        return root.Clone();
    }
    /// <summary>只读字符串结构键，不执行原生SQL配置。</summary>
    private static string Value(JsonElement value,string key)=>value.TryGetProperty(key,out var item)&&item.ValueKind==JsonValueKind.String?item.GetString()!:"";
    /// <summary>兼容扩展字段保留原文本值。</summary>
    private static string Text(ColumnMetadata column,string key)=>column.Extra.TryGetValue(key,out var value)?value.ToString():"";
    /// <summary>MySQL单段标识符严格白名单，方言集中在当前适配文件。</summary>
    private static string Q(string name){MetadataValidation.RequireIdentifier(name);return "`"+name+"`";}
}
