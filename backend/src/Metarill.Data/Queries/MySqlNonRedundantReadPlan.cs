using System.Collections.ObjectModel;
using System.Collections.Frozen;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Relations;

namespace Metarill.Data.Queries;

/// <summary>多值投影只持有编译后的标识符与固定范围，执行阶段不再读取元数据。</summary>
internal sealed record MySqlPhysicalReference(string Table,Metarill.Data.Schema.ColumnSchema Column);
internal sealed record MySqlValueOrigin(string Table,string Column);
internal sealed record MySqlReverseProjection(string TargetTable,string From,string ForeignExpression,string ForeignType,IReadOnlyDictionary<string,IReadOnlySet<string>> Outputs);

internal sealed record MySqlMultiProjection(string SourceExpression,string TargetTable,string TargetName,string TargetScope,string Joins="",string? NameExpression=null);

/// <summary>只由已发布元数据编译的单值读取计划，不提供SQL反序列化或任意表达式入口。</summary>
public sealed class MySqlNonRedundantReadPlan
{
    internal MySqlNonRedundantReadPlan(string from, string projectId, string scope, Dictionary<string,string> expressions,
        Dictionary<string,string> types, string[] outputs, Dictionary<string,string> arrayNamePredicates, Dictionary<string,MySqlMultiProjection> multiProjections,Dictionary<string,string> arrayIdPredicates,IReadOnlyList<MySqlPhysicalReference> physicalReferences,IReadOnlyDictionary<string,MySqlValueOrigin> origins,IReadOnlyList<MySqlReverseProjection>? reverse=null,IReadOnlyList<MySqlCrossTableProjection>? crosses=null,IReadOnlyDictionary<string,object?>? constants=null)
    { From=from; ProjectId=projectId; Scope=scope; Expressions=new ReadOnlyDictionary<string,string>(expressions); Types=new ReadOnlyDictionary<string,string>(types); Outputs=Array.AsReadOnly(outputs); ArrayNamePredicates=new ReadOnlyDictionary<string,string>(arrayNamePredicates); MultiProjections=new ReadOnlyDictionary<string,MySqlMultiProjection>(multiProjections); ArrayIdPredicates=new ReadOnlyDictionary<string,string>(arrayIdPredicates); PhysicalReferences=physicalReferences; Origins=origins; Reverse=reverse??[]; Crosses=crosses??[];Constants=constants??new ReadOnlyDictionary<string,object?>(new Dictionary<string,object?>()); }
    internal IReadOnlyList<MySqlReverseProjection> Reverse {get;}
    internal IReadOnlyList<MySqlCrossTableProjection> Crosses{get;}
    internal IReadOnlyDictionary<string,object?> Constants{get;}
    internal IReadOnlyList<MySqlPhysicalReference> PhysicalReferences {get;}
    internal IReadOnlyDictionary<string,MySqlValueOrigin> Origins {get;}
    internal string From { get; }
    internal string Scope { get; }
    internal string ProjectId { get; }
    internal IReadOnlyDictionary<string,string> ArrayNamePredicates {get;}
    internal IReadOnlyDictionary<string,string> ArrayIdPredicates {get;}
    internal IReadOnlyDictionary<string,MySqlMultiProjection> MultiProjections {get;}
    internal IReadOnlyDictionary<string,string> Expressions { get; }
    internal IReadOnlyDictionary<string,string> Types { get; }
    public IReadOnlyList<string> Outputs { get; }
    /// <summary>筛选和排序仅能引用本计划明确编译的字段，标识符不是客户端SQL。</summary>
    internal string Expression(string name)=>Expressions.TryGetValue(name,out var expression)?expression:throw new InvalidOperationException("读取字段未编译："+name);
}

