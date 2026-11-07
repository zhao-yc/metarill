using System.Data.Common;
using System.Text;
using FreeSql.Internal.CommonProvider;
using MySqlConnector;

namespace Metarill.Data.Relations;

/// <summary>从原 Mapper 的单行参数化 INSERT 取绑定，禁止改变浮点协议或绕过宿主 AOP。</summary>
internal sealed record MySqlInsertCacheBatch(string Sql,DbParameter[] Parameters,long ParameterBudget)
{
    /// <summary>只有自有、无写入钩子且不会自动同步结构的 ORM 可使用原生追加。</summary>
    internal static bool UsesPlainOrm(DatabaseSession session)
    {
        if (!session.UsesOwnedOrm) return false;
        var orm = session.Orm;
        var aop = orm.Aop;
        return aop.AuditValueHandler is null && aop.CurdBeforeHandler is null && aop.CurdAfterHandler is null &&
            aop.CommandBeforeHandler is null && aop.CommandAfterHandler is null &&
            aop.TraceBeforeHandler is null && aop.TraceAfterHandler is null &&
            !orm.CodeFirst.IsAutoSyncStructure && orm.GlobalFilter.GetAllFilters().Count == 0;
    }

    /// <summary>只接受默认 ORM 生成的固定表与一一对应的 VALUES 参数，其他形态回到原 Mapper。</summary>
    internal static MySqlInsertCacheBatch? Prepare<T>(DatabaseSession session,string table,long id,T entity,string? parameterPrefix=null,(string Column,long Id)? parentKey=null) where T:class
    {
        if (!UsesPlainOrm(session)) return null;
        if(session.Insert<T>().AppendData(entity) is not InsertProvider<T> insert)
            return null;
        var original=insert.ToSql();
        var quotedTable=session.Provider.QuoteIdentifier(table);
        var prefix="INSERT INTO "+quotedTable+"(";
        const string separator=") VALUES(";
        if(original is null||!original.StartsWith(prefix,StringComparison.Ordinal)||!original.EndsWith(')'))return null;
        var split=original.IndexOf(separator,StringComparison.Ordinal);
        if(split<prefix.Length)return null;
        var columns=original[prefix.Length..split].Split(", ",StringSplitOptions.None);
        var placeholders=original[(split+separator.Length)..^1].Split(", ",StringSplitOptions.None);
        if(insert._params is not {} parameters||columns.Length!=parameters.Length||placeholders.Length!=parameters.Length||
            columns.Distinct(StringComparer.Ordinal).Count()!=columns.Length)
            return null;
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"@expected_database","@exact_id"};
        long budget=0;
        var exactId=false;
        var exactParent=parentKey is null;
        for(var index=0;index<columns.Length;index++)
        {
            var column=columns[index];
            var name=placeholders[index];
            if(column.Length<3||column[0]!='`'||column[^1]!='`'||!Identifier(column.AsSpan(1,column.Length-2))||
                name.Length<2||name[0]!='@'||!Identifier(name.AsSpan(1))||!names.Add(name)||
                parameters[index] is not MySqlParameter parameter||parameter.ParameterName!=name)
                return null;
            if(column=="`id`")exactId=parameter.Value is long key&&key==id;
            if(parentKey is {} parent&&column=="`"+parent.Column+"`")
                exactParent=parameter.Value is long parentId&&parentId==parent.Id ||
                    parameter.Value is string parentText&&parentText==parent.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var bytes=ParameterBytes(parameter.Value);
            if(bytes<0)return null;
            budget+=bytes;
        }
        if(!exactId||!exactParent)return null;
        // 多行命令各用独立参数名，保留原 MySqlParameter 的类型、值与绑定顺序。
        if(parameterPrefix is not null)
        {
            for(var index=0;index<parameters.Length;index++)
            {
                placeholders[index]="@"+parameterPrefix+placeholders[index][1..];
                parameters[index].ParameterName=placeholders[index];
            }
        }
        var database=session.Provider.QuoteIdentifier(session.Options.ExpectedDatabase);
        return new("INSERT INTO "+database+"."+quotedTable+"("+string.Join(", ",columns)+") SELECT "+
            string.Join(", ",placeholders)+" WHERE CAST(DATABASE() AS BINARY)=CAST(@expected_database AS BINARY)",parameters,budget);
    }

    /// <summary>估算固定文本协议参数大小；需要不同传输形态的值回到原命令。</summary>
    internal static long ParameterBytes(object? value) => value switch
    {
        null or DBNull or bool or byte or sbyte or short or ushort or int or uint or long or ulong or decimal or DateTime or DateOnly =>128L,
        string text=>6L*Encoding.UTF8.GetByteCount(text)+128,
        _=>-1
    };

    /// <summary>接受原 ORM 的普通 ASCII 标识符，阻止把额外 SQL 拼进命令。</summary>
    private static bool Identifier(ReadOnlySpan<char> value)
    {
        foreach(var character in value)
            if(!char.IsAsciiLetterOrDigit(character)&&character!='_')return false;
        return !value.IsEmpty;
    }
}
