using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Xunit;
namespace Metarill.Metadata.Tests;

/// <summary>实际仓储加载继续输出字符串配置ID；物理long不改变Core元数据合同。</summary>
public sealed class ConfigurationIdRowDecodingTests
{
    /// <summary>字段、页字段和子页三种真实解码类型均保留大整数精度。</summary>
    [Theory][InlineData(9007199254740993L)][InlineData(long.MaxValue)]
    public void Database_integer_references_decode_to_the_original_string_metadata(long id)
    {
        var column=MetadataRepository.DecodeRow<ColumnMetadata>(new(){["id"]=id,["link_config_table_id"]=id,["link_config_page_id"]=id});
        var field=MetadataRepository.DecodeRow<PageColumnMetadata>(new(){["id"]=id,["link_config_table_id"]=id,["link_config_page_id"]=id,["combine_super_column_id"]=id});
        var child=MetadataRepository.DecodeRow<JsonElement>(new(){["id"]=id,["child_config_page_id"]=id});
        foreach(var(record,keys)in new (object Record,string[] Keys)[]{(column,["linkConfigTableId","linkConfigPageId"]),(field,["linkConfigTableId","linkConfigPageId","combineSuperColumnId"]),(child,["childConfigPageId"])})
        {
            var json=JsonSerializer.SerializeToElement(record,JsonDefaults.Options);
            foreach(var key in keys)Assert.Equal(id.ToString(),json.GetProperty(key).GetString());
            Assert.Equal(id.ToString(),json.GetProperty("id").GetString());
        }
    }
}
