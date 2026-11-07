using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime.Files;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>共享父页面的原文件字段与完整源码包共用正式文件运行时，不手改生成代码。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class SharedCrossFilesGenerationTests
{
    /// <summary>原共享父模型只增加真实FileLibrary列；物理删除仍走原元数据开关。</summary>
    internal static MetadataCatalog Catalog(string prefix,string type,string parent,bool shorter=false,bool derived=false,bool physicalDelete=false)
    {
        var c=SharedCrossParentGenerationTests.Catalog(prefix,type,parent,shorter,derived);c.ConfigColumns.Add(new(){Id="9001",YzProjectId=c.YzProjectId,ConfigTableId="2",ColumnName="attachments",Name="附件",LogicalType="string",ColumnLength="max",ControlKind="FileLibrary",ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}"});foreach(var page in c.ConfigPages.Where(p=>p.ConfigTableId=="2"))c.ConfigPageColumns.Add(new(){Id=page.Id+"9001",YzProjectId=c.YzProjectId,ConfigPageId=page.Id,ConfigColumnId="9001",IsShow=1,IsEdit=1});if(physicalDelete){var deleted=c.ConfigColumns.Single(x=>x.ConfigTableId=="2"&&x.ColumnName=="delete_no");c.ConfigColumns.Remove(deleted);c.ConfigPageColumns.RemoveAll(x=>x.ConfigColumnId==deleted.Id);}return c;
    }
    /// <summary>普通、短主叶和派生目标三种Full实际更新、保护手写partial并核对原文件上下文／挂载级联组件。</summary>
    [Theory][InlineData(false,false,false)][InlineData(true,false,true)][InlineData(false,true,false)]
    public async Task Full_shared_file_packages_build_update_and_keep_original_controls(bool shorter,bool derived,bool physicalDelete)
    {
        var c=Catalog("shared_parent","string","int64",shorter,derived,physicalDelete);var root=NonRedundantApplicationFixture.Root();var context=new FileMetadataContext([c]);Assert.Single(context.Catalogs);var boundary=Path.Combine(Path.GetTempPath(),"metarill-shared-files-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"app");
        try{new ApplicationPackager().Create(c,output,"Example.SharedFileFull",root);var hand=Path.Combine(output,"backend/generated/Services/SharedParentSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 共享父范围文件手写业务保留。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(c,candidate,"Example.SharedFileFull",root);var updater=new LocalGenerationUpdater();var actions=new[]{"generate-project-config","sync-runtime","generate-all-page"};var plan=updater.Preview(output,candidate,actions);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));var repeated=updater.Preview(output,candidate,actions);Assert.Equal("unchanged",updater.Apply(repeated,repeated.PlanSha256).Status);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);foreach(var name in new[]{"yzp-cascade-tree","yzp-file-library"})Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components/"+name+"/"+name+".vue")),File.ReadAllBytes(Path.Combine(output,"frontend/components/"+name+"/"+name+".vue")));var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"SharedCrossParentVueProbe.mjs"));probe.ArgumentList.Add(Path.Combine(output,"frontend"));probe.ArgumentList.Add(shorter.ToString().ToLowerInvariant());await NonRedundantApplicationFixture.RunAsync(probe);var fileProbe=NonRedundantApplicationFixture.Start("node",output);fileProbe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"SharedCrossFilesVueProbe.mjs"));fileProbe.ArgumentList.Add(Path.Combine(output,"frontend"));await NonRedundantApplicationFixture.RunAsync(fileProbe);}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
