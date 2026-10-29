using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Metarill.Data.Schema;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>完整原V8样板沿正式打包/更新/编译保留时间语义；不替代MySQL写入或性能验收。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class TemporalDefinitionGenerationTests
{
    /// <summary>只改变显式物理精度和审计默认；原页面、组件、业务partial及所有业务能力仍完整保留。</summary>
    [Fact]
    public async Task Full_sample_keeps_original_pages_and_builds_explicit_audit_defaults()
    {
        var catalog=GlobalCachedSourceGenerationTests.Catalog();var boundary=Path.Combine(Path.GetTempPath(),"metarill-temporal-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        var output=Path.Combine(directory,"application");var candidate=Path.Combine(directory,"candidate");var root=NonRedundantApplicationFixture.Root();
        try
        {
            new ApplicationPackager().Create(catalog,output,"Example.GlobalSource",root);
            RestorePreviousCalendarTarget(output);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            var hand=Path.Combine(output,"backend/generated/Services/SampleContractService.cs");await File.WriteAllTextAsync(hand,GlobalCachedSourceGenerationTests.HandwrittenRule());var handwritten=await File.ReadAllBytesAsync(hand);
            var pages=Directory.GetFiles(Path.Combine(output,"frontend/pages"),"*.vue",SearchOption.AllDirectories).ToDictionary(path=>Path.GetRelativePath(output,path),File.ReadAllBytes);
            foreach(var column in catalog.ConfigColumns.Where(column=>column.LogicalType=="datetime"))
            {
                column.ColumnType="datetime(0)";
                if(column.ColumnName is "insert_time" or "update_time")column.DefaultValue="now()";
            }
            var expected=JsonSerializer.Serialize(catalog);var schema=MySqlSchemaPlanner.Compile(catalog,"temporal_sample");Assert.Equal(3,schema.Tables.Count);Assert.Equal(46,schema.Tables.Sum(table=>table.Columns.Count));Assert.Equal(6,schema.Tables.Sum(table=>table.Indexes!.Count));
            Assert.Equal(9,schema.Tables.SelectMany(table=>table.Columns).Count(column=>column.StoreType=="datetime"));Assert.Equal(6,schema.Tables.SelectMany(table=>table.Columns).Count(column=>column.DefaultSql=="CURRENT_TIMESTAMP"));
            new ApplicationPackager().Create(catalog,candidate,"Example.GlobalSource",root);
            foreach(var page in pages)Assert.Equal(page.Value,await File.ReadAllBytesAsync(Path.Combine(candidate,page.Key)));
            var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);Assert.Contains(plan.Files,file=>file.Path=="src/Metarill.Core/Contracts/BusinessCalendar.cs"&&file.Action=="create");updater.Apply(plan,plan.PlanSha256);Assert.Equal(handwritten,await File.ReadAllBytesAsync(hand));
            var second=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.Equal("unchanged",updater.Apply(second,second.PlanSha256).Status);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            var service=await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/Services/SampleContractService.g.cs"));Assert.Contains("entity.InsertTime = BusinessCalendar.Now",service);Assert.DoesNotContain("now()",service,StringComparison.OrdinalIgnoreCase);
            Assert.Equal(expected,JsonSerializer.Serialize(catalog));
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>显式模拟旧日历版本：移除新合同、还原UTC生成表达式并重算归属；不冒充历史冻结包。</summary>
    private static void RestorePreviousCalendarTarget(string output)
    {
        const string clock="src/Metarill.Core/Contracts/BusinessCalendar.cs";
        foreach(var file in Directory.GetFiles(Path.Combine(output,"backend/generated/Services"),"*.g.cs"))
            File.WriteAllText(file,File.ReadAllText(file).Replace("BusinessCalendar.Now","DateTime.UtcNow",StringComparison.Ordinal));
        File.Delete(Path.Combine(output,clock));
        var generated=Path.Combine(output,"backend/generated/.metarill-generation.json");var generatedNode=JsonNode.Parse(File.ReadAllText(generated))!;
        foreach(var entry in generatedNode["files"]!.AsArray())entry!["sha256"]=Hash(Path.Combine(output,"backend/generated",entry["path"]!.GetValue<string>()));
        File.WriteAllText(generated,generatedNode.ToJsonString(new(){WriteIndented=true}));
        var marker=Path.Combine(output,".metarill-package.json");var package=JsonNode.Parse(File.ReadAllText(marker))!;var files=package["files"]!.AsArray();files.Remove(files.Single(entry=>entry!["path"]!.GetValue<string>()==clock));
        foreach(var entry in files)entry!["sha256"]=Hash(Path.Combine(output,entry["path"]!.GetValue<string>()));
        File.WriteAllText(marker,package.ToJsonString(new(){WriteIndented=true}));
    }
    /// <summary>使用实际文件字节同步双层归属，不删除保护信息来绕过更新器。</summary>
    private static string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();

}
