using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Metadata;

public sealed record JoinColumnField(string TargetColumnId,string Name,string ColumnName,string ColumnType,string ColumnLength,string JoinColumnName,string ColumnFullName,string JoinTable);
public sealed record JoinColumnGroup(string SourceColumnId,string SourceColumnName,string SourceName,string SourcePrefix,string TargetTableId,string TargetTableName,string TargetName,string ResolveMessage,IReadOnlyList<JoinColumnField> Fields);
public sealed record JoinColumnCandidates(string ConfigTableId,string ConfigTableName,string TableName,IReadOnlyList<JoinColumnGroup> Groups);
internal sealed record JoinColumnPlan(Dictionary<string,object?> Source,Dictionary<string,object?> Target,JoinColumnField Field);
internal sealed record JoinColumnSnapshot(JoinColumnCandidates Detail,IReadOnlyList<JoinColumnPlan> Plans);

/// <summary>只解释原关系声明并计算联接字段候选，不执行配置SQL或读取业务表。</summary>
internal static class JoinColumnWorkbenchRules
{
    private static readonly HashSet<string> SystemColumns=new(StringComparer.OrdinalIgnoreCase){"id","insert_time","update_time","insert_manager_id","update_manager_id","insert_manager_real_name","update_manager_real_name","insert_user_id","update_user_id","delete_no","import_no"};
    /// <summary>候选来源仅为同项目活动配置；名字、完整路径和原辅助名称三重去重。</summary>
    internal static JoinColumnSnapshot Build(Dictionary<string,object?> source,IReadOnlyList<Dictionary<string,object?>> tables,IReadOnlyList<Dictionary<string,object?>> columns,IReadOnlyList<Dictionary<string,object?>> pages)
    {
        var project=T(source,"yz_project_id");
        if(tables.Concat(columns).Concat(pages).Any(row=>T(row,"yz_project_id")!=project||row.GetValueOrDefault("delete_no") is not null))throw new InvalidOperationException("联接配置来源不属于当前项目或已删除。");
        var own=columns.Where(row=>L(row,"config_table_id")==L(source,"id")).ToArray();var names=own.Select(row=>T(row,"column_name")).ToHashSet(StringComparer.OrdinalIgnoreCase);var paths=own.Where(row=>Flag(row,"is_join_column")).Select(row=>T(row,"column_full_name")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups=new List<JoinColumnGroup>();var plans=new List<JoinColumnPlan>();
        foreach(var relation in Sort(own.Where(row=>T(row,"column_name").EndsWith("_id",StringComparison.Ordinal)&&!Flag(row,"is_join_column"))))
        {
            var prefix=T(relation,"column_name")[..^3];MetadataValidation.RequireIdentifier(prefix);var target=Resolve(relation,tables,pages);var detail=Definition(relation);var nameField=Value(detail,"nameField");if(nameField.Length==0)nameField="name";MetadataValidation.RequireIdentifier(nameField);var auxiliary=nameField=="id"?"":prefix+"_"+nameField;
            var fields=new List<JoinColumnField>();
            if(target.Table is not null)foreach(var column in Sort(columns.Where(row=>L(row,"config_table_id")==L(target.Table,"id"))))
            {
                var columnName=T(column,"column_name");if(columnName.Length==0||SystemColumns.Contains(columnName)||T(column,"control_kind") is "Title" or "Tab")continue;MetadataValidation.RequireIdentifier(columnName);
                var full=prefix+"."+columnName;var joined=prefix+"_"+columnName;
                if(joined.Equals(T(relation,"column_name"),StringComparison.OrdinalIgnoreCase)||joined.Equals(auxiliary,StringComparison.OrdinalIgnoreCase)||names.Contains(joined)||paths.Contains(full))continue;
                MetadataValidation.RequireIdentifier(joined);
                var next=columnName.EndsWith("_id",StringComparison.Ordinal)?Resolve(column,tables,pages).Name:"";
                var field=new JoinColumnField(T(column,"id"),T(column,"name"),columnName,T(column,"column_type"),T(column,"column_length"),joined,full,next);fields.Add(field);plans.Add(new(relation,column,field));
            }
            groups.Add(new(T(relation,"id"),T(relation,"column_name"),T(relation,"name"),prefix,target.Table is null?"":T(target.Table,"id"),target.Name,target.Table is null?"":T(target.Table,"name"),target.Table is null?"未找到关联目标表："+target.Name:"",fields));
        }
        return new(new(T(source,"id"),T(source,"name"),T(source,"table_name"),groups),plans);
    }
    /// <summary>整批键必须唯一且全部仍为当前可添加候选，不能默默保存合法子集。</summary>
    internal static IReadOnlyList<JoinColumnPlan> Select(JsonElement selections,JoinColumnSnapshot snapshot)
    {
        if(selections.ValueKind!=JsonValueKind.Array||selections.GetArrayLength()<1)throw new InvalidOperationException("请先选择要添加的联接字段。");
        var available=snapshot.Plans.ToDictionary(plan=>T(plan.Source,"id")+":"+T(plan.Target,"id"),StringComparer.Ordinal);var seen=new HashSet<string>(StringComparer.Ordinal);var result=new List<JoinColumnPlan>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var value in selections.EnumerateArray())
        {
            MetadataWorkbenchRules.Object(value,"sourceColumnId","targetColumnId");var key=MetadataWorkbenchRules.Id(value,"sourceColumnId").ToString(CultureInfo.InvariantCulture)+":"+MetadataWorkbenchRules.Id(value,"targetColumnId").ToString(CultureInfo.InvariantCulture);
            if(!seen.Add(key)||!available.TryGetValue(key,out var plan)||!names.Add(plan.Field.JoinColumnName)||!paths.Add(plan.Field.ColumnFullName))throw new InvalidOperationException("联接字段候选已变化、重复或无效，请刷新后重试。");result.Add(plan);
        }
        // 顺序仍沿原关系/目标字段排序，客户端选择数组顺序不改变原排序规则。
        return snapshot.Plans.Where(result.Contains).ToArray();
    }
    /// <summary>原优先级为joinTable、控件声明、字段前缀；目标始终限定当前项目，不回退到其它项目。</summary>
    private static (Dictionary<string,object?>? Table,string Name) Resolve(Dictionary<string,object?> column,IReadOnlyList<Dictionary<string,object?>> tables,IReadOnlyList<Dictionary<string,object?>> pages)
    {
        Dictionary<string,object?>? ByName(string name)=>tables.SingleOrDefault(table=>T(table,"table_name")==name);
        var name=T(column,"join_table");var target=name.Length==0?null:ByName(name);var kind=T(column,"control_kind");var detail=Definition(column);
        if(target is null&&kind=="List")
        {
            var id=Value(detail,"tableId");name=Value(detail,"table");target=id.Length>0?tables.SingleOrDefault(table=>T(table,"id")==id):null;if(target is null&&name.Length>0)target=ByName(name);
            if(target is not null&&name.Length>0&&T(target,"table_name")!=name)throw new InvalidOperationException("关系声明中的目标表ID与表名不一致。");
        }
        if(target is null&&kind is "SelectPage" or "MultiSelectPage")
        {
            var page=pages.SingleOrDefault(page=>T(page,"id")==Value(detail,"pageId"));if(page is not null)target=tables.SingleOrDefault(table=>L(table,"id")==L(page,"config_table_id"));
        }
        if(target is null){name=T(column,"column_name");name=name.EndsWith("_id",StringComparison.Ordinal)?name[..^3]:name;target=ByName(name);}
        return(target,target is null?name:T(target,"table_name"));
    }
    /// <summary>控件JSON只读取目标与显示字段，SQL条件保持文本，绝不执行；重复键拒绝歧义。</summary>
    private static JsonElement Definition(Dictionary<string,object?> column)
    {
        var kind=T(column,"control_kind");var raw=T(column,"control_detail");if(kind is not("List" or "SelectPage" or "MultiSelectPage")||string.IsNullOrWhiteSpace(raw))return JsonSerializer.SerializeToElement(new{});
        try{using var document=JsonDocument.Parse(raw);if(document.RootElement.ValueKind!=JsonValueKind.Object)throw new JsonException();var keys=new HashSet<string>();if(document.RootElement.EnumerateObject().Any(property=>!keys.Add(property.Name)))throw new JsonException();return document.RootElement.Clone();}catch(JsonException){throw new InvalidOperationException("关联控件明细必须是无重复键的JSON对象。");}
    }
    /// <summary>只将明确字符串或数字ID当作声明值，不解释数组或对象。</summary>
    private static string Value(JsonElement value,string key)=>value.TryGetProperty(key,out var field)&&field.ValueKind is JsonValueKind.String or JsonValueKind.Number?field.ToString():"";
    /// <summary>沿原排序号稳定排列，空排序号按零处理。</summary>
    private static IEnumerable<Dictionary<string,object?>> Sort(IEnumerable<Dictionary<string,object?>> rows)=>rows.OrderBy(row=>Convert.ToInt32(row.GetValueOrDefault("sort_no")??0,CultureInfo.InvariantCulture)).ThenBy(row=>L(row,"id"));
    /// <summary>配置空文本沿原空字符串语义读取。</summary>
    private static string T(IReadOnlyDictionary<string,object?> row,string key)=>SqlRowStore.Text(row,key);
    /// <summary>读取配置整数标识，不经浮点转换。</summary>
    private static long L(IReadOnlyDictionary<string,object?> row,string key)=>SqlRowStore.Id(row,key);
    /// <summary>沿统一数据库标志规则判断联接列。</summary>
    private static bool Flag(IReadOnlyDictionary<string,object?> row,string key)=>MetadataWorkbenchRules.StoredFlag(row.GetValueOrDefault(key))==1;
}
