using System.Reflection;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>TEXT默认值只允许原安全字符串常量；服务器表达式标记不能误判成生成列或接受原SQL。</summary>
public sealed class MySqlTextDefaultTests
{
    /// <summary>长文本常量带括号，普通VARCHAR合同不变，SQL形态输入也只成为字面值。</summary>
    [Theory][InlineData("text")][InlineData("max")]
    public void Text_constant_is_expression_wrapped_without_executing_input(string length)
    {
        var column=new ColumnMetadata{Id="1",ConfigTableId="1",ColumnName="values_text",LogicalType="string",ColumnLength=length,DefaultValue="['甲'); DROP TABLE users;--']"};
        Assert.Equal("('[甲]')",Normalize("_utf8mb4'[甲]'"));
        var result=MySqlSchemaPlanner.CompileColumn(column);Assert.Equal("('[''甲''); DROP TABLE users;--'']')",result.DefaultSql);
        column.ColumnLength="255";Assert.Equal("'[''甲''); DROP TABLE users;--'']'",MySqlSchemaPlanner.CompileColumn(column).DefaultSql);
    }
    /// <summary>只归一MySQL实际返回的固定字符串字面量、字符集和单层括号。</summary>
    [Theory][InlineData("'[\"9007199254740993\"]'","('[\"9007199254740993\"]')")][InlineData("_utf8mb4'中文'","('中文')")][InlineData("(_utf8mb4'a''b')","('a''b')")][InlineData("'a\\'b'","('a''b')")]
    [InlineData("_utf8mb4\\'[\"9007199254750003\"]\\'","('[\"9007199254750003\"]')")]
    public void Inspection_accepts_only_known_literal_shapes(string raw,string expected)=>Assert.Equal(expected,Normalize(raw));
    /// <summary>函数、运算、未知字符集和转义仍受原生成表达式保护。</summary>
    [Theory][InlineData("(uuid())")][InlineData("('a'+'b')")][InlineData("_latin1'a'")][InlineData("'a' OR 1=1")][InlineData("'\\x'")]
    public void Inspection_rejects_unproved_expressions(string raw)=>Assert.Null(Normalize(raw));
    /// <summary>反射仅验证同程序集内部反查函数，不为测试暴露生产API。</summary>
    private static string? Normalize(string raw)
    {
        var method=typeof(MySqlSchemaPlanner).Assembly.GetType("Metarill.Data.Schema.MySqlTextDefault",true)!.GetMethod("TryNormalize",BindingFlags.NonPublic|BindingFlags.Static)!;var arguments=new object?[]{raw,null};return (bool)method.Invoke(null,arguments)!?(string?)arguments[1]:null;
    }
}
