using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Metarill.Core.Metadata;

public enum FixedConditionComparison { Equal, NotEqual, Greater, GreaterOrEqual, Less, LessOrEqual, Like, In, IsNull, IsNotNull }
public enum FixedConditionValueKind { String, Number, Boolean, Null, Variable }

/// <summary>只由条件编译器构造的值节点；变量与字面量分离，不保存可执行SQL。</summary>
public sealed class FixedConditionValue
{
    /// <summary>节点只由编译器创建，避免运行时重写操作数形状。</summary>
    internal FixedConditionValue(FixedConditionValueKind kind,string text){Kind=kind;Text=text;}
    public FixedConditionValueKind Kind {get;}
    [JsonIgnore] public string Text {get;}
}

/// <summary>字段已绑定发布目录中的规范名称，操作符和操作数不可由请求替换。</summary>
public sealed class FixedConditionTerm
{
    /// <summary>复制操作数集合，输入列表变化不能影响发布计划。</summary>
    internal FixedConditionTerm(string field,FixedConditionComparison comparison,IEnumerable<FixedConditionValue> values)
    {Field=field;Comparison=comparison;Values=Array.AsReadOnly(values.ToArray());}
    public string Field {get;}
    public FixedConditionComparison Comparison {get;}
    [JsonIgnore] public IReadOnlyList<FixedConditionValue> Values {get;}
    /// <summary>将已解析节点恢复为规范元数据定义；生成器和发布运行时共用，不作为可执行SQL。
    /// 字符串标点只保留为字面量，数据库执行仍须经类型计划和参数绑定。</summary>
    public string ToDefinition()
    {
        var operation=Comparison switch{FixedConditionComparison.Equal=>"=",FixedConditionComparison.NotEqual=>"<>",FixedConditionComparison.Greater=>">",FixedConditionComparison.GreaterOrEqual=>">=",FixedConditionComparison.Less=>"<",FixedConditionComparison.LessOrEqual=>"<=",FixedConditionComparison.Like=>" LIKE ",FixedConditionComparison.In=>" IN ",FixedConditionComparison.IsNull=>" IS NULL",FixedConditionComparison.IsNotNull=>" IS NOT NULL",_=>throw new ArgumentException("固定条件节点无法格式化。")};
        string Value(FixedConditionValue value)=>value.Kind switch{FixedConditionValueKind.String=>"'"+value.Text.Replace("'","''",StringComparison.Ordinal)+"'",FixedConditionValueKind.Number=>value.Text,FixedConditionValueKind.Boolean=>value.Text,FixedConditionValueKind.Null=>"NULL",FixedConditionValueKind.Variable=>"@"+value.Text,_=>throw new ArgumentException("固定条件节点无法格式化。")};
        return Field+operation+(Comparison is FixedConditionComparison.IsNull or FixedConditionComparison.IsNotNull?"":Comparison==FixedConditionComparison.In?"("+string.Join(",",Values.Select(Value))+")":Value(Values.Single()));
    }
}

