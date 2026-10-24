using System.Text.Json;
using Metarill.Runtime.Files;
using Xunit;

namespace Metarill.Files.Tests;

public sealed class FileValidationTests
{
    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("..\\outside.txt")]
    [InlineData("C:outside.txt")]
    [InlineData("x\r\nContent-Type: text/html")]
    [InlineData(" name.txt")]
    public void File_names_cannot_supply_paths_or_headers(string name) => Assert.Throws<InvalidOperationException>(() => FileValidation.RequireName(name));

    [Theory]
    [InlineData("../../secret")]
    [InlineData("https://example.com/file")]
    [InlineData("C:\\secret")]
    [InlineData("00000000000000000000000000000000.blob/../x")]
    public void Storage_paths_only_accept_server_generated_key(string key) => Assert.Throws<InvalidOperationException>(() => FileValidation.StoragePath(Path.GetTempPath(), key));

    [Fact]
    public void Files_parse_id_arrays_and_legacy_objects_but_never_urls()
    {
        Assert.Equal([9007199254740993L, 2L], FileValidation.ParseFileIds("[\"9007199254740993\",{\"fileId\":\"2\"},{\"id\":\"2\"}]"));
        Assert.Empty(FileValidation.ParseFileIds(null));
        Assert.Empty(FileValidation.ParseFileIds(JsonSerializer.Deserialize<JsonElement>("null")));
        Assert.Throws<InvalidOperationException>(() => FileValidation.ParseFileIds("[\"https://example.com/a\"]"));
        Assert.Throws<InvalidOperationException>(() => FileValidation.ParseFileIds("[{\"url\":\"/file/1\"}]"));
        Assert.Throws<InvalidOperationException>(() => FileValidation.ParseFileIds("{\"fileId\":\"1\"}"));
    }

    [Fact]
    public void Digest_and_mime_are_server_validated()
    {
        Assert.Throws<InvalidOperationException>(() => FileValidation.Digest("http://example.com/hash"));
        Assert.Equal(new string('a', 64), FileValidation.Digest(new string('A', 64)));
        Assert.Equal("text/plain", FileValidation.ContentType("upload.txt"));
        Assert.Equal("application/octet-stream", FileValidation.ContentType("upload.html"));
    }
}
