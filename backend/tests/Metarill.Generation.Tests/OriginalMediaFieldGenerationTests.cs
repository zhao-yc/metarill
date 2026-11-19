using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Runtime;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>从已发布元数据验证原媒体控件、保存合同与两种生成读取模式。</summary>
public sealed class OriginalMediaFieldGenerationTests
{
    private const string Namespace = "Example.GuardContract";
    private const string Descriptor = """{"url":"/api/admin/sys/file-library/public-content/101","name":"样本.png","extname":"png"}""";
    private const string Descriptors = "[" + Descriptor + "]";

    /// <summary>完整包沿用原控件和上传入口，单图异步回写保持原数组协议。</summary>
    [Fact]
    public async Task Original_media_components_run_from_a_full_package()
    {
        var catalog = Catalog();
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-original-upload-full");
        var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(new ApplicationPackager().Create(catalog, output, Namespace, NonRedundantApplicationFixture.Root()).FrontendIncluded);
            Assert.Contains("/admin/sys/upload/uploadImage",
                await File.ReadAllTextAsync(Path.Combine(output, "frontend/common/yzp_runtime/yzp-runtime.js")));
            await RunVue(output);
            foreach (var command in new[] { "restore", "build" }) await NonRedundantApplicationFixture.BuildAsync(output, command);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    /// <summary>按原数组提交，保存与数据库JSON文本交换，已存空值及坏文本按原null规则投影。</summary>
    [Fact]
    public async Task Generated_service_preserves_media_array_writes_and_existing_reads()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-original-upload-write");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(Catalog(), Path.Combine(directory, "generated"), Namespace);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var run = new GeneratedMutationGuardRun(assembly);
            var service = run.Service([]);
            var request = run.Request(RecordMutationOperation.Create,
                "{\"name\":\"旧附件\",\"photo\":" + Descriptors + ",\"gallery\":" + Descriptors +
                ",\"document\":" + Descriptors + ",\"video\":" + Descriptors + "}");
            var response = await ClockTimeGenerationTests.Invoke(service, "Create101Async", request, null, run.Actor, CancellationToken.None);
            Assert.Contains("write:Insert", run.Trace);
            foreach (var name in new[] { "Photo", "Gallery", "Document", "Video" })
                Assert.Equal("样本.png", Value(response!, name)!.Value[0].GetProperty("name").GetString());

            // 模拟旧记录数据库列中的原JSON文本。省略时照原保存，显式null及空数组分别清除。
            run.SeedRow = row =>
            {
                SetProperty(row, "Photo", Descriptors);
                SetProperty(row, "Gallery", "{\"url\":\"旧对象\"}");
                SetProperty(row, "Document", "无法解析的旧内容");
                SetProperty(row, "Video", Descriptors);
                SetProperty(row, "LegacyGallery", Descriptors);
            };
            var updated = await ClockTimeGenerationTests.Invoke(service, "Update101Async",
                run.Request(RecordMutationOperation.Update, """{"id":"1","photo":null,"video":[]}"""),
                null, run.Actor, CancellationToken.None);
            Assert.Null(Value(updated!, "Photo"));
            Assert.Null(Value(updated!, "Gallery"));
            Assert.Null(Value(updated!, "Document"));
            Assert.Empty(Value(updated!, "Video")!.Value.EnumerateArray());
            Assert.Equal("样本.png", Value(updated!, "LegacyGallery")!.Value[0].GetProperty("name").GetString());
            await Assert.ThrowsAsync<ArgumentException>(() =>
                ClockTimeGenerationTests.Invoke(service, "Update101Async",
                    run.Request(RecordMutationOperation.Update, """{"id":"1","photo":{"url":"旧对象"}}"""),
                    null, run.Actor, CancellationToken.None));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>实际非冗余VO仍从物理字符串列读入数组，错误值不会中断其它字段回显。</summary>
    [Fact]
    public async Task Non_redundant_media_projection_keeps_the_legacy_response_shape()
    {
        var catalog = Catalog(nonRedundant: true);
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-original-upload-read");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), Namespace);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var vo = assembly.GetType(Namespace + ".Generated.VOs.CustomerPage101Vo", true)!;
            var model = GenerationModel.Create(catalog, Namespace);
            var table = model.Tables.Single(item => item.Source.Id == "1");
            var plan = NonRedundantReadCompilation.ForPage(model, table, table.Pages.Single());
            var row = plan.Outputs.ToDictionary(output => output.Storage, _ => (object?)null);
            row["id"] = 17L;
            row["name"] = "原业务";
            row["photo"] = Descriptors;
            row["gallery"] = "损坏";
            row["document"] = "[]";
            var value = vo.GetMethod("FromReadRow")!.Invoke(null, [row])!;
            Assert.Equal("样本.png", Value(value, "Photo")!.Value[0].GetProperty("name").GetString());
            Assert.Null(Value(value, "Gallery"));
            Assert.Empty(Value(value, "Document")!.Value.EnumerateArray());
            Assert.Null(Value(value, "Video"));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    private static JsonElement? Value(object row, string field) => (JsonElement?)row.GetType().GetProperty(field)!.GetValue(row);
    private static void SetProperty(object row, string field, string value) => row.GetType().GetProperty(field)!.SetValue(row, value);

    /// <summary>加入已开放的旧上传字段。额外的普通联接只用于触发实际非冗余读取。</summary>
    private static MetadataCatalog Catalog(bool nonRedundant = false)
    {
        var catalog = GeneratorTests.Catalog();
        foreach (var column in catalog.ConfigColumns.Where(column => column.LogicalType == "id")) column.LogicalType = "int64";
        var source = catalog.ConfigTables.Single();
        source.IsRongyu = nonRedundant ? 0 : 1;
        var page = catalog.ConfigPages.Single();
        page.IsNew = page.IsExport = 1;
        page.Extra["isUpdate"] = JsonSerializer.SerializeToElement(1);
        page.Extra["exportMethod"] = JsonSerializer.SerializeToElement("list");
        Add("51", "photo", "Image", """{"cropImages":true}""");
        Add("52", "gallery", "MultiImage", """{"limit":3,"cropImages":true}""");
        Add("53", "document", "Upload", "4");
        Add("54", "video", "Video", "2");
        Add("55", "inline_preview", "Base64Image", "");
        Add("57", "legacy_gallery", "MultiImage", "6");
        if (nonRedundant)
        {
            catalog.ConfigTables.Add(new() { Id = "2", TableName = "customer_group", IsRongyu = 1 });
            catalog.ConfigColumns.AddRange([
                new() { Id = "21", ConfigTableId = "2", ColumnName = "id", LogicalType = "int64", IsDbNotNull = 1 },
                new() { Id = "22", ConfigTableId = "2", ColumnName = "name" },
                new() { Id = "23", ConfigTableId = "2", ColumnName = "yz_project_id", LogicalType = "int64" }
            ]);
            catalog.ConfigPages.Add(new() { Id = "201", ConfigTableId = "2", WhereSql = "yz_project_id=@current_yz_project_id" });
            catalog.ConfigPageColumns.Add(new() { Id = "2022", ConfigPageId = "201", ConfigColumnId = "22", IsShow = 1 });
            Add("56", "group_id", "List", """{"tableId":"2","nameField":"name"}""", "int64");
            catalog.ConfigColumns.Single(column => column.Id == "56").LinkConfigTableId = "2";
        }
        return RedundantMultiNameCacheTests.Published(catalog);

        void Add(string id, string name, string kind, string detail, string logicalType = "string")
        {
            catalog.ConfigColumns.Add(new() { Id = id, ConfigTableId = "1", ColumnName = name, Name = name,
                LogicalType = logicalType, ColumnLength = logicalType == "string" ? "max" : "", ControlKind = kind, ControlDetail = detail });
            catalog.ConfigPageColumns.Add(new() { Id = "10" + id, ConfigPageId = page.Id, ConfigColumnId = id, IsShow = 1, IsEdit = 1 });
        }
    }

    /// <summary>使用生成包内真实Vue与上传运行时入口，隔离网络，只替换上传回执。</summary>
    private static async Task RunVue(string output, [CallerFilePath] string file = "")
    {
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(file)!, "OriginalMediaFieldProbe.mjs"));
        start.ArgumentList.Add(output);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }
}
