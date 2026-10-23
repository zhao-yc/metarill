using System.Text;
namespace Metarill.Data.Schema;

/// <summary>仅归一MySQL反查返回的TEXT字符串常量，绝不执行或接受函数默认表达式。</summary>
internal static class MySqlTextDefault
{
    /// <summary>服务端可添加字符集及一层表达式括号；其余SQL形状保持不支持。</summary>
    internal static bool TryNormalize(string? raw,out string? sql)
    {
        sql=null;if(raw is null)return false;var text=raw.Trim();if(text.StartsWith('(')&&text.EndsWith(')'))text=text[1..^1].Trim();
        if(text.StartsWith("_utf8mb4",StringComparison.OrdinalIgnoreCase))text=text[8..];
        // MySQL 8.4的COLUMN_DEFAULT会用反斜杠标记表达式字面量的边界；只消除此一对固定边界。
        if(text.Length>=4&&text[0]==(char)92&&text[1]==(char)39&&text[^2]==(char)92&&text[^1]==(char)39)text=text[1..^2]+"'";
        if(text.Length<2||text[0]!=(char)39||text[^1]!=(char)39)return false;
        var result=new StringBuilder();
        for(var index=1;index<text.Length-1;index++)
        {
            var value=text[index];
            if(value==(char)39){if(index+1>=text.Length-1||text[index+1]!=(char)39)return false;index++;}
            else if(value==(char)92)
            {
                if(++index>=text.Length-1)return false;
                value=text[index] switch{(char)39=>(char)39,'"'=>'"','n'=>'\n','r'=>'\r','t'=>'\t','b'=>'\b','0'=>'\0','Z'=>(char)26,_=>(char)92};
                if(value==(char)92)return false;
            }
            result.Append(value);
        }
        sql="('"+result.ToString().Replace("'","''")+"')";return true;
    }
}
