using System.Text.Json;
using Metarill.Runtime.Metadata;
using Xunit;

namespace Metarill.Metadata.Tests;

public sealed class IndexWorkbenchRulesTests
{
    private const string Revision = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static string Request(string index) => "{\"configTableId\":\"100\",\"revision\":\"" + Revision + "\",\"indexes\":[" + index + "]}";
    private const string Valid = "{\"id\":\"\",\"indexName\":\"yzp_idx_title\",\"indexType\":\"NORMAL\",\"isEnabled\":1,\"columns\":[{\"id\":\"\",\"configColumnId\":\"2\",\"columnName\":\"title\",\"sortNo\":10,\"sortDirection\":\"ASC\"}]}";
    [Theory]
    [InlineData("yzp_idx_title", true)] [InlineData("yzp_uk_title", true)] [InlineData("ux_infrastructure", false)] [InlineData("yzp_idx_", false)] [InlineData("yzp_idx_a;DROP", false)]
    public void OnlyManagedNamesAreEditable(string name, bool managed) => Assert.Equal(managed, IndexWorkbenchRules.Managed(name));
    [Theory]
    [InlineData("\"sortNo\":10", "\"sortNo\":0")]
    [InlineData("\"sortDirection\":\"ASC\"", "\"sortDirection\":\"DESC;DROP\"")]
    [InlineData("\"indexName\":\"yzp_idx_title\"", "\"indexName\":\"ux_title\"")]
    [InlineData("\"indexType\":\"NORMAL\"", "\"indexType\":\"FULLTEXT\"")]
    [InlineData("\"isEnabled\":1", "\"isEnabled\":2")]
    [InlineData("\"columnName\":\"title\"", "\"columnName\":\"title`\"")]
    [InlineData("\"configColumnId\":\"2\"", "\"configColumnId\":\"1e3\"")]
    public void InvalidBatchValuesAreRejectedBeforeWrites(string source, string replacement)
    { using var json = JsonDocument.Parse(Request(Valid.Replace(source, replacement))); Assert.Throws<InvalidOperationException>(() => IndexWorkbenchRules.Parse(json.RootElement, null)); }
    [Fact]
    public void RevisionAndExplicitCompleteCollectionAreRequired()
    {
        using var missing = JsonDocument.Parse("{\"configTableId\":\"100\",\"revision\":\"" + Revision + "\"}"); Assert.Throws<InvalidOperationException>(() => IndexWorkbenchRules.Parse(missing.RootElement, null));
        using var json = JsonDocument.Parse(Request(Valid)); Assert.Throws<InvalidOperationException>(() => IndexWorkbenchRules.Parse(json.RootElement, new string('b', 64)));
        using var duplicate = JsonDocument.Parse(Request(Valid).Replace("\"isEnabled\":1", "\"isEnabled\":1,\"isEnabled\":0")); Assert.Throws<InvalidOperationException>(() => IndexWorkbenchRules.Parse(duplicate.RootElement, null));
        using var empty = JsonDocument.Parse(Request("")); Assert.Empty(IndexWorkbenchRules.Parse(empty.RootElement, null).Indexes);
    }
}
