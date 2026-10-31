using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Metarill.Core.Metadata;

/// <summary>发布时编译原Java受限只读条件；只读已装载记录，不执行JavaScript、方法或赋值。</summary>
public sealed partial class ReadOnlyConditionRule
{
    private readonly Node root;
    private ReadOnlyConditionRule(Node root,IEnumerable<string> fields){this.root=root;Fields=Array.AsReadOnly(fields.Distinct(StringComparer.Ordinal).ToArray());}
    public IReadOnlyList<string> Fields {get;}
    /// <summary>完整消费语法并收集固定字段；即使布尔分支将短路，也不能藏入脚本或反射属性。</summary>
    public static ReadOnlyConditionRule Compile(string? source)
    {
        if(source is null||source.All(JavaWhitespace))return new(new Literal(false),[]);
        if(source.Length>4096)throw Invalid();
        var parser=new Parser(source);var expression=parser.Or();if(parser.Current.Kind!="end")throw Invalid();
        return new(expression,parser.Fields);
    }
    /// <summary>每次使用服务端原记录重新求值；字段缺失、对象转换及非有限值均拒绝，不默认成可写。</summary>
    public bool IsReadOnly(IReadOnlyDictionary<string,object?> record)=>Truth(root.Evaluate(record));
    /// <summary>只把已编译语法树输出为静态JS表达式；字段由生成器固定绑定，不重新执行或拼入原条件文本。</summary>
    public string ToJavaScript(Func<string,string> bindField)
    {
        ArgumentNullException.ThrowIfNull(bindField);
        return Render(root);
        string Render(Node node)=>node switch
        {
            Literal {NumberText:{} number}=>JavaScriptNumber(number),
            Literal literal=>JsonSerializer.Serialize(literal.Value),
            Field field=>bindField(field.Name),
            Not not=>"(!"+Render(not.Value)+")",
            Binary binary=>"("+Render(binary.Left)+" "+binary.Operation+" "+Render(binary.Right)+")",
            _=>throw Invalid()
        };
    }
    /// <summary>普通十进制令牌归一为JS模块合法数字，去除旧式八进制前导零并映射Unicode十进制数字。</summary>
    private static string JavaScriptNumber(string source)
    {
        var text=new string(source.Select(c=>char.IsDigit(c)?(char)('0'+(int)char.GetNumericValue(c)):c).ToArray());
        var negative=text.StartsWith('-');if(negative)text=text[1..];var dot=text.IndexOf('.');var integer=dot<0?text:text[..dot];
        integer=integer.TrimStart('0');if(integer.Length==0)integer="0";
        var fraction=dot<0?"":"."+(dot+1==text.Length?"0":text[(dot+1)..]);return (negative?"-":"")+integer+fraction;
    }
    private abstract record Node{internal abstract object? Evaluate(IReadOnlyDictionary<string,object?> row);}
    private sealed record Literal(object? Value,string? NumberText=null):Node{internal override object? Evaluate(IReadOnlyDictionary<string,object?> row)=>Value;}
    private sealed record Field(string Name):Node
    {
        internal override object? Evaluate(IReadOnlyDictionary<string,object?> row)
        {
            object? value=row;
            foreach(var part in Name.Split('.'))
            {
                value=Unwrap(value);
                if(value is IReadOnlyDictionary<string,object?> fields){if(!fields.TryGetValue(part,out value))throw Invalid();}
                else if(value is IDictionary dictionary){if(!dictionary.Contains(part))throw Invalid();value=dictionary[part];}
                else if(value is JsonElement json&&json.ValueKind==JsonValueKind.Object){if(!json.TryGetProperty(part,out var child))throw Invalid();value=child;}
                else throw Invalid();
            }
            return Unwrap(value);
        }
    }
    private sealed record Not(Node Value):Node{internal override object? Evaluate(IReadOnlyDictionary<string,object?> row)=>!Truth(Value.Evaluate(row));}
    private sealed record Binary(string Operation,Node Left,Node Right):Node
    {
        internal override object? Evaluate(IReadOnlyDictionary<string,object?> row)
        {
            // 原Java两侧都求值并保留&&/||选中的操作数；不能用短路跳过缺失字段。
            var left=Left.Evaluate(row);var right=Right.Evaluate(row);
            return Operation switch
            {
                "||"=>Truth(left)?left:right,"&&"=>Truth(left)?right:left,
                "=="=>Equal(left,right,false),"!="=>!Equal(left,right,false),
                "==="=>Equal(left,right,true),"!=="=>!Equal(left,right,true),
                ">"=>Compare(left,right)>0,">="=>Compare(left,right)>=0,"<"=>Compare(left,right)<0,"<="=>Compare(left,right)<=0,
                _=>throw Invalid()
            };
        }
    }
    /// <summary>JSON数值保留十进制文本，不经double或decimal缩窄；对象只允许明确字典/JSON路径。</summary>
    private static object? Unwrap(object? value)=>value is not JsonElement json?value:json.ValueKind switch
    {JsonValueKind.Null=>null,JsonValueKind.True=>true,JsonValueKind.False=>false,JsonValueKind.String=>json.GetString(),JsonValueKind.Number=>ParseNumber(json.GetRawText()),JsonValueKind.Object or JsonValueKind.Array=>json,_=>throw Invalid()};
    /// <summary>仅识别Java Number对应的标量，不对任意对象执行字符串或数值转换。</summary>
    private static bool IsNumber(object value)=>value is JavaDecimalNumber or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or BigInteger;
    /// <summary>空对象/数组为真，非空字符串不因文字等于false或空白就变成假。</summary>
    private static bool Truth(object? value)=>value switch{null=>false,bool flag=>flag,string text=>text.Length!=0,_ when IsNumber(value)=>Numeric(value).Sign!=0,_=>true};
    /// <summary>严格比较不隐式转换字符串/布尔，宽松比较沿原Java标量数值转换，其他对象比较拒绝。</summary>
    private static bool Equal(object? left,object? right,bool strict)
    {
        if(left is null||right is null)return left is null&&right is null;
        if(IsNumber(left)&&IsNumber(right))return Numeric(left).CompareTo(Numeric(right))==0;
        if(left is string a&&right is string b)return a==b;
        if(left is bool x&&right is bool y)return x==y;
        if(strict)return false;
        if(left is bool||right is bool||IsNumber(left)||IsNumber(right))return Numeric(left).CompareTo(Numeric(right))==0;
        throw Invalid();
    }
    /// <summary>两个字符串按UTF-16文字序比较，其余只走受限数值比较。</summary>
    private static int Compare(object? left,object? right)=>left is string a&&right is string b?string.CompareOrdinal(a,b):Numeric(left).CompareTo(Numeric(right));
    /// <summary>Java trim仅去掉U+0020及以下字符；无效值拒绝，不能悄悄变成0。</summary>
    private static JavaDecimalNumber Numeric(object? value)
    {
        if(value is JavaDecimalNumber number)return number;
        if(value is null)return ParseNumber("0");if(value is bool flag)return ParseNumber(flag?"1":"0");
        if(value is not string&&!IsNumber(value))throw Invalid();
        var text=value is string raw?raw:((IFormattable)value).ToString(value is double or float?"R":null,CultureInfo.InvariantCulture);
        var first=0;var last=text.Length;while(first<last&&text[first]<=' ')first++;while(last>first&&text[last-1]<=' ')last--;
        return ParseNumber(first==last?"0":text[first..last]);
    }
    /// <summary>对齐Character.isWhitespace；NBSP、窄NBSP和数字空格不属于语法空白。</summary>
    private static bool JavaWhitespace(char c)=>c is >= '\t' and <= '\r' or >= '\u001c' and <= '\u001f'||char.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator||char.GetUnicodeCategory(c)==UnicodeCategory.SpaceSeparator&&c is not('\u00a0' or '\u2007' or '\u202f');
    private static bool IdentifierStart(char c)=>char.IsLetter(c)||char.GetUnicodeCategory(c) is UnicodeCategory.LetterNumber or UnicodeCategory.CurrencySymbol or UnicodeCategory.ConnectorPunctuation;
    private static bool IdentifierPart(char c)=>IdentifierStart(c)||char.IsDigit(c)||char.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.Format||c is <= '\u0008' or >= '\u000e' and <= '\u001b' or >= '\u007f' and <= '\u009f';
    private static ArgumentException Invalid()=>new("页面只读条件无法可靠解释，未执行业务。");
    private sealed record Token(string Kind,string Text,object? Value=null);
    /// <summary>递归下降保持原优先级、32层/512令牌限制与受限字符串转义。</summary>
    private sealed class Parser
    {
        private readonly string source;private int index,tokens,depth;
        internal Token Current{get;private set;}=new("end","");
        internal List<string> Fields{get;}=[];
        internal Parser(string source){this.source=source;Next();}
        internal Node Or(){var result=And();while(Accept("||"))result=new Binary("||",result,And());return result;}
        private Node And(){var result=Comparison();while(Accept("&&"))result=new Binary("&&",result,Comparison());return result;}
        private Node Comparison(){var left=Unary();var operation=Current.Kind;if(operation is not("==" or "===" or "!=" or "!==" or ">" or ">=" or "<" or "<="))return left;Next();return new Binary(operation,left,Unary());}
        /// <summary>否定及括号按原32层上限展开，字段前缀与禁止路径在发布时固定。</summary>
        private Node Unary()
        {
            if(++depth>32)throw Invalid();
            try
            {
                if(Accept("!"))return new Not(Unary());
                if(Accept("(")){var value=Or();if(!Accept(")"))throw Invalid();return value;}
                var token=Current;if(Accept("literal"))return new Literal(token.Value,token.Value is JavaDecimalNumber?token.Text:null);
                if(Accept("field"))
                {
                    var name=token.Text.StartsWith("this.entity.",StringComparison.Ordinal)?token.Text[12..]:token.Text.StartsWith("entity.",StringComparison.Ordinal)?token.Text[7..]:token.Text;
                    if(name.Split('.').Any(part=>part.Length==0||part is "class" or "getClass" or "constructor" or "prototype" or "__proto__" or "undefined" or "NaN" or "Infinity"))throw Invalid();
                    Fields.Add(name);return new Field(name);
                }
                throw Invalid();
            }
            finally{depth--;}
        }
        private bool Accept(string kind){if(Current.Kind!=kind)return false;Next();return true;}
        /// <summary>令牌只允许字段、普通十进制字面量和固定运算符；不支持指数数值字面量或调用。</summary>
        private void Next()
        {
            if(++tokens>512)throw Invalid();while(index<source.Length&&JavaWhitespace(source[index]))index++;
            if(index==source.Length){Current=new("end","");return;}
            var start=source[index];if(start is '\'' or '"'){Current=new("literal","",ReadString(start));return;}
            foreach(var operation in new[]{"===","!==","==","!=",">=","<=","&&","||","!",">","<","(",")"})
                if(source.AsSpan(index).StartsWith(operation,StringComparison.Ordinal)){index+=operation.Length;Current=new(operation,operation);return;}
            if(char.IsDigit(start)||start=='-'&&index+1<source.Length&&char.IsDigit(source[index+1]))
            {
                var begin=index++;while(index<source.Length&&(char.IsDigit(source[index])||source[index]=='.'))index++;
                var number=source[begin..index];Current=new("literal",number,ParseNumber(number));return;
            }
            if(IdentifierStart(start))
            {
                var begin=index++;while(index<source.Length&&(IdentifierPart(source[index])||source[index]=='.'))index++;
                var word=source[begin..index];Current=word switch{"true"=>new("literal",word,true),"false"=>new("literal",word,false),"null"=>new("literal",word),_=>new("field",word)};return;
            }
            throw Invalid();
        }
        /// <summary>仅还原原实现允许的常见转义，不解释模板或Unicode脚本转义。</summary>
        private string ReadString(char quote)
        {
            index++;var result=new StringBuilder();while(index<source.Length)
            {
                var c=source[index++];if(c==quote)return result.ToString();if(c!='\\'){result.Append(c);continue;}
                if(index>=source.Length)throw Invalid();result.Append(source[index++] switch{'\\'=>'\\','\''=>'\'','"'=>'"','n'=>'\n','r'=>'\r','t'=>'\t',_=>throw Invalid()});
            }
            throw Invalid();
        }
    }
    /// <summary>只读条件的单值数值转换有界，超限明确拒绝；不影响显示规则既有文字回退语义。</summary>
    private static JavaDecimalNumber ParseNumber(string text)
    {if(text.Length>65536||!JavaDecimalNumber.Try(text,out var number))throw Invalid();return number;}
}
