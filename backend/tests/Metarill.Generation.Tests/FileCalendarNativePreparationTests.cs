using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>离线冻结原模型并拒绝漂移；正式数据库尚未连接，不冒充真实上传通过。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class FileCalendarNativePreparationTests
{
    /// <summary>真实完整包严格编译、驱动依赖和源目录验证，正常运行只放行固定结构审计日志。</summary>
    [Fact]
    public async Task Original_file_calendar_package_is_frozen_before_database_lease()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-file-calendar-native");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var package=Path.Combine(directory,"prepared");
        try
        {
            FileCalendarNativePreparation.Create(source,package);var start=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",package);
            foreach(var argument in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--verbosity","quiet","--disable-build-servers"})start.ArgumentList.Add(argument);await NonRedundantApplicationFixture.RunAsync(start);
            var proof=FileCalendarNativePreparation.Capture(package);var catalog=FileCalendarNativePreparation.Verify(proof);Assert.Equal(3,catalog.ConfigTables.Count);Assert.Equal(46,catalog.ConfigColumns.Count);Assert.Equal(6,catalog.ConfigIndexes.Count);Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,FileCalendarNativePreparation.RootNamespace)));
            foreach(var relative in new[]{"src/Metarill.Core/Contracts/FileUploadLifetime.cs","backend/metadata/files.json","backend/Application.Host/bin/Release/net10.0/Application.Host.dll","backend/Application.Host/bin/Release/net10.0/Application.Host.deps.json"})
            {var file=CSharpGenerator.SafePath(package,relative);var bytes=await File.ReadAllBytesAsync(file);await File.AppendAllTextAsync(file,"漂移");Assert.Throws<InvalidOperationException>(()=>FileCalendarNativePreparation.Verify(proof));await File.WriteAllBytesAsync(file,bytes);}
            var copied=FileCalendarNativePreparation.CopyForExecution(proof,Path.Combine(directory,"execution/application"));var journal=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host/.local/schema-history.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(journal)!);await File.WriteAllTextAsync(journal,"{}\n");Assert.Throws<InvalidOperationException>(()=>FileCalendarNativePreparation.Verify(copied));FileCalendarNativePreparation.Verify(copied,true);
            var unknown=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host/.local/unknown.json");await File.WriteAllTextAsync(unknown,"{}");Assert.Throws<InvalidOperationException>(()=>FileCalendarNativePreparation.Verify(copied,true));File.Delete(unknown);Assert.Throws<InvalidOperationException>(()=>FileCalendarNativePreparation.CopyForExecution(proof,copied.PackageRoot));FileCalendarNativePreparation.Verify(proof);
            var exported=Environment.GetEnvironmentVariable("METARILL_FILE_CALENDAR_PREPARE_OUTPUT");if(!string.IsNullOrWhiteSpace(exported))
            {Assert.Equal(CSharpGenerator.SafePath(source,".local/file-calendar-native/prepared"),Path.GetFullPath(exported));var result=FileCalendarNativePreparation.CopyForExecution(proof,exported);await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(exported)!,"prepared-receipt.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));}
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>无数据库/进程的合成租约测试，错误用途/状态/产品/路径均在任何副作用前拒绝。</summary>
    [Theory][InlineData("purpose")][InlineData("authorization")][InlineData("ended")][InlineData("product")][InlineData("run")][InlineData("path")][InlineData("relative")][InlineData("proof")]
    public void Native_lease_rejects_unapproved_or_foreign_input_without_side_effects(string invalid)
    {
        var root=Path.Combine(Path.GetTempPath(),"metarill-file-calendar-gate",Guid.NewGuid().ToString("N"));var model=JsonSerializer.SerializeToNode(new{Purpose=FileCalendarNativeLease.Purpose,AuthorizationStatus="approved",LeaseStatus="running",ProductCommit=FileCalendarNativePreparation.ProductCommit,RunId="0123456789abcdef",FileCalendarPreparation=new{PackageRoot=root}})!;
        Assert.Equal("0123456789abcdef",FileCalendarNativeLease.Validate(JsonSerializer.SerializeToElement(model),root));
        if(invalid=="purpose")model["Purpose"]="other";if(invalid=="authorization")model["AuthorizationStatus"]="pending";if(invalid=="ended")model["LeaseStatus"]="ended";if(invalid=="product")model["ProductCommit"]=new string('0',40);if(invalid=="run")model["RunId"]="../old";if(invalid=="path")model["FileCalendarPreparation"]!["PackageRoot"]=Path.Combine(root,"foreign");if(invalid=="relative")model["FileCalendarPreparation"]!["PackageRoot"]="prepared";if(invalid=="proof")model.AsObject().Remove("FileCalendarPreparation");
        Assert.Throws<InvalidOperationException>(()=>FileCalendarNativeLease.Validate(JsonSerializer.SerializeToElement(model),root));Assert.False(Directory.Exists(root));
    }
}
