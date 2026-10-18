using Metarill.Runtime.Metadata;
using Xunit;

namespace Metarill.Metadata.Tests;

public sealed class BatchColumnParserTests
{
    /// <summary>二列文本保留物理行号并沿用命名类型默认值。</summary>
    [Fact]
    public void Text_preserves_line_numbers_and_naming_defaults()
    {
        var rows=BatchColumnParser.Parse(42,"名称 name\n\n启用 is_enabled\n日期 start_date\n数量 item_count");
        Assert.True(BatchColumnParser.Preview(rows).Valid);Assert.Equal(3,rows[1].RowNumber);Assert.Equal("nvarchar",rows[0].ColumnType);Assert.Equal("50",rows[0].ColumnLength);
        Assert.Equal("CheckBox",rows[1].ControlKind);Assert.Equal("int32",rows[1].LogicalType);Assert.Equal("date",rows[2].ControlKind);Assert.Equal("int",rows[3].ColumnType);
    }
    /// <summary>JSON保持显式类型和控件对象，未知ID、归属或审计字段不能注入。</summary>
    [Fact]
    public void Json_is_a_strict_configuration_whitelist()
    {
        var valid=BatchColumnParser.Parse(42,"[{\"name\":\"金额\",\"columnName\":\"amount\",\"columnType\":\"decimal\",\"columnLength\":\"18,2\"}]");Assert.True(BatchColumnParser.Preview(valid).Valid);Assert.Equal(18,valid[0].Precision);Assert.Equal(2,valid[0].Scale);
        foreach(var field in new[]{"id","configTableId","yzProjectId","insertTime","sql"})
        {var rows=BatchColumnParser.Parse(42,"[{\"name\":\"标题\",\"columnName\":\"title\",\"columnType\":\"nvarchar\",\""+field+"\":\"注入\"}]");Assert.Contains(rows[0].Errors,error=>error.Code=="UNSUPPORTED_FIELD");}
        var nullType=BatchColumnParser.Parse(42,"[{\"name\":\"标题\",\"columnName\":\"title\",\"columnType\":null}]");Assert.False(BatchColumnParser.Preview(nullType).Valid);
    }
    /// <summary>任意坏行与大小写重复都逐行展示，不会跳过后保存有效子集。</summary>
    [Fact]
    public void Invalid_rows_and_case_insensitive_duplicates_block_the_batch()
    {
        var rows=BatchColumnParser.Parse(42,"标题 title\n名称 TITLE\n无效三列 too many\n恶意 bad;drop");
        Assert.Equal(4,rows.Count);Assert.False(BatchColumnParser.Preview(rows).Valid);Assert.Contains(rows[0].Errors,error=>error.Code=="DUPLICATE_IN_BATCH");Assert.Contains(rows[1].Errors,error=>error.Code=="DUPLICATE_IN_BATCH");Assert.Contains(rows[3].Errors,error=>error.Code=="INVALID_IDENTIFIER");
    }
    /// <summary>限制与原服务一致，且不接受未知或当前无法生成的数据类型。</summary>
    [Fact]
    public void Size_count_and_type_validation_are_explicit()
    {
        Assert.False(BatchColumnParser.Preview(BatchColumnParser.Parse(42,new string('x',1024*1024+1))).Valid);
        Assert.False(BatchColumnParser.Preview(BatchColumnParser.Parse(42,string.Join('\n',Enumerable.Range(0,501).Select(index=>"字段 field_"+index)))).Valid);
        var rows=BatchColumnParser.Parse(42,"[{\"name\":\"位置\",\"columnName\":\"location\",\"columnType\":\"point\"}]");Assert.Contains(rows[0].Errors,error=>error.Code=="INVALID_TYPE");
    }
}