/// <summary>编译单值联接与固定多选名称计划；未证明的级联和虚拟表达式继续明确拒绝。</summary>
public static partial class MySqlNonRedundantReadCompiler
{
    /// <summary>输出由生成用途指定；依赖独立编译，不擅自扩成整表读取或读取权限。</summary>
    public static MySqlNonRedundantReadPlan Compile(MetadataCatalog catalog,string tableId,IReadOnlyList<string> outputNames,IReadOnlyList<string>? dependencies=null,bool deferCascadeNames=false)=>CompileWithCrossTables(catalog,tableId,outputNames,[],dependencies,deferCascadeNames);
    /// <summary>冻结目标页绑定仅用于跨表逻辑投影，现有公开编译入口及其二进制签名保持。</summary>
    public static MySqlNonRedundantReadPlan CompileWithCrossTables(MetadataCatalog catalog,string tableId,IReadOnlyList<string> outputNames,IReadOnlyList<NonRedundantCrossTableScope> crossTables,IReadOnlyList<string>? dependencies=null,bool deferCascadeNames=false)
    {
        ArgumentNullException.ThrowIfNull(crossTables);
        ArgumentNullException.ThrowIfNull(catalog); ArgumentNullException.ThrowIfNull(outputNames);
        MetadataValidation.Validate(catalog);
        if(!long.TryParse(catalog.YzProjectId,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var projectId)||projectId<=0)throw new InvalidOperationException("发布项目标识必须为正整数。");
        if(catalog.DatabaseProvider!="mysql")throw new NotSupportedException("当前固定读取执行仅完成MySQL适配。");
        if(outputNames.Count==0||outputNames.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=outputNames.Count)throw new InvalidOperationException("输出字段不能为空或重复。");
        var builder=new Builder(catalog,tableId,deferCascadeNames:deferCascadeNames,crossTables:crossTables);
        return builder.Compile(outputNames,dependencies??[]);
    }
    /// <summary>维护用途只重算声明的物理副本；普通读取仍坚持原冗余／非冗余边界。</summary>
    internal static MySqlNonRedundantReadPlan CompileRefresh(MetadataCatalog catalog,string tableId,IReadOnlyList<string> fields,IReadOnlyList<RelationCacheDefinition> multiple,IReadOnlyDictionary<string,string>? paths=null)
    {
        MetadataValidation.Validate(catalog);
        if(catalog.DatabaseProvider!="mysql")throw new NotSupportedException("维护回填尚未完成该方言验收。");
        return new Builder(catalog,tableId,refreshFields:fields.ToHashSet(StringComparer.Ordinal),refreshMultiples:multiple,refreshPaths:paths).Compile(["id",..fields,..multiple.Select(item=>catalog.ConfigColumns.Single(column=>column.Id==item.ForeignColumnId).ColumnName).Distinct(StringComparer.Ordinal)],[]);
    }
    /// <summary>先完整验证目录，再以专用用途编译单一匹配字段；最终物理列必须仍为非凭据标量。</summary>
    public static MySqlNonRedundantReadPlan CompileMatch(MetadataCatalog catalog,string tableId,string field)
    {
        Compile(catalog,tableId,[field]);return new Builder(catalog,tableId,true).Compile([field],[]);
    }
    /// <summary>构造时消费元数据，最终只保留不可变的表达式文本、类型和项目范围。</summary>
    private sealed partial class Builder
    {
        private readonly MetadataCatalog catalog;
        private readonly string rootAlias,aliasPrefix;
        private readonly bool matching;
        private readonly IReadOnlySet<string>? refreshFields;
        private readonly IReadOnlyDictionary<string,string> refreshPaths;
        private readonly IReadOnlyList<RelationCacheDefinition> refreshMultiples;
        private readonly Dictionary<(string,string),MySqlPhysicalReference> references=new();
        private MySqlValueOrigin? origin;
        private string output="";
        private readonly Dictionary<string,(string Table,string From,string Foreign,string Type)> reverse=new(StringComparer.Ordinal);
        private readonly Dictionary<string,Dictionary<string,HashSet<string>>> aliasFields=new(StringComparer.Ordinal);
        private readonly bool deferCascadeNames;
        private IReadOnlySet<string> queryDependencies=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,TableMetadata> tables;
        private readonly Dictionary<string,Dictionary<string,ColumnMetadata>> columns;
        private readonly Dictionary<string,MetadataStoragePlan> storage;
        private readonly TableMetadata root;
        private readonly Dictionary<string,string> aliases=new(StringComparer.Ordinal);
        private readonly List<string> joins=[];
        private readonly Dictionary<string,string> multiPredicates=new(StringComparer.Ordinal);
        private readonly Dictionary<string,string> idPredicates=new(StringComparer.Ordinal);
        private readonly Dictionary<string,MySqlMultiProjection> multiProjections=new(StringComparer.Ordinal);
        private readonly HashSet<string> resolving=new(StringComparer.Ordinal);
        internal Builder(MetadataCatalog catalog,string tableId,bool matching=false,bool deferCascadeNames=false,IReadOnlySet<string>? refreshFields=null,IReadOnlyList<RelationCacheDefinition>? refreshMultiples=null,IReadOnlyDictionary<string,string>? refreshPaths=null,IReadOnlyList<NonRedundantCrossTableScope>? crossTables=null,string rootAlias="b",string aliasPrefix="r")
        {
            this.rootAlias=rootAlias;this.aliasPrefix=aliasPrefix;
            this.crossTables=(crossTables??[]).ToDictionary(scope=>scope.SourceColumnId,StringComparer.Ordinal);
            this.catalog=catalog;this.matching=matching;this.deferCascadeNames=deferCascadeNames;this.refreshFields=refreshFields;this.refreshMultiples=refreshMultiples??[];this.refreshPaths=refreshPaths??new Dictionary<string,string>();
            tables=catalog.ConfigTables.Where(table=>(table.YzProjectId.Length==0?catalog.YzProjectId:table.YzProjectId)==catalog.YzProjectId).ToDictionary(table=>table.Id,StringComparer.Ordinal);
            root=tables.GetValueOrDefault(tableId)??throw new InvalidOperationException("源表不属于当前项目。");
            if((refreshFields is null?root.IsRongyu!=0:root.IsRongyu!=1)||root.IsFunctionTable!=0||root.IsView!=0)throw new InvalidOperationException("固定非冗余读取源必须是非冗余实体表。");
            if(tables.Values.GroupBy(table=>table.TableName,StringComparer.OrdinalIgnoreCase).Any(group=>group.Count()!=1))throw new InvalidOperationException("项目表名重复。");
            if(catalog.ConfigColumns.Any(column=>tables.ContainsKey(column.ConfigTableId)&&column.YzProjectId.Length>0&&column.YzProjectId!=catalog.YzProjectId))throw new InvalidOperationException("字段归属与发布项目不一致。");
            columns=tables.Values.ToDictionary(table=>table.Id,table=>RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id)).ToDictionary(column=>column.ColumnName,StringComparer.OrdinalIgnoreCase),StringComparer.Ordinal);
            storage=tables.Values.ToDictionary(table=>table.Id,table=>MetadataStoragePolicy.ForTable(catalog,table),StringComparer.Ordinal);
            foreach(var binding in this.crossTables.Values)if(!columns[root.Id].Values.Any(column=>column.Id==binding.SourceColumnId&&column.ControlKind=="CascadeTree"))throw new InvalidOperationException("跨表读取绑定不属于当前来源表。");
        }
        /// <summary>主键作为稳定排序依赖；根及每个目标分别保留软删和项目范围。</summary>
        internal MySqlNonRedundantReadPlan Compile(IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies)
        {
            queryDependencies=new HashSet<string>(dependencies,StringComparer.OrdinalIgnoreCase);
            var id=Column(root,"id");if(id.LogicalType!="int64"||id.IsDbNotNull!=1||!storage[root.Id].IsPhysical(id))throw new InvalidOperationException("读取源缺少非空int64物理主键。");
            var expressions=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var types=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var origins=new Dictionary<string,MySqlValueOrigin>(StringComparer.Ordinal);
            foreach(var name in outputs.Concat(dependencies).Append("id").Distinct(StringComparer.OrdinalIgnoreCase))
            {output=name;origin=null;var resolved=Resolve(root,rootAlias,name);expressions.Add(name,resolved.Sql);types.Add(name,resolved.Type);if(origin is not null)origins[name]=origin;}
            var scope=Scope(root,rootAlias);return new(" FROM "+Q(root.TableName)+" "+rootAlias+string.Concat(joins),catalog.YzProjectId,scope,expressions,types,outputs.ToArray(),expressions.Where(item=>multiPredicates.ContainsKey(item.Value)).ToDictionary(item=>item.Key,item=>multiPredicates[item.Value],StringComparer.OrdinalIgnoreCase),expressions.Where(item=>multiProjections.ContainsKey(item.Value)).ToDictionary(item=>item.Key,item=>multiProjections[item.Value],StringComparer.OrdinalIgnoreCase),expressions.Where(item=>idPredicates.ContainsKey(item.Value)).ToDictionary(item=>item.Key,item=>idPredicates[item.Value],StringComparer.OrdinalIgnoreCase),System.Array.AsReadOnly(references.Values.ToArray()),new ReadOnlyDictionary<string,MySqlValueOrigin>(origins),System.Array.AsReadOnly(reverse.Select(item=>new MySqlReverseProjection(item.Value.Table,item.Value.From,item.Value.Foreign,item.Value.Type,new ReadOnlyDictionary<string,IReadOnlySet<string>>(aliasFields[item.Key].ToDictionary(field=>field.Key,field=>(IReadOnlySet<string>)field.Value.ToFrozenSet(StringComparer.Ordinal),StringComparer.Ordinal)))).ToArray()),System.Array.AsReadOnly(crossProjections.Values.ToArray()),new ReadOnlyDictionary<string,object?>(crossConstants));
        }
        /// <summary>仅遍历真正的字段依赖，允许自关联但拒绝递归字段循环。</summary>
        private (string Sql,string Type) Resolve(TableMetadata table,string alias,string name)
        {
            MetadataValidation.RequireIdentifier(name);var key=table.Id+":"+name;
            if(!resolving.Add(key))throw new InvalidOperationException("非冗余字段存在循环依赖。");
            try
            {
                var column=columns[table.Id].GetValueOrDefault(name);
                if(alias==rootAlias&&table.Id==root.Id&&refreshFields?.Contains(name)==true&&refreshMultiples.SingleOrDefault(item=>item.CacheColumnId==column?.Id) is{} refresh)
                {
                    var foreign=columns[table.Id].Values.Single(item=>item.Id==refresh.ForeignColumnId);var target=tables[refresh.TargetTableId];var value=columns[target.Id].Values.Single(item=>item.Id==refresh.ValueColumnId);
                    return MultiDisplay(table,alias,foreign,value.ColumnName);
                }
                foreach(var tree in columns[table.Id].Values.Where(item=>item.ControlKind=="CascadeTree"&&item.ColumnName.EndsWith("_id",StringComparison.Ordinal)))
                {
                    if(table.IsRongyu!=0||!name.Equals(tree.ColumnName[..^3]+"_name",StringComparison.OrdinalIgnoreCase))continue;
                    var definition=Definition(tree);if(Value(definition,"sourceMode")!="selfTree")continue;
                    if(matching||alias!=rootAlias||!deferCascadeNames||queryDependencies.Contains(name))throw new NotSupportedException("级联名称要求已证明的来源图投影，不能当作精确匹配、筛选排序或跨表派生值。");
                    if(!storage[table.Id].IsPhysical(tree)||!definition.TryGetProperty("selfTree",out var settings)||settings.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("自树派生名称缺少物理来源和固定定义。");
                    var target=tables.Values.SingleOrDefault(item=>item.TableName==Value(settings,"table"))??throw new InvalidOperationException("自树名称目标不属于发布项目。");ValidateTarget(target);
                    var targetName=Value(settings,"nameField");if(targetName.Length==0)targetName="name";var value=Column(target,targetName);
                    if(!storage[target.Id].IsPhysical(value)||value.LogicalType!="string"||Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(value.ColumnName))throw new NotSupportedException("自树叶子名称必须为非凭据物理字符串。");
                    // 明确交由生成服务的固定图投影，SQL阶段不读取未经完整祖先范围证明的叶子名称。
                    // 只有显式选择此两阶段用途时才生成占位；普通读取和名称查询依赖一律拒绝。
                    return ("NULL","string");
                }
                // 稀疏存储位不是完整逻辑路径；固定SQL投影接通前拒绝将原始空位当作祖先值发布。
                foreach(var tree in columns[table.Id].Values.Where(item=>item.ControlKind=="CascadeTree"))
                {
                    var definition=Definition(tree);if(table.IsRongyu!=0||Value(definition,"sourceMode")=="selfTree")continue;
                    var sparse=CrossTableSparseSelection.Compile(table,tree.Id,columns[table.Id].Values);
                    if(sparse.Slots.Any(slot=>slot.ColumnName.Equals(name,StringComparison.OrdinalIgnoreCase)||(slot.ColumnName[..^3]+"_name").Equals(name,StringComparison.OrdinalIgnoreCase)))
                    {
                        if(!crossTables.TryGetValue(tree.Id,out var binding)||matching||refreshFields is not null)throw new NotSupportedException("非冗余跨表逻辑路径缺少明确目标页绑定或当前用途尚未接线。");
                        return CrossValue(table,alias,tree,sparse,binding,name);
                    }
                }
                // 多值显示先识别明确的_ids关系，聚合留在相关子查询内，不扩张根结果的基数。
                foreach(var multi in columns[table.Id].Values.Where(item=>item.ControlKind is "MultiSelectPage" or "MultiCheckBoxDatabase"&&item.ColumnName.EndsWith("_ids",StringComparison.Ordinal)))
                {
                    var display=Value(Definition(multi),"nameField");if(display.Length==0)display="name";
                    var auxiliary=multi.ColumnName[..^4]+"_"+(display.EndsWith('s')?display:display+"s");
                    if(display=="id"||!name.Equals(auxiliary,StringComparison.OrdinalIgnoreCase))continue;
                    if(matching)throw new NotSupportedException("精确管理匹配不能使用多值派生名称。");
                    if(column is not null&&(Kind(table,alias,column)!=MetadataFieldStorage.Join||Text(column,"columnFullName")!=multi.ColumnName[..^4]+"."+display&&Text(column,"columnFullName")!=multi.ColumnName+"."+display))throw new InvalidOperationException("多值显示字段必须声明同一关系与名称路径。");
                    return MultiDisplay(table,alias,multi,display);
                }
                if(column is not null)
                {
                    var kind=Kind(table,alias,column);
                    if(kind==MetadataFieldStorage.Physical)
                    {
                        if(refreshFields is not null&&(Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(column.ColumnName)||table.TableName=="yz_manager"&&column.ColumnName=="salt"))throw new NotSupportedException("维护回填不能读取凭据或令牌字段。");
                        if(matching)
                        {
                            if(Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(column.ColumnName)||column.ControlKind is "FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "CheckBoxList" or "MultiList"||column.ColumnName.EndsWith("_ids",StringComparison.Ordinal)||column.LogicalType=="json")throw new NotSupportedException("精确管理匹配路径只能使用非凭据单值列。");
                            Metarill.Core.Security.ExactDataManageValues.Unrestricted.ReadLogical(column.LogicalType);
                        }
                        if(refreshFields is not null&&alias!=rootAlias&&(column.ControlKind is "FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "CheckBoxList" or "MultiList"||column.LogicalType is "json" or "binary"))throw new NotSupportedException("维护派生文件或数组路径需要独立事务合同，未执行部分回填。");
                        Use(table,column);Touch(alias,column.ColumnName);origin=new(table.TableName,column.ColumnName);
                        var expression=alias+"."+Q(column.ColumnName);
                        if(alias==rootAlias&&column.ControlKind=="MultiCheckBoxDatabase"&&column.ColumnName.EndsWith("_ids",StringComparison.Ordinal))
                        {
                            if(column.LogicalType!="string")throw new NotSupportedException("数据库复选必须以字符串保存JSON ID数组。");
                            idPredicates[expression]=ArrayMember(Array(expression),"{parameter}");
                        }
                        return(expression,column.LogicalType);
                    }
                    if(kind==MetadataFieldStorage.Join)
                    {
                        var path=(alias==rootAlias&&table.Id==root.Id&&refreshPaths.TryGetValue(column.ColumnName,out var refreshPath)?refreshPath:Text(column,"columnFullName")).Split('.');if(path.Length<2||path.Any(string.IsNullOrWhiteSpace))throw new InvalidOperationException("联接字段缺少有效来源路径。");
                        var current=table;var currentAlias=alias;
                        foreach(var segment in path[..^1])
                        {MetadataValidation.RequireIdentifier(segment);var relation=columns[current.Id].GetValueOrDefault(segment+"_id")??columns[current.Id].GetValueOrDefault(segment)??throw new InvalidOperationException("联接路径关系不存在。");var target=Target(relation);var id=Resolve(current,currentAlias,relation.ColumnName);currentAlias=Join(currentAlias,relation,target,id.Sql,id.Type);current=target;}
                        return Resolve(current,currentAlias,path[^1]);
                    }
                    if(kind is MetadataFieldStorage.Virtual or MetadataFieldStorage.Layout)throw new NotSupportedException("当前单值计划不支持虚拟或布局读取字段。");
                    if(name is "insert_manager_real_name" or "update_manager_real_name")
                    {var relation=Column(table,name.Replace("_real_name","_id",StringComparison.Ordinal));var target=tables.Values.SingleOrDefault(item=>item.TableName=="yz_manager")??throw new InvalidOperationException("缺少固定管理员基础设施元数据。");ValidateTarget(target);var id=Resolve(table,alias,relation.ColumnName);return Resolve(target,Join(alias,relation,target,id.Sql,id.Type),"real_name");}
                }
                foreach(var relation in columns[table.Id].Values.Where(MetadataStoragePolicy.IsRelationColumn))
                {
                    var definition=Definition(relation);var display=Value(definition,"nameField");if(display.Length==0)display="name";
                    var auxiliary=relation.ColumnName.EndsWith("_id",StringComparison.Ordinal)&&display!="id"?relation.ColumnName[..^3]+"_"+display:"";
                    if(!name.Equals(auxiliary,StringComparison.OrdinalIgnoreCase))continue;
                    var target=Target(relation);var id=Resolve(table,alias,relation.ColumnName);return Resolve(target,Join(alias,relation,target,id.Sql,id.Type),display);
                }
                throw new NotSupportedException("字段不是已支持的物理列或单值派生关系："+name);
            }
            finally{resolving.Remove(key);}
        }
        /// <summary>仅编译明确多选的物理ID与名称；窗口聚合按原数组顺序保留缺失目标的null位置。</summary>
        private (string Sql,string Type) MultiDisplay(TableMetadata table,string owner,ColumnMetadata relation,string display)
        {
            if(matching)throw new NotSupportedException("固定单值名称路径不能穿过多值关系或数组。");
            if(!storage[table.Id].IsPhysical(relation)||relation.LogicalType!="string")throw new NotSupportedException("多值关系必须以物理字符串列存储JSON ID数组。");
            var definition=Definition(relation);TableMetadata? target=null;
            var id=Value(definition,"tableId");if(id.Length>0)target=tables.GetValueOrDefault(id);
            var pageId=Value(definition,"pageId");if(target is null&&pageId.Length>0)target=PageTarget(pageId);
            if(target is null&&relation.LinkConfigTableId.Length>0)target=tables.GetValueOrDefault(relation.LinkConfigTableId);
            if(target is null&&Text(relation,"linkConfigPageId").Length>0)target=PageTarget(Text(relation,"linkConfigPageId"));
            if(target is null){var targetName=Value(definition,"table");if(targetName.Length==0)targetName=Text(relation,"joinTable");target=tables.Values.SingleOrDefault(item=>item.TableName==targetName);}
            if(target is null)throw new InvalidOperationException("多值关联缺少同项目目标。");ValidateTarget(target);
            if(id.Length>0&&tables.GetValueOrDefault(id)?.Id!=target.Id||pageId.Length>0&&PageTarget(pageId)?.Id!=target.Id||Value(definition,"table") is {Length:>0} declared&&declared!=target.TableName||Text(relation,"linkConfigPageId") is {Length:>0} linkedPage&&PageTarget(linkedPage)?.Id!=target.Id)throw new InvalidOperationException("多值关系的目标声明缺失或相互矛盾。");
            if(relation.LinkConfigTableId.Length>0&&relation.LinkConfigTableId!=target.Id)throw new InvalidOperationException("多值关系目标声明不一致。");
            var value=Column(target,display);
            if(value.LogicalType!="string"||value.ControlKind is "FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase"||Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(value.ColumnName))throw new NotSupportedException("多值名称必须为非凭据的字符串标量。");
            if(!storage[target.Id].IsPhysical(value))return DerivedMultiDisplay(table,owner,relation,target,value);
            Use(table,relation);
            var source=owner+"."+Q(relation.ColumnName);
            // JSON_TYPE只在有效JSON分支求值，损坏历史值与非数组按原空集合处理，不能让JSON_TABLE整页失败。
            var array=Array(source);
            var targetScope=Scope(target,"multi_value");
            var expression="CAST(COALESCE((SELECT JSON_ARRAYAGG(multi_value."+Q(display)+") OVER (ORDER BY multi_ids.ordinal ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) FROM JSON_TABLE("+array+", '$[*]' COLUMNS(ordinal FOR ORDINALITY, member_id VARCHAR(32) PATH '$' NULL ON ERROR)) multi_ids LEFT JOIN "+Q(target.TableName)+" multi_value ON "+(relation.ControlKind=="MultiCheckBoxDatabase"?"CAST(CAST(multi_value.`id` AS CHAR) AS BINARY)=CAST(multi_ids.member_id AS BINARY)":"CAST(multi_value.`id` AS CHAR)=multi_ids.member_id")+(targetScope.Length==0?"":" AND "+targetScope)+" ORDER BY multi_ids.ordinal LIMIT 1),JSON_ARRAY()) AS CHAR)";
            multiProjections[expression]=new(source,Q(target.TableName),Q(display),targetScope);
            // 名称筛选直接对目标做EXISTS，不先聚合整份名称数组，也不把根记录JOIN成多行。
            multiPredicates[expression]="EXISTS (SELECT 1 FROM "+Q(target.TableName)+" multi_value WHERE JSON_CONTAINS("+array+",JSON_QUOTE(CAST(multi_value.`id` AS CHAR)))"+(targetScope.Length==0?"":" AND "+targetScope)+" AND multi_value."+Q(display)+" LIKE {parameter} ESCAPE '!')";
            if(relation.ControlKind=="MultiCheckBoxDatabase")multiPredicates[expression]="((SELECT 1 FROM "+Q(target.TableName)+" multi_value WHERE "+ArrayMember(array,"CAST(multi_value.`id` AS CHAR)")+(targetScope.Length==0?"":" AND "+targetScope)+" AND multi_value."+Q(display)+" LIKE {parameter} ESCAPE '!' LIMIT 1) IS NOT NULL)";
            origin=new(target.TableName,value.ColumnName);return(expression,"string");
        }
        /// <summary>坏历史JSON不能使整页SQL失败，源数据不在读取时修补。</summary>
        private static string Array(string source)=>"CASE WHEN JSON_VALID("+source+") THEN CASE WHEN JSON_TYPE("+source+")='ARRAY' THEN "+source+" ELSE '[]' END ELSE '[]' END";
        /// <summary>同时匹配旧数值／字符串ID，二进制比较避免尾空格或子串误命中；表达式只来自固定计划。</summary>
        // 验收库8.4.11中WHERE EXISTS的JSON_TABLE漏判多来源行；有界标量查询保持逐行相关执行。
        private static string ArrayMember(string array,string parameter)=>"((SELECT 1 FROM JSON_TABLE("+array+", '$[*]' COLUMNS(member_id VARCHAR(32) PATH '$' NULL ON ERROR)) checkbox_ids WHERE CAST(checkbox_ids.member_id AS BINARY)=CAST("+parameter+" AS BINARY) LIMIT 1) IS NOT NULL)";
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
            if(refreshFields is not null&&(id.Length>0&&tables.GetValueOrDefault(id)?.Id!=target.Id||pageId.Length>0&&PageTarget(pageId)?.Id!=target.Id||relation.LinkConfigTableId.Length>0&&relation.LinkConfigTableId!=target.Id||Value(definition,"table") is {Length:>0} named&&named!=target.TableName||Text(relation,"joinTable") is {Length:>0} joined&&joined!=target.TableName||Text(relation,"linkConfigPageId") is {Length:>0} linked&&PageTarget(linked)?.Id!=target.Id))throw new InvalidOperationException("维护关系目标的有效声明相互矛盾。");
            ValidateTarget(target);return target;
        }
        /// <summary>固定审计目标与业务关系使用相同实体主键约束，不能因固定表名绕过唯一性前提。</summary>
        private void ValidateTarget(TableMetadata target)
        {
            if(target.IsFunctionTable!=0||target.IsView!=0)throw new InvalidOperationException("关联目标必须为同项目实体表。");
            var key=Column(target,"id");if(key.LogicalType!="int64"||key.IsDbNotNull!=1||!storage[target.Id].IsPhysical(key))throw new InvalidOperationException("关联目标必须使用非空int64物理主键。");
        }
        /// <summary>同一路径复用别名，不同角色到同表和自关联分别命名。</summary>
        private string Join(string ownerAlias,ColumnMetadata relation,TableMetadata target,string source,string sourceType)
        {
            if(matching&&sourceType!="int64")throw new NotSupportedException("精确管理关联键必须与目标主键同为int64，不能借数据库隐式转换匹配。");
            var path=ownerAlias+":"+relation.Id;
            if(aliases.TryGetValue(path,out var existing)){Scope(target,existing);Touch(existing,"id");return existing;}
            var alias=aliasPrefix+aliases.Count;aliases.Add(path,alias);var prefix=" FROM "+Q(root.TableName)+" "+rootAlias+string.Concat(joins);var scope=Scope(target,alias);
            if(refreshFields is not null){Touch(alias,"id");reverse.Add(alias,(target.TableName,prefix,source,sourceType));}
            if(refreshFields is not null&&sourceType is not("int64" or "string"))throw new NotSupportedException("维护关系ID必须为int64或规范字符串。");
            var equality=refreshFields is not null&&sourceType=="string"?"BINARY CAST("+alias+".`id` AS CHAR)=BINARY "+source:alias+"."+Q("id")+"="+source;
            joins.Add(" LEFT JOIN "+Q(target.TableName)+" "+alias+" ON "+equality+(scope.Length==0?"":" AND "+scope));return alias;
        }
        /// <summary>范围字段必须是真实物理列；项目值固定绑定，不让筛选覆盖。</summary>
        private string Scope(TableMetadata table,string alias)
        {
            if((matching||refreshFields is not null)&&!columns[table.Id].ContainsKey("yz_project_id"))throw new NotSupportedException("精确管理关联必须声明真实项目列。");
            var predicates=new List<string>();foreach(var name in new[]{"delete_no","yz_project_id"})
            {
                if(!columns[table.Id].TryGetValue(name,out var column))continue;
                if(!storage[table.Id].IsPhysical(column))throw new InvalidOperationException("范围字段必须为物理列。");
                if(name=="delete_no"&&column.LogicalType!="datetime"||name=="yz_project_id"&&column.LogicalType is not("int64" or "string"))throw new InvalidOperationException("范围字段类型不匹配。");
                if(name=="yz_project_id"&&(!long.TryParse(catalog.YzProjectId,out var project)||project<=0))throw new InvalidOperationException("项目标识无效。");
                Use(table,column);Touch(alias,column.ColumnName);
                predicates.Add(alias+"."+Q(name)+(name=="delete_no"?" IS NULL":column.LogicalType=="int64"?"=@project_scope_number":"=@project_scope_text"));
            }
            return string.Join(" AND ",predicates);
        }
        /// <summary>选择页只提供目标归属，页面授权由既有HTTP入口建立。</summary>
        private TableMetadata? PageTarget(string pageId)=>catalog.ConfigPages.FirstOrDefault(page=>page.Id==pageId) is {} page?tables.GetValueOrDefault(page.ConfigTableId):null;
        /// <summary>读取固定元数据字段，不猜测缺失物理字段。</summary>
        private ColumnMetadata Column(TableMetadata table,string name)
        {
            var column=columns[table.Id].GetValueOrDefault(name)??throw new InvalidOperationException("缺少字段："+name);if(storage[table.Id].IsPhysical(column))Use(table,column);return column;
        }
        /// <summary>仅维护根投影把指定的物理副本当作派生值，不改变目标R1缓存的正常读取语义。</summary>
        private MetadataFieldStorage Kind(TableMetadata table,string alias,ColumnMetadata column)=>alias==rootAlias&&table.Id==root.Id&&refreshFields?.Contains(column.ColumnName)==true?MetadataFieldStorage.Join:storage[table.Id].Kind(column);
        /// <summary>每个输出分别登记目标字段依赖，复用别名也不能丢失第二个缓存的传播条件。</summary>
        private void Touch(string alias,string field)
        {
            if(refreshFields is null)return;if(!aliasFields.TryGetValue(alias,out var fields))aliasFields[alias]=fields=new(StringComparer.Ordinal);if(!fields.TryGetValue(output,out var names))fields[output]=names=new(StringComparer.Ordinal);names.Add(field);
        }
        /// <summary>记录实际依赖的物理列，执行回填前一次校验，不能因缺表而写一半。</summary>
        private void Use(TableMetadata table,ColumnMetadata column){if(refreshFields is not null)references.TryAdd((table.TableName,column.ColumnName),new(table.TableName,Metarill.Data.Schema.MySqlSchemaPlanner.CompileColumn(column)));}
    }
    /// <summary>控件定义仅作结构解析；旧式或重复JSON键必须明确拒绝。</summary>
    private static JsonElement Definition(ColumnMetadata column)
    {
        if(column.ControlKind=="MultiCheckBoxDatabase")return DatabaseCheckboxDefinition.Parse(column.ControlDetail).ReadDefinition(column);
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
