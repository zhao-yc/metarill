using System.Globalization;
using Metarill.Data.Queries;
using Metarill.Core.Metadata;

namespace Metarill.Runtime.Files;

internal sealed record FileParentScope(ColumnMetadata Source, TableMetadata Parent, ColumnMetadata Project, bool HasSoftDelete);

internal sealed record FilePageScope(IReadOnlyList<FileParentScope> Parents,FixedConditionSqlPlan? Condition,IReadOnlyList<string> ConditionFields);

/// <summary>发布时固定项目/父等值及物理标量常量，与生成页面共用条件AST和数据类型绑定。</summary>
internal static class FileReferenceScope
{
    /// <summary>兼容父范围编译调用；生产观察者和读取器消费发布时缓存的完整计划。</summary>
    internal static IReadOnlyList<FileParentScope> Compile(MetadataCatalog catalog,TableMetadata table,PageMetadata page)=>CompilePage(catalog,table,page).Parents;
    /// <summary>未知变量/数组/派生/凭据条件整体拒绝；不会只保留已知父项而丢弃其它限制。</summary>
    internal static FilePageScope CompilePage(MetadataCatalog catalog,TableMetadata table,PageMetadata page)
    {
        try
        {
            var result=new List<FileParentScope>();var constants=new List<FixedConditionTerm>();
            var columns=MetadataStoragePolicy.ForTable(catalog,table).PhysicalColumns;
            var syntax=FixedConditionPlan.Compile(page.WhereSql,columns.Select(column=>column.ColumnName));
            foreach(var term in syntax.Terms)
            {
                var column=columns.Single(candidate=>candidate.ColumnName==term.Field);
                var variables=term.Values.Where(value=>value.Kind==FixedConditionValueKind.Variable).ToArray();
                if(variables.Length==0)
                {
                    var multiNames=columns.Any(source=>source.ControlKind=="MultiSelectPage"&&source.ColumnName.EndsWith("_ids",StringComparison.Ordinal)&&column.ColumnName==source.ColumnName[..^4]+"_names");
                    var credential=table.Id=="6031906809708544"&&table.TableName=="yz_manager"&&column.ColumnName is "password" or "salt" or "old_password";
                    if(column.LogicalType.Equals("json",StringComparison.OrdinalIgnoreCase)||column.ControlKind is "FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase"||multiNames||credential)throw Unsupported();
                    constants.Add(term);continue;
                }
                if(term.Comparison!=FixedConditionComparison.Equal||term.Values.Count!=1||variables.Length!=1)throw Unsupported();
                var variable=variables[0].Text;
                var project=variable.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase);
                if(!project&&!variable.Equals("parent_id",StringComparison.OrdinalIgnoreCase)&&!variable.Equals("parentId",StringComparison.OrdinalIgnoreCase))throw Unsupported();
                if((column.ColumnName=="yz_project_id")!=project||!IdentityType(column.LogicalType))throw Unsupported();
                if(project)continue;
                var parents=catalog.ConfigTables.Where(candidate=>!string.IsNullOrWhiteSpace(column.LinkConfigTableId)?candidate.Id==column.LinkConfigTableId:candidate.TableName+"_id"==column.ColumnName).ToArray();
                if(parents.Length!=1||parents[0].IsFunctionTable!=0||parents[0].IsView!=0)throw Unsupported();
                var parent=parents[0];var parentColumns=MetadataStoragePolicy.ForTable(catalog,parent).PhysicalColumns;
                var id=parentColumns.SingleOrDefault(candidate=>candidate.ColumnName=="id");var owner=parentColumns.SingleOrDefault(candidate=>candidate.ColumnName=="yz_project_id");
                if(id is null||!LongType(id.LogicalType)||owner is null||!IdentityType(owner.LogicalType))throw Unsupported();
                var deleted=parentColumns.SingleOrDefault(candidate=>candidate.ColumnName=="delete_no");
                if(deleted is not null&&(deleted.LogicalType.ToLowerInvariant() is not("datetime" or "timestamp")||deleted.IsDbNotNull!=0))throw Unsupported();
                result.Add(new(column,parent,owner,deleted is not null));
            }
            var fields=constants.Select(term=>term.Field).Distinct(StringComparer.Ordinal).ToArray();
            var condition=constants.Count==0?null:FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile(string.Join(" AND ",constants.Select(term=>term.ToDefinition())),fields),fields.ToDictionary(field=>field,field=>columns.Single(column=>column.ColumnName==field).LogicalType,StringComparer.Ordinal));
            return new(result.AsReadOnly(),condition,Array.AsReadOnly(fields));
        }
        catch(Exception invalid)when(invalid is ArgumentException or InvalidOperationException){throw Unsupported();}
    }

    /// <summary>实际父键必须是可表示的正整数，不能把null、浮点或表达式解释为记录身份。</summary>
    internal static long PositiveId(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id : throw new UnauthorizedAccessException("实际记录的父范围无效。");
    }

    /// <summary>查询参数沿用生成器的字符串/整型身份映射，范围不依赖MySQL隐式数值转换。</summary>
    internal static object IdentityValue(ColumnMetadata column, long value) => column.LogicalType.ToLowerInvariant() switch
    {
        "string" or "text" or "varchar" => value.ToString(CultureInfo.InvariantCulture),
        "id" or "long" or "bigint" or "int64" => value,
        "integer" or "int" or "int32" => checked((int)value),
        _ => throw Unsupported()
    };

    /// <summary>只接受生成器明确支持的身份逻辑类型，JSON/日期/小数不作为父键。</summary>
    private static bool IdentityType(string type) => LongType(type) || type.ToLowerInvariant() is "string" or "text" or "varchar" or "integer" or "int" or "int32";
    /// <summary>父主键与生成实体一致，必须为64位整数。</summary>
    private static bool LongType(string type) => type.ToLowerInvariant() is "id" or "long" or "bigint" or "int64";
    /// <summary>能力缺口必须明确拒绝，不能退化成不带范围的查询。</summary>
    private static NotSupportedException Unsupported() => new("该页面包含尚未证明的记录范围或父表定义，文件引用同步拒绝执行。");
}
