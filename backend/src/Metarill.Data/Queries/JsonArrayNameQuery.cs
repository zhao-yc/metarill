using System.Linq.Expressions;
using System.Reflection;
using FreeSql;
using FreeSql.DataAnnotations;
using Metarill.Core.Metadata;
using MySqlConnector;

namespace Metarill.Data.Queries;

/// <summary>只供已编译名称辅助列使用的成员包含筛选；不会接受客户端表名、列名或SQL。</summary>
public static class JsonArrayNameQuery
{
    /// <summary>以强类型直接属性保留ORM别名和既有范围，关键词仅按字面子串参数化。</summary>
    public static ISelect<T> ApplyJsonArrayAnyLike<T>(DatabaseSession session, ISelect<T> query, Expression<Func<T, string?>> storageColumn, string keyword) where T : class
        => ApplyNameLike(session, query, storageColumn, keyword, true);

    /// <summary>数据库复选原单ID筛选按数组成员精确相等，不把序列化JSON当子串搜索。</summary>
    public static ISelect<T> ApplyJsonArrayHasId<T>(DatabaseSession session,ISelect<T> query,Expression<Func<T,string?>> storageColumn,long id) where T:class
    {
        ArgumentNullException.ThrowIfNull(session);ArgumentNullException.ThrowIfNull(query);ArgumentNullException.ThrowIfNull(storageColumn);
        if(id<=0)throw new ArgumentException("复选筛选ID必须为正整数。",nameof(id));
        if(!string.Equals(session.Provider.Name,"mysql",StringComparison.OrdinalIgnoreCase)||session.Orm.Ado.DataType!=DataType.MySql)throw new NotSupportedException("当前数据库尚未实现复选成员筛选。");
        if(storageColumn.Body is not MemberExpression{Expression:ParameterExpression parameter,Member:PropertyInfo property}||parameter!=storageColumn.Parameters[0]||property.PropertyType!=typeof(string)||property.GetMethod is not{IsStatic:false,IsPublic:true})throw new ArgumentException("复选存储列必须是已映射直接字符串属性。",nameof(storageColumn));
        var table=session.Orm.CodeFirst.GetTableByEntity(typeof(T));
        if(table is null||!table.ColumnsByCs.TryGetValue(property.Name,out var mapped)||mapped.Attribute.IsIgnore)throw new ArgumentException("复选存储列没有实体映射。",nameof(storageColumn));
        MetadataValidation.RequireIdentifier(mapped.Attribute.Name);
        var call=Expression.Call(typeof(DatabaseFunctions).GetMethod(nameof(DatabaseFunctions.HasId))!,storageColumn.Body,Expression.Constant(id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return query.Where(Expression.Lambda<Func<T,bool>>(call,storageColumn.Parameters));
    }

    /// <summary>两个名称入口共用固定映射与参数边界，模式只由服务端方法决定。</summary>
    internal static ISelect<T> ApplyNameLike<T>(DatabaseSession session, ISelect<T> query, Expression<Func<T, string?>> storageColumn, string keyword, bool jsonArray) where T : class
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(storageColumn);
        ArgumentNullException.ThrowIfNull(keyword);
        if (string.IsNullOrWhiteSpace(keyword)) throw new ArgumentException("名称关键词不能为空白。", nameof(keyword));
        if (!string.Equals(session.Provider.Name, "mysql", StringComparison.OrdinalIgnoreCase) || session.Orm.Ado.DataType != DataType.MySql)
            throw new NotSupportedException("当前数据库尚未实现名称字面筛选。");
        // 列只能来自已映射实体的直接字符串属性，不能传方法、导航或任意SQL表达式。
        if (storageColumn.Body is not MemberExpression { Expression: ParameterExpression parameter, Member: PropertyInfo property } ||
            parameter != storageColumn.Parameters[0] || property.PropertyType != typeof(string) || property.GetMethod is not { IsStatic: false, IsPublic: true })
            throw new ArgumentException("名称存储列必须是已映射实体的直接字符串属性。", nameof(storageColumn));
        var table = session.Orm.CodeFirst.GetTableByEntity(typeof(T));
        if (table is null || !table.ColumnsByCs.TryGetValue(property.Name, out var mapped) || mapped.Attribute.IsIgnore)
            throw new ArgumentException("名称存储列没有实体映射。", nameof(storageColumn));
        MetadataValidation.RequireIdentifier(mapped.Attribute.Name);
        var pattern = "%" + keyword.Replace("!", "!!", StringComparison.Ordinal).Replace("%", "!%", StringComparison.Ordinal).Replace("_", "!_", StringComparison.Ordinal) + "%";
        var call = Expression.Call(typeof(DatabaseFunctions).GetMethod(jsonArray ? nameof(DatabaseFunctions.AnyLike) : nameof(DatabaseFunctions.ScalarLike))!, storageColumn.Body, Expression.Constant(pattern));
        return query.Where(Expression.Lambda<Func<T, bool>>(call, storageColumn.Parameters));
    }

    /// <summary>通过FreeSql公开解析合同取得真实列与别名，方言只拼固定结构且所有值独立绑定。</summary>
    [ExpressionCall]
    private static class DatabaseFunctions
    {
        private static readonly ThreadLocal<ExpressionCallContext> context = new();

        /// <summary>JSON字符串及精确整数成员共用十进制文本比较，异常历史结构不匹配。</summary>
        public static bool HasId(string? document,[RawValue] string id)
        {
            var call=context.Value??throw new InvalidOperationException("JSON成员谓词只能用于数据库表达式。");
            if(call.DataType!=DataType.MySql)throw new NotSupportedException("当前数据库尚未实现复选成员筛选。");
            var column=call.ParsedContent[nameof(document)];var name="mt_json_id_"+Guid.NewGuid().ToString("N");
            call.UserParameters.Add(new MySqlParameter(name,MySqlDbType.VarChar){Value=id});
            var array=$"CASE WHEN JSON_VALID({column})=1 THEN CASE WHEN JSON_TYPE({column})='ARRAY' AND JSON_DEPTH({column})<=2 THEN {column} ELSE '[]' END ELSE '[]' END";
            // 与非冗余计划共用有界标量形式，避免WHERE EXISTS对不同来源数组产生漏判。
            call.Result=$"((SELECT 1 FROM JSON_TABLE({array},'$[*]' COLUMNS(member_id VARCHAR(32) PATH '$' NULL ON ERROR)) checkbox_ids WHERE CAST(checkbox_ids.member_id AS BINARY)=CAST(@{name} AS BINARY) LIMIT 1) IS NOT NULL)";
            return false;
        }

        /// <summary>标量名称必须用列LIKE保留其真实比较规则，不能以INSTR/Contains代替旧LIKE的重音语义。</summary>
        public static bool ScalarLike(string? document, [RawValue] string pattern)
        {
            var call = context.Value ?? throw new InvalidOperationException("名称谓词只能用于数据库表达式。");
            if (call.DataType != DataType.MySql) throw new NotSupportedException("当前数据库尚未实现名称字面筛选。");
            var column = call.ParsedContent[nameof(document)];
            var name = "mt_scalar_name_" + Guid.NewGuid().ToString("N");
            call.UserParameters.Add(new MySqlParameter(name, MySqlDbType.LongText) { Value = pattern });
            call.Result = $"({column} LIKE @{name} ESCAPE '!')";
            return false;
        }

        /// <summary>仅在ORM解析中执行；非法、非数组、嵌套非空结构按异常历史数据整行不匹配。</summary>
        public static bool AnyLike(string? document, [RawValue] string pattern)
        {
            var call = context.Value ?? throw new InvalidOperationException("JSON名称数组谓词只能用于数据库表达式。");
            if (call.DataType != DataType.MySql) throw new NotSupportedException("当前数据库尚未实现名称字面筛选。");
            var column = call.ParsedContent[nameof(document)];
            // 每次解析独立命名，与既有筛选、多个名称谓词及Count/Page重用互不覆盖。
            var name = "mt_json_name_" + Guid.NewGuid().ToString("N");
            call.UserParameters.Add(new MySqlParameter(name, MySqlDbType.LongText) { Value = pattern });
            var array = $"CASE WHEN JSON_VALID({column}) = 1 THEN CASE WHEN JSON_TYPE({column}) = 'ARRAY' AND JSON_DEPTH({column}) <= 2 THEN {column} ELSE '[]' END ELSE '[]' END";
            // 用固定物理列的零长度片段使模式继承实际列collation；不读运行时元数据，不猜默认排序规则。
            call.Result = $"(JSON_SEARCH({array}, 'one', CONCAT(LEFT({column}, 0), @{name}), '!', '$[*]') IS NOT NULL)";
            return false;
        }
    }
}
