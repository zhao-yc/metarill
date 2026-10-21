using Metarill.Runtime.Files;
using Xunit;

namespace Metarill.Files.Tests;

[Collection(FilesIntegrationCollection.Name)]
public sealed class LegacyPublicImportTests
{
    /// <summary>只有服务端兼容入口公开导入；普通无控件上传计划仍为受保护，失败导入不残留计划。</summary>
    [FilesIntegrationFact]
    public async Task Legacy_public_import_is_atomic_and_does_not_change_ordinary_plan_policy()
    {
        await using var fixture = await FilesFixture.CreateAsync(); var service = fixture.Service();
        var bytes = "%PDF-1.4\n测试\n%%EOF"u8.ToArray();
        var imported = await service.ImportLegacyPublicAsync("legacy.pdf", bytes.Length, new MemoryStream(bytes)); fixture.FileIds.Add(imported.FileId);
        Assert.Equal("PUBLIC", imported.AccessMode); Assert.Equal("ready", imported.Status);
        await using (var content = (await service.OpenContentAsync(imported.FileId, publicOnly: true)).Stream) { using var result = new MemoryStream(); await content.CopyToAsync(result); Assert.Equal(bytes, result.ToArray()); }
        var ordinary = await service.PlanUploadAsync(new("ordinary.pdf", null, 1)); fixture.FileIds.Add(ordinary.FileId);
        Assert.Equal("PROTECTED", Assert.Single(await fixture.Store.ReadAsync("yz_file_library", new Dictionary<string, object?> { ["id"] = ordinary.FileId }))["access_mode"]);
        var failedName = "failed-" + Guid.NewGuid().ToString("N") + ".pdf";
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportLegacyPublicAsync(failedName, 2, new MemoryStream(new byte[3])));
        Assert.Empty(await fixture.Store.ReadAsync("yz_file_library", new Dictionary<string, object?> { ["name"] = failedName }));
        Assert.Empty(Directory.GetFiles(fixture.Options.RootDirectory, "*.part.*"));
    }
}
