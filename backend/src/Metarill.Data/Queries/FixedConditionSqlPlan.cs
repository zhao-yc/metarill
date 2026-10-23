using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FreeSql;
using Metarill.Core.Metadata;
using Metarill.Core.Contracts;

namespace Metarill.Data.Queries;

/// <summary>字段类型及字面量在生成/发布时固定；宿主仅提供明确的身份/表单窄变量，不授予任何页面权限。</summary>
public sealed class FixedConditionSqlPlan
{
    private sealed record NumberText(string Text);
    private sealed record Value(string? Variable,object? Literal);
    private sealed record Term(string Field,string Type,FixedConditionComparison Comparison,IReadOnlyList<Value> Values);
    private readonly IReadOnlyList<Term> terms;
    /// <summary>保存已转换的常量及固定变量目录，不保留可变元数据字典。</summary>
    private FixedConditionSqlPlan(IReadOnlyList<Term> terms,IReadOnlyList<string> variables){this.terms=terms;Variables=variables;}
    [JsonIgnore] public IReadOnlyList<string> Variables{get;}

    /// <summary>只编译已证明字段的逻辑类型；未知类型或无效字面量在发出命令前拒绝。</summary>
    public static FixedConditionSqlPlan Compile(FixedConditionPlan syntax,IReadOnlyDictionary<string,string> logicalTypes)
    {
        ArgumentNullException.ThrowIfNull(syntax);ArgumentNullException.ThrowIfNull(logicalTypes);
        var fields=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var pair in logicalTypes){MetadataValidation.RequireIdentifier(pair.Key);if(!fields.TryAdd(pair.Key,pair.Value))throw Invalid();}
        var result=new List<Term>();
        foreach(var term in syntax.Terms)
        {
            if(!fields.TryGetValue(term.Field,out var declared))throw Invalid();
            var empty=term.Comparison is FixedConditionComparison.IsNull or FixedConditionComparison.IsNotNull;
            var type=empty?"":Type(declared);
            if(term.Comparison==FixedConditionComparison.Like&&type!="string")throw Invalid();
            var values=term.Values.Select(value=>value.Kind==FixedConditionValueKind.Variable?new Value(value.Text,null):new Value(null,Scalar(value.Kind switch
            {FixedConditionValueKind.String=>value.Text,FixedConditionValueKind.Number=>new NumberText(value.Text),FixedConditionValueKind.Boolean=>value.Text=="true",FixedConditionValueKind.Null=>null,_=>throw Invalid()},type))).ToArray();
            result.Add(new(term.Field,type,term.Comparison,Array.AsReadOnly(values)));
        }
        return new(result.AsReadOnly(),Array.AsReadOnly(syntax.Variables.ToArray()));
    }
    /// <summary>请求只绑定已编译窄变量，生成不可变常量计划；不重新解析元数据或接受外部字段。</summary>
    public FixedConditionSqlPlan BindVariables(IReadOnlyDictionary<string,object?> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        var bound=terms.Select(term=>term with{Values=Array.AsReadOnly(term.Values.Select(value=>
        {
            if(value.Variable is not{} name)return value;
            if(!variables.TryGetValue(name,out var current))throw new ArgumentException("固定条件缺少明确的上下文变量。");
            return new Value(null,Scalar(current,term.Type));
        }).ToArray())}).ToArray();
        return new(Array.AsReadOnly(bound),Array.Empty<string>());
    }
    /// <summary>页面条件与控件条件始终AND合并，保留各自类型和值，不允许同字段采用矛盾的发布类型。</summary>
    public FixedConditionSqlPlan And(FixedConditionSqlPlan other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var combined=terms.Concat(other.terms).ToArray();
        if(combined.Where(term=>term.Type.Length>0).GroupBy(term=>term.Field,StringComparer.OrdinalIgnoreCase).Any(group=>group.Select(term=>term.Type).Distinct(StringComparer.Ordinal).Count()>1))throw new InvalidOperationException("固定条件合并的字段类型不一致。");
        return new(Array.AsReadOnly(combined),Array.AsReadOnly(Variables.Concat(other.Variables).Distinct(StringComparer.Ordinal).ToArray()));
    }
    /// <summary>跨表固定条件须再次证明真实物理字段及其逻辑类型，不能借调用者自报类型放宽比较。</summary>
    internal void RequirePhysicalTypes(IReadOnlyDictionary<string,string> physicalTypes)
    {
        foreach(var term in terms)if(!physicalTypes.TryGetValue(term.Field,out var type)||term.Type.Length>0&&Type(type)!=term.Type)throw new InvalidOperationException("跨表目标条件与已发布物理字段不符。");
    }
    /// <summary>同一类型化常量计划逐项比较，精简目录不能让真实目标条件被遗漏或替换。</summary>
    internal bool SameConstants(FixedConditionSqlPlan other)=>JsonSerializer.Serialize(terms)==JsonSerializer.Serialize(other.terms);
    /// <summary>表单依赖先按发布的源字段类型绑定，再由目标条件复核，避免源int32借目标int64放宽范围。</summary>
    public static object? BindScalar(object? value,string logicalType)=>Scalar(value,Type(logicalType));
    /// <summary>条件总是作为原查询的AND追加，值用命名参数绑定；调用方仍须先建立真实身份和对象范围。</summary>
    public ISelect<T> Apply<T>(ISelect<T> query,DatabaseSession session,IReadOnlyDictionary<string,object?> variables,string parameterPrefix)where T:class
    {
        var clause=Render(session.Provider,variables,parameterPrefix,session.Provider.QuoteIdentifier);
        return clause.Sql.Length==0?query:query.Where(clause.Sql,clause.Parameters);
    }
    private static readonly IReadOnlyDictionary<string,object?> EmptyVariables=new Dictionary<string,object?>();
    /// <summary>页面常量计划不接受上下文变量，复用不可变空槽，避免请求提供身份覆盖。</summary>
    public ISelect<T> ApplyConstants<T>(ISelect<T> query,DatabaseSession session,string parameterPrefix)where T:class
    {
        var clause=RenderConstants(session.Provider,parameterPrefix,session.Provider.QuoteIdentifier);
        return clause.Sql.Length==0?query:query.Where(clause.Sql,clause.Parameters);
    }
    /// <summary>向适配器自建命令绑定已发布常量，返回固定字段谓词；不接受外部SQL或变量。</summary>
    public string BindConstants(DbCommand command,DatabaseSession session,string parameterPrefix)
    {
        ArgumentNullException.ThrowIfNull(command);ArgumentNullException.ThrowIfNull(session);
        var clause=RenderConstants(session.Provider,parameterPrefix,session.Provider.QuoteIdentifier);
        var existing=command.Parameters.Cast<DbParameter>().Select(parameter=>parameter.ParameterName.TrimStart('@',':','?')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(clause.Parameters.Keys.Any(existing.Contains))throw new ArgumentException("固定条件参数与命令已有参数冲突。");
        foreach(var pair in clause.Parameters)
        {var parameter=command.CreateParameter();parameter.ParameterName=pair.Key;parameter.Value=pair.Value??DBNull.Value;command.Parameters.Add(parameter);}
        return clause.Sql;
    }
    /// <summary>非冗余适配器只能传已编译字段表达式，保留NULL与原LIKE的SQL语义。</summary>
    internal Clause RenderConstants(IDatabaseProvider provider,string prefix,Func<string,string> expression)
    {
        if(Variables.Count!=0)throw new InvalidOperationException("常量页面计划不能包含上下文变量。");
        return Render(provider,EmptyVariables,prefix,expression);
    }
    internal sealed record Clause(string Sql,[property:JsonIgnore] IReadOnlyDictionary<string,object?> Parameters);
    /// <summary>物理或固定联接表达式由适配器提供；此入口不接受客户端SQL、列名或操作符。</summary>
    internal Clause Render(IDatabaseProvider provider,IReadOnlyDictionary<string,object?> variables,string parameterPrefix,Func<string,string> expression)
    {
        ArgumentNullException.ThrowIfNull(variables);MetadataValidation.RequireIdentifier(parameterPrefix);
        var parameters=new Dictionary<string,object?>();var predicates=new List<string>();
        foreach(var term in terms)
        {
            var column=expression(term.Field);
            if(term.Comparison is FixedConditionComparison.IsNull or FixedConditionComparison.IsNotNull)
            {predicates.Add(column+(term.Comparison==FixedConditionComparison.IsNull?" IS NULL":" IS NOT NULL"));continue;}
            var marks=new List<string>();
            foreach(var value in term.Values)
            {
                object? scalar=value.Literal;
                if(value.Variable is {} variable)
                {if(!variables.TryGetValue(variable,out var current))throw new ArgumentException("固定条件缺少明确的上下文变量。");scalar=Scalar(current,term.Type);}
                // =NULL与IN(NULL)保持SQL三值语义，不能偷换成IS NULL；空值无须依赖ORM的空参数重写。
                if(scalar is null){marks.Add("NULL");continue;}
                var name=parameterPrefix+"_"+parameters.Count;parameters.Add(name,scalar);marks.Add(provider.ParameterPlaceholder(name));
            }
            var operation=term.Comparison switch
            {FixedConditionComparison.Equal=>"=",FixedConditionComparison.NotEqual=>"<>",FixedConditionComparison.Greater=>">",FixedConditionComparison.GreaterOrEqual=>">=",FixedConditionComparison.Less=>"<",FixedConditionComparison.LessOrEqual=>"<=",FixedConditionComparison.Like=>" LIKE ",FixedConditionComparison.In=>" IN ",_=>throw Invalid()};
            predicates.Add(column+operation+(term.Comparison==FixedConditionComparison.In?"("+string.Join(',',marks)+")":marks.Single()));
        }
        return new(string.Join(" AND ",predicates.Select(predicate=>"("+predicate+")")),parameters);
    }
    /// <summary>变量只接受单个可表示值，JSON对象/数组和浮点ID不能隐式转换或展开成更宽范围。</summary>
    private static object? Scalar(object? value,string type)
    {
        var culture=CultureInfo.InvariantCulture;
        if(value is JsonElement json)value=json.ValueKind switch
        {JsonValueKind.Null=>null,JsonValueKind.String=>json.GetString(),JsonValueKind.Number=>new NumberText(json.GetRawText()),JsonValueKind.True=>true,JsonValueKind.False=>false,_=>throw Invalid()};
        if(value is null)return null;
        if(value is NumberText numericLiteral)
        {
            if(type=="string")throw Invalid();
            if(type is "int64" or "int32" or "bool")value=ExactNumericValue.TryInt64(numericLiteral.Text,out var integer)?integer:throw Invalid();
            else if(type=="double")value=ExactNumericValue.TryDouble(numericLiteral.Text,out var real)?real:throw Invalid();
            else if(type=="decimal")value=ExactNumericValue.TryDecimal(numericLiteral.Text,out var exact)?exact:throw Invalid();
            else throw Invalid();
        }
        if(type=="string")return value is string text?text:throw Invalid();
        if(type is "int64" or "int32")
        {
            decimal integerNumber=value switch{int integer=>integer,long wide=>wide,decimal exact=>exact,bool flag=>flag?1:0,string text when long.TryParse(text,NumberStyles.Integer,culture,out var integer)=>integer,_=>throw Invalid()};
            if(decimal.Truncate(integerNumber)!=integerNumber||integerNumber<long.MinValue||integerNumber>long.MaxValue||type=="int32"&&(integerNumber<int.MinValue||integerNumber>int.MaxValue))throw Invalid();
            return type=="int32"?(object)(int)integerNumber:(long)integerNumber;
        }
        if(type=="decimal")return value switch{int integer=>(decimal)integer,long wide=>(decimal)wide,decimal exact=>exact,string text when ExactNumericValue.TryDecimal(text,out var exact)=>exact,_=>throw Invalid()};
        if(type=="double")
        {
            var realNumber=value switch{int integer=>(double)integer,long wide=>(double)wide,decimal exact=>(double)exact,double real=>real,float real=>real,string text when ExactNumericValue.TryDouble(text,out var real)=>real,_=>throw Invalid()};
            return double.IsFinite(realNumber)?realNumber:throw Invalid();
        }
        if(type=="bool")return value switch{bool flag=>flag,int integer when integer is 0 or 1=>integer==1,long wide when wide is 0 or 1=>wide==1,decimal exact when exact is 0m or 1m=>exact==1m,string text when text is "0" or "1"=>text=="1",string text when bool.TryParse(text,out var flag)=>flag,_=>throw Invalid()};
        if(type is "date" or "datetime")return value switch{DateTime date=>date,string text when DateTime.TryParse(text,culture,DateTimeStyles.RoundtripKind,out var date)=>date,_=>throw Invalid()};
        if(type=="time")return value switch{TimeSpan time=>time,string text when TimeSpan.TryParse(text,culture,out var time)=>time,_=>throw Invalid()};
        throw Invalid();
    }
    /// <summary>逻辑类型不带数据库品牌，方言名称和占位符由实际适配器承担。</summary>
    private static string Type(string value)=>value.ToLowerInvariant() switch
    {"id" or "long" or "bigint" or "int64"=>"int64","integer" or "int" or "int32"=>"int32","boolean" or "bool"=>"bool","money" or "number" or "decimal"=>"decimal","double"=>"double","date"=>"date","datetime" or "timestamp"=>"datetime","time"=>"time","string" or "text" or "varchar"=>"string",_=>throw Invalid()};
    /// <summary>拒绝信息不回显账号、表单值或含字面量的原配置。</summary>
    private static ArgumentException Invalid()=>new("固定条件的值或逻辑类型不符合已发布字段定义。");
}