/// <summary>原V8结构化候选条件的固定AND计划；在生成/发布时解析，普通请求不解释配置SQL。</summary>
public sealed class FixedConditionPlan
{
    /// <summary>冻结条件并提取所需字段和变量，便于宿主只读取窄上下文。</summary>
    private FixedConditionPlan(IEnumerable<FixedConditionTerm> terms)
    {
        Terms=Array.AsReadOnly(terms.ToArray());
        Variables=Array.AsReadOnly(Terms.SelectMany(term=>term.Values).Where(value=>value.Kind==FixedConditionValueKind.Variable).Select(value=>value.Text).Distinct(StringComparer.Ordinal).ToArray());
        Fields=Array.AsReadOnly(Terms.Select(term=>term.Field).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
    public IReadOnlyList<FixedConditionTerm> Terms {get;}
    public IReadOnlyList<string> Variables {get;}
    public IReadOnlyList<string> Fields {get;}

    /// <summary>仅接收已发布字段白名单；未知字段、任意表达式、语句和缺失值都在生成前拒绝。</summary>
    public static FixedConditionPlan Compile(string? definition,IEnumerable<string> allowedFields)
    {
        ArgumentNullException.ThrowIfNull(allowedFields);
        var fields=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var field in allowedFields){MetadataValidation.RequireIdentifier(field);if(!fields.TryAdd(field,field))throw new ArgumentException("固定条件字段白名单重复。");}
        var text=definition??"";if(text.Length>32768)throw new ArgumentException("固定条件定义过长。");
        return new(new Parser(text,fields).Parse());
    }

    /// <summary>按词法边界读取，字符串中的AND和逗号不参与拆分；括号只分组AND，不执行其它语法。</summary>
    private sealed class Parser(string text,IReadOnlyDictionary<string,string> fields)
    {
        private int position;
        private readonly List<FixedConditionTerm> terms=[];
        /// <summary>整个定义必须完整消费，禁止未知尾部被忽略。</summary>
        internal IReadOnlyList<FixedConditionTerm> Parse()
        {
            Space();if(position==text.Length)return terms.AsReadOnly();
            Group(0);Space();if(position!=text.Length)throw Invalid();return terms.AsReadOnly();
        }
        /// <summary>嵌套及项数有界，未知连接词不能被当作可忽略尾部。</summary>
        private void Group(int depth)
        {
            if(depth>16)throw Invalid();Atom(depth);
            while(Word("AND"))Atom(depth);
        }
        /// <summary>只接受单字段比较或AND分组，字段名统一绑定至已发布白名单。</summary>
        private void Atom(int depth)
        {
            Space();if(Take('(')){Group(depth+1);Require(')');return;}
            if(terms.Count>=128)throw Invalid();var name=Identifier();
            if(!fields.TryGetValue(name,out var field))throw new ArgumentException("固定条件引用了未发布字段。");
            FixedConditionComparison comparison;
            if(Word("IS")){var not=Word("NOT");if(!Word("NULL"))throw Invalid();comparison=not?FixedConditionComparison.IsNotNull:FixedConditionComparison.IsNull;terms.Add(new(field,comparison,[]));return;}
            if(Word("LIKE"))comparison=FixedConditionComparison.Like;
            else if(Word("IN"))comparison=FixedConditionComparison.In;
            else
            {
                Space();if(Take('='))comparison=FixedConditionComparison.Equal;
                else if(Take('!')){if(!Adjacent('='))throw Invalid();comparison=FixedConditionComparison.NotEqual;}
                else if(Take('<'))comparison=Adjacent('=')?FixedConditionComparison.LessOrEqual:Adjacent('>')?FixedConditionComparison.NotEqual:FixedConditionComparison.Less;
                else if(Take('>'))comparison=Adjacent('=')?FixedConditionComparison.GreaterOrEqual:FixedConditionComparison.Greater;
                else throw Invalid();
            }
            var values=new List<FixedConditionValue>();
            if(comparison==FixedConditionComparison.In)
            {
                Require('(');values.Add(Value());while(Take(',')){if(values.Count>=1024)throw Invalid();values.Add(Value());}Require(')');
            }
            else values.Add(Value());
            terms.Add(new(field,comparison,values));
        }
        /// <summary>SQL字符串只解释成对单引号；反斜杠保持字符，不依赖数据库会话转义模式。</summary>
        private FixedConditionValue Value()
        {
            Space();if(position==text.Length)throw Invalid();
            if(Take('\''))
            {
                var value=new StringBuilder();
                while(position<text.Length)
                {
                    var current=text[position++];if(current!='\''){value.Append(current);continue;}
                    if(position<text.Length&&text[position]=='\''){position++;value.Append('\'');continue;}
                    return new(FixedConditionValueKind.String,value.ToString());
                }
                throw Invalid();
            }
            if(Take('@'))
            {
                var start=position;PlainIdentifier();while(position<text.Length&&text[position]=='.'){position++;PlainIdentifier();}
                return new(FixedConditionValueKind.Variable,text[start..position]);
            }
            if(Word("NULL"))return new(FixedConditionValueKind.Null,"");
            if(Word("TRUE"))return new(FixedConditionValueKind.Boolean,"true");
            if(Word("FALSE"))return new(FixedConditionValueKind.Boolean,"false");
            var begin=position;if(position<text.Length&&text[position] is '+' or '-')position++;
            var digits=position;while(position<text.Length&&char.IsAsciiDigit(text[position]))position++;
            if(position==digits)throw Invalid();
            if(position<text.Length&&text[position]=='.'){position++;var fraction=position;while(position<text.Length&&char.IsAsciiDigit(text[position]))position++;if(position==fraction)throw Invalid();}
            if(position<text.Length&&text[position] is 'e' or 'E')
            {position++;if(position<text.Length&&text[position] is '+' or '-')position++;var exponent=position;while(position<text.Length&&char.IsAsciiDigit(text[position]))position++;if(position==exponent)throw Invalid();}
            return new(FixedConditionValueKind.Number,text[begin..position]);
        }
        /// <summary>单段反引号或普通标识符映射至字段白名单，不能接受表别名、函数或属性访问。</summary>
        private string Identifier()
        {
            Space();if(!Take('`')){var start=position;PlainIdentifier();return text[start..position];}
            var begin=position;PlainIdentifier();var result=text[begin..position];if(position==text.Length||text[position]!='`')throw Invalid();position++;return result;
        }
        /// <summary>变量路径每段与字段都使用ASCII标识符，不允许索引或代码。</summary>
        private void PlainIdentifier()
        {
            if(position==text.Length||!(char.IsAsciiLetter(text[position])||text[position]=='_'))throw Invalid();position++;
            while(position<text.Length&&(char.IsAsciiLetterOrDigit(text[position])||text[position]=='_'))position++;
        }
        /// <summary>关键字必须为完整词；不吞掉未知标识或把AND前缀当连接词。</summary>
        private bool Word(string word)
        {
            var before=position;Space();if(position==before&&position>0&&(char.IsAsciiLetterOrDigit(text[position-1])||text[position-1]=='_'))return false;
            if(position+word.Length>text.Length||!text.AsSpan(position,word.Length).Equals(word,StringComparison.OrdinalIgnoreCase))return false;
            var end=position+word.Length;if(end<text.Length&&(char.IsAsciiLetterOrDigit(text[end])||text[end]=='_'))return false;
            position=end;return true;
        }
        /// <summary>只跳过语法空白，不进入字符串内部。</summary>
        private void Space(){while(position<text.Length&&char.IsWhiteSpace(text[position]))position++;}
        /// <summary>读取允许前置空白的语法标点。</summary>
        private bool Take(char value){Space();if(position==text.Length||text[position]!=value)return false;position++;return true;}
        /// <summary>复合操作符必须相邻，不能把无效输入悄悄改成另一语法。</summary>
        private bool Adjacent(char value){if(position==text.Length||text[position]!=value)return false;position++;return true;}
        /// <summary>必需的括号或标点缺失时终止整个编译。</summary>
        private void Require(char value){if(!Take(value))throw Invalid();}
        /// <summary>错误只含位置，不回显配置中的字面量或未来绑定的账号值。</summary>
        private ArgumentException Invalid()=>new("固定条件在第"+(position+1).ToString(CultureInfo.InvariantCulture)+"字符无法解析。");
    }
}
