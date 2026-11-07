using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace Metarill.Core.Metadata;

/// <summary>发布时编译原按钮结构化显示规则；只比较实际记录值，不执行脚本、SQL或客户端规则。</summary>
public sealed partial class ButtonVisibilityRule
{
    private readonly Group root;
    private static readonly JsonSerializerOptions CompactJson=new(){Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    private ButtonVisibilityRule(Group root,IEnumerable<string> fields){this.root=root;Fields=Array.AsReadOnly(fields.Distinct(StringComparer.Ordinal).ToArray());}
    public IReadOnlyList<string> Fields {get;}
    /// <summary>空规则及JSON null沿原后端视为真；非法定义不能降级为空规则。</summary>
    public static ButtonVisibilityRule Compile(string? definition)
    {
        if(definition is null||BlankText(definition))return new(new(false,[]),[]);
        if(definition.Length>65536)throw Invalid();
        try
        {
            using var json=JsonDocument.Parse(definition,new JsonDocumentOptions{MaxDepth=40});
            if(json.RootElement.ValueKind==JsonValueKind.Null)return new(new(false,[]),[]);
            var fields=new List<string>();var budget=1024;var group=ParseGroup(json.RootElement,fields,ref budget,0);return new(group,fields);
        }
        catch(JsonException){throw Invalid();}
    }
    /// <summary>记录只能由宿主真实生成读取结果提供；规则不接收调用方的筛选或值覆盖。</summary>
    public bool Matches(IReadOnlyDictionary<string,object?> record)=>root.Matches(record);
    /// <summary>完整验证所有分组后才执行；OR分支不能隐藏非法运算符或无限递归。</summary>
    private static Group ParseGroup(JsonElement value,List<string> fields,ref int budget,int depth)
    {
        if(value.ValueKind!=JsonValueKind.Object||depth>16||--budget<0)throw Invalid();
        Unique(value);var nodes=new List<Node>();
        if(value.TryGetProperty("conditions",out var conditions)&&conditions.ValueKind!=JsonValueKind.Null)
        {
            if(conditions.ValueKind!=JsonValueKind.Array)throw Invalid();
            foreach(var condition in conditions.EnumerateArray())
            {
                if(condition.ValueKind==JsonValueKind.Null)continue;
                if(condition.ValueKind!=JsonValueKind.Object||--budget<0)throw Invalid();Unique(condition);
                var field=String(condition,"field")??"";var operation=String(condition,"operator");if(BlankText(operation))operation="eq";
                if(operation is not("eq" or "ne" or "gt" or "gte" or "lt" or "lte" or "in" or "notIn" or "isBlank" or "isNotBlank"))throw Invalid();
                if(field.Length>512||field.Count(c=>c=='.')>16)throw Invalid();fields.Add(field);
                var expected=condition.TryGetProperty("value",out var operand)?operand.Clone():default;
                var candidates=new List<object?>();
                if(condition.TryGetProperty("valueList",out var values)&&values.ValueKind==JsonValueKind.Array)candidates.AddRange(values.EnumerateArray().Select(item=>(object?)item.Clone()));
                else if(expected.ValueKind==JsonValueKind.Array)candidates.AddRange(expected.EnumerateArray().Select(item=>(object?)item.Clone()));
                else if(!Blank(expected))candidates.AddRange(Text(expected).Split([',','\n']).Where(item=>!BlankText(item)).Select(item=>(object?)TrimJava(item)));
                nodes.Add(new Condition(field,operation,expected,candidates.ToArray()));
            }
        }
        if(value.TryGetProperty("groups",out var groups)&&groups.ValueKind!=JsonValueKind.Null)
        {
            if(groups.ValueKind!=JsonValueKind.Array)throw Invalid();
            foreach(var child in groups.EnumerateArray())if(child.ValueKind!=JsonValueKind.Null)nodes.Add(ParseGroup(child,fields,ref budget,depth+1));
        }
        return new((String(value,"logic")??"AND").ToUpperInvariant()=="OR",nodes.ToArray());
    }
    private abstract record Node {internal abstract bool Matches(IReadOnlyDictionary<string,object?> record);}
    private sealed record Group(bool Or,Node[] Children):Node
    {
        internal override bool Matches(IReadOnlyDictionary<string,object?> record)=>Children.Length==0|| (Or?Children.Any(node=>node.Matches(record)):Children.All(node=>node.Matches(record)));
    }
    private sealed record Condition(string Field,string Operation,object? Expected,object?[] Candidates):Node
    {
        internal override bool Matches(IReadOnlyDictionary<string,object?> record)
        {
            var actual=Read(record,Field);
            return Operation switch
            {
                "isBlank"=>Blank(actual),"isNotBlank"=>!Blank(actual),
                "in"=>Candidates.Any(value=>Compare(actual,value)==0),"notIn"=>!Candidates.Any(value=>Compare(actual,value)==0),
                "eq"=>Compare(actual,Expected)==0,"ne"=>Compare(actual,Expected)!=0,"gt"=>Compare(actual,Expected)>0,"gte"=>Compare(actual,Expected)>=0,
                "lt"=>Compare(actual,Expected)<0,"lte"=>Compare(actual,Expected)<=0,_=>false
            };
        }
    }
    /// <summary>原点路径只读取Map/JSON对象，不反射任意对象成员，也不把字符串当JSON再次执行。</summary>
    private static object? Read(IReadOnlyDictionary<string,object?> record,string path)
    {
        if(BlankText(path))return null;object? current=record;
        foreach(var part in path.Split('.'))
        {
            if(current is IReadOnlyDictionary<string,object?> dictionary)current=dictionary.GetValueOrDefault(part);
            else if(current is IDictionary map)current=map.Contains(part)?map[part]:null;
            else if(current is JsonElement{ValueKind:JsonValueKind.Object} element)current=element.TryGetProperty(part,out var found)?found:null;
            else return null;
        }
        return current;
    }
    /// <summary>数值按十进制精确比较，保留大整数；字符串按原Java UTF-16顺序比较，不依赖服务器区域。</summary>
    private static int Compare(object? actual,object? expected)
    {
        var leftBlank=Blank(actual);var rightBlank=Blank(expected);if(leftBlank||rightBlank)return leftBlank?(rightBlank?0:-1):1;
        var left=TrimJava(Text(actual));var right=TrimJava(Text(expected));
        if(JavaDecimalNumber.Try(left,out var a)&&JavaDecimalNumber.Try(right,out var b))return a.CompareTo(b);
        return string.CompareOrdinal(left,right);
    }
    /// <summary>空白及空集合沿原后端语义；false、0、空对象不是空值。</summary>
    private static bool BlankText(string? value)=>value is null||value.All(character=>character is >= '\u001c' and <= '\u001f'||char.IsWhiteSpace(character)&&character is not('\u0085' or '\u00a0' or '\u2007' or '\u202f'));
    /// <summary>原记录/JSON的空值判定，不把false和0视为空。</summary>
    private static bool Blank(object? value)=>value switch
    {
        null=>true,string text=>BlankText(text),JsonElement element=>element.ValueKind switch{JsonValueKind.Null or JsonValueKind.Undefined=>true,JsonValueKind.String=>BlankText(element.GetString()),JsonValueKind.Array=>element.GetArrayLength()==0,_=>false},
        ICollection collection=>collection.Count==0&&value is not IDictionary,_=>false
    };
    /// <summary>使用原Java标量表示；日期不经本机时区或区域格式转换。</summary>
    private static string Text(object? value)=>value switch
    {
        null=>"null",string text=>text,bool boolean=>boolean?"true":"false",DateOnly date=>date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        TimeOnly time=>time.ToString("HH:mm:ss",CultureInfo.InvariantCulture),DateTime date=>date.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture)+"."+(date.ToString("fffffff",CultureInfo.InvariantCulture).TrimEnd('0') is var fraction&&fraction.Length>0?fraction:"0"),
        JsonElement element=>element.ValueKind switch{JsonValueKind.Null or JsonValueKind.Undefined=>"null",JsonValueKind.String=>element.GetString()!,JsonValueKind.True=>"true",JsonValueKind.False=>"false",JsonValueKind.Array or JsonValueKind.Object=>JsonSerializer.Serialize(element,CompactJson),_=>element.GetRawText()},
        IDictionary map=>"{"+string.Join(", ",map.Cast<DictionaryEntry>().Select(item=>Text(item.Key)+"="+Text(item.Value)))+"}",
        IEnumerable list=>"["+string.Join(", ",list.Cast<object?>().Select(Text))+"]",IFormattable scalar=>scalar.ToString(null,CultureInfo.InvariantCulture)??"",_=>value.ToString()??""
    };
    /// <summary>Java String.trim只去除UTF-16码元不大于空格的边界字符。</summary>
    private static string TrimJava(string value)
    {var first=0;var last=value.Length;while(first<last&&value[first]<=' ')first++;while(last>first&&value[last-1]<=' ')last--;return value[first..last];}
    /// <summary>兼容原JSON标量转字符串，不执行其中内容。</summary>
    private static string? String(JsonElement value,string key)=>value.TryGetProperty(key,out var found)&&found.ValueKind is not(JsonValueKind.Null or JsonValueKind.Undefined)?found.ValueKind==JsonValueKind.String?found.GetString():found.GetRawText():null;
    /// <summary>重复JSON键拒绝，不能借解析器选择差异覆盖运算符或字段。</summary>
    private static void Unique(JsonElement value){if(value.EnumerateObject().Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=value.EnumerateObject().Count())throw Invalid();}
    private static ArgumentException Invalid()=>new("按钮显示规则定义无效，未执行业务。");
}
