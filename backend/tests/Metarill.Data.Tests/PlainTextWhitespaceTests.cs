using FreeSql.DataAnnotations;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>以原应用commons-lang3实际枚举得到的UTF-16空白集合验证普通文本入口。</summary>
public sealed class PlainTextWhitespaceTests
{
    /// <summary>完整枚举65536字符，集合来自原Java依赖的独立运行，不按C#类别近似推断。</summary>
    [Fact]public void Original_java_blank_snapshot_matches_entire_utf16_domain()
    {
        const string original="0009 000A 000B 000C 000D 001C 001D 001E 001F 0020 1680 2000 2001 2002 2003 2004 2005 2006 2008 2009 200A 2028 2029 205F 3000";
        var expected=original.Split(' ').Select(value=>(char)Convert.ToInt32(value,16)).ToHashSet();
        for(var code=0;code<=char.MaxValue;code++)Assert.Equal(expected.Contains((char)code),StringNameQuery.IsPatternBlank(((char)code).ToString()));
        Assert.True(StringNameQuery.IsPatternBlank(null));Assert.True(StringNameQuery.IsPatternBlank(""));Assert.False(StringNameQuery.IsPatternBlank("\u001c文字\u00a0"));
    }
    /// <summary>四个.NET额外空白仍应进入原LIKE，不能在ORM边界再次被拒绝；字面名称规则保持。</summary>
    [Theory][InlineData("\u0085")][InlineData("\u00a0")][InlineData("\u2007")][InlineData("\u202f")]
    public void Pattern_accepts_non_java_blank_without_relaxing_literal_names(string keyword)
    {
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
        var query=StringNameQuery.ApplyPattern(session,session.Select<Row>(),row=>row.Name,keyword);
        Assert.Contains(" LIKE @mt_text_pattern_",query.ToSql());
        Assert.Throws<ArgumentException>(()=>StringNameQuery.ApplyLike(session,session.Select<Row>(),row=>row.Name,keyword));
    }
    /// <summary>原Java独有的四种分隔符应由生成入口跳过，直接模式调用也保持拒绝。</summary>
    [Theory][InlineData("\u001c")][InlineData("\u001d")][InlineData("\u001e")][InlineData("\u001f")]
    public void Pattern_rejects_original_java_control_blanks(string keyword)
    {
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
        Assert.Throws<ArgumentException>(()=>StringNameQuery.ApplyPattern(session,session.Select<Row>(),row=>row.Name,keyword));
    }
    private sealed class Row
    {
        [Column(IsPrimary=true)]public long Id{get;set;}
        public string? Name{get;set;}
    }
}