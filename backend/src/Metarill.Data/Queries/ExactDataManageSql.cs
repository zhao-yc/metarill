using FreeSql;
using Metarill.Core.Metadata;

namespace Metarill.Data.Queries;

/// <summary>精确范围集合按固定名称绑定ADO参数，避免ORM的Contains把账号匹配值内联进SQL或日志。</summary>
public static class ExactDataManageSql
{
    /// <summary>只接收生成期固定物理列/前缀；值永不进入SQL语法，空集合固定不匹配。</summary>
    public static ISelect<T> Apply<T>(ISelect<T> query,DatabaseSession session,string column,string parameterPrefix,IReadOnlyList<object?> values)where T:class
    {
        MetadataValidation.RequireIdentifier(column);MetadataValidation.RequireIdentifier(parameterPrefix);
        if(values.Count==0)return query.Where("1=0");
        var provider=session.Provider;
        var parameters=new Dictionary<string,object?>();var names=new List<string>();
        foreach(var value in values)
        {
            if(value is not(string or long or int or decimal or double or bool or DateTime))throw new ArgumentException("精确管理只接受已转换的非空标量参数。");
            var name=parameterPrefix+"_"+parameters.Count;parameters.Add(name,value);names.Add(provider.ParameterPlaceholder(name));
        }
        // Where的对象重载识别参数字典；DbParameter数组会被当普通对象而丢失绑定。
        return query.Where(provider.QuoteIdentifier(column)+" IN ("+string.Join(",",names)+")",parameters);
    }
}
