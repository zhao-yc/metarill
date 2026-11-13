using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>新工程首次恢复原壳，已有工程sync-runtime必须保护手写App/home/project/config。</summary>
public sealed class LocalGenerationUpdateShellProtectionTests
{
    [Fact]
    public async Task Runtime_update_preserves_existing_original_shell_and_project_extensions()
    {
        var root=Path.Combine(Path.GetTempPath(),"metarill-shell-protection",Guid.NewGuid().ToString("N"));
        try
        {
            var source=SourceRoot();var metadata=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(source,"backend/metadata/demo.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var target=Path.Combine(root,"target");var candidate=Path.Combine(root,"candidate");new ApplicationPackager().Create(metadata,target,"Example.ProtectedShell",source);new ApplicationPackager().Create(metadata,candidate,"Example.ProtectedShell",source);
            var protectedPaths=new[]{"App.vue","main.js","pages/main/index.vue","pages/main/main.vue","pages/main/top.vue","pages/main/left.vue","pages/main/menu.vue","pages/main/my.vue","pages/main/change_password.vue","common/project.js","common/application.config.js"};
            foreach(var path in protectedPaths){var file=Path.Combine(target,"frontend",path);var content=File.ReadAllText(file);File.WriteAllText(file,path.EndsWith(".vue",StringComparison.Ordinal)?content.Replace("</script>","// 用户原工程扩展保持\n</script>",StringComparison.Ordinal):content+"\n// 用户原工程扩展保持\n");}var before=protectedPaths.ToDictionary(path=>path,path=>File.ReadAllBytes(Path.Combine(target,"frontend",path)));
            var updater=new LocalGenerationUpdater();var plan=updater.Preview(target,candidate,["sync-runtime"]);Assert.True(plan.CanApply);Assert.All(protectedPaths,path=>Assert.Contains(plan.Files,file=>file.Path=="frontend/"+path&&file.Action=="preserve"));
            updater.Apply(plan,plan.PlanSha256);foreach(var path in protectedPaths)Assert.Equal(before[path],File.ReadAllBytes(Path.Combine(target,"frontend",path)));
            Assert.DoesNotContain(plan.Files,file=>file.Path.EndsWith("credentials.json",StringComparison.Ordinal));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,Path.Combine(Path.GetTempPath(),"metarill-shell-protection"));}
    }
    private static string SourceRoot([CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
}
