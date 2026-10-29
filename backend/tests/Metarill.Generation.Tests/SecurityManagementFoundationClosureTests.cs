using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>管理元数据与独立包基础源码必须同源，不能把可编译但结构矛盾的包交给开发者。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class SecurityManagementFoundationClosureTests
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true};

    /// <summary>普通Full可接受另一合法基础版本，固定管理模块必须拒绝它与自身冻结结构的错配。</summary>
    [Theory]
    [InlineData("files-column")]
    [InlineData("security-column")]
    [InlineData("security-index")]
    [InlineData("files-index-direction")]
    [InlineData("files-added-column")]
    [InlineData("files-added-table")]
    [InlineData("files-removed-table")]
    public async Task Prepared_management_rejects_valid_but_mismatched_foundations_without_publishing(string change)
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-management-foundation-closure");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var application=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(source,"backend/metadata/demo.json")),Json)!;
            var module=SecurityManagementModuleFactory.Compose(application,application.YzAdminTypes[0].Id);
            var snapshot=Path.Combine(root,"source");CopySource(source,snapshot);
            var filename=change.StartsWith("files",StringComparison.Ordinal)?"files":"security";
            var file=Path.Combine(snapshot,"backend/metadata/"+filename+".json");var metadata=JsonNode.Parse(File.ReadAllText(file))!;
            if(change=="security-index")
            {
                metadata["configIndexes"]![0]!["isUnique"]=0;metadata["configIndexes"]![0]!["indexType"]="INDEX";
            }
            else if(change=="files-index-direction")metadata["configIndexColumns"]![0]!["sortDirection"]="DESC";
            else if(change=="files-added-table")
            {
                metadata["configTables"]!.AsArray().Add(new JsonObject{["id"]="9001234500000000020",["tableName"]="closure_extra",["name"]="额外基础表"});
                metadata["configColumns"]!.AsArray().Add(new JsonObject{["id"]="9001234500000000021",["configTableId"]="9001234500000000020",["columnName"]="id",["logicalType"]="int64",["isDbNotNull"]=1});
            }
            else if(change=="files-removed-table")
            {
                var id=metadata["configTables"]!.AsArray().Single(value=>value!["tableName"]!.GetValue<string>()=="yz_file_library_folder")!["id"]!.GetValue<string>();
                var indexIds=metadata["configIndexes"]!.AsArray().Where(value=>value!["configTableId"]!.GetValue<string>()==id).Select(value=>value!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
                foreach(var (key,owner) in new[]{("configTables","id"),("configColumns","configTableId"),("configIndexes","configTableId")})
                    metadata[key]=new JsonArray(metadata[key]!.AsArray().Where(value=>value![owner]!.GetValue<string>()!=id).Select(value=>value!.DeepClone()).ToArray());
                metadata["configIndexColumns"]=new JsonArray(metadata["configIndexColumns"]!.AsArray().Where(value=>!indexIds.Contains(value!["configIndexId"]!.GetValue<string>())).Select(value=>value!.DeepClone()).ToArray());
            }
            else if(change=="files-added-column")metadata["configColumns"]!.AsArray().Add(new JsonObject
            {
                ["id"]="9001234500000000001",["configTableId"]=metadata["configTables"]![0]!["id"]!.GetValue<string>(),
                ["columnName"]="closure_probe",["name"]="基础闭包样本",["logicalType"]="string",["columnLength"]="17"
            });
            else
            {
                var field=metadata["configColumns"]!.AsArray().First(value=>value!["columnName"]!.GetValue<string>()==(filename=="files"?"insert_user_name":"name"))!;
                field["columnLength"]="401";
            }
            File.WriteAllText(file,metadata.ToJsonString(Json));
            // 先证明输入仍是可独立打包的合法版本，不能以无效JSON或缺文件制造假拒绝。
            new ApplicationPackager().Create(application,Path.Combine(root,"ordinary"),"Example.Foundation",snapshot);
            var output=Path.Combine(root,"managed");
            var error=Assert.Throws<InvalidOperationException>(()=>new SecurityManagementApplicationPreparer().Prepare(module,output,"Example.Foundation",snapshot));
            Assert.Contains("基础",error.Message);Assert.False(Directory.Exists(output));
            Assert.Empty(Directory.GetDirectories(root,"managed.preparing-*"));
            // 拒绝不改来源与签发快照；修正应发生在公共基础元数据，而非补丁生成文件。
            Assert.Equal(metadata.ToJsonString(Json),File.ReadAllText(file));
            Assert.Equal(application.YzProjectId,SecurityManagementModuleFactory.RequireApplicationForPreparation(module).YzProjectId);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>只改说明和JSON排列仍是相同物理版本，不能用原字节散列误拒绝开发者正常维护。</summary>
    [Fact]
    public async Task Nonphysical_foundation_labels_and_order_do_not_change_the_bound_management_graph()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-management-foundation-closure");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var application=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(source,"backend/metadata/demo.json")),Json)!;
            var module=SecurityManagementModuleFactory.Compose(application,application.YzAdminTypes[0].Id);var before=JsonSerializer.Serialize(module.Published,Json);
            var snapshot=Path.Combine(root,"source");CopySource(source,snapshot);
            foreach(var name in new[]{"files","security"})
            {
                var file=Path.Combine(snapshot,"backend/metadata/"+name+".json");var metadata=JsonNode.Parse(File.ReadAllText(file))!;
                metadata["configTables"]![0]!["name"]="仅修改基础说明";
                var columns=metadata["configColumns"]!.AsArray();var reordered=columns.Reverse().Select(value=>value!.DeepClone()).ToArray();
                metadata["configColumns"]=new JsonArray(reordered);File.WriteAllText(file,metadata.ToJsonString(Json));
            }
            var output=Path.Combine(root,"managed");new SecurityManagementApplicationPreparer().Prepare(module,output,"Example.Foundation",snapshot);
            Assert.True(File.Exists(Path.Combine(output,".metarill-management-preparation.json")));Assert.False(File.Exists(Path.Combine(output,".metarill-package.json")));
            Assert.Equal(before,JsonSerializer.Serialize(module.Published,Json));
            using var marker=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,".metarill-management-preparation.json")));Assert.False(marker.RootElement.GetProperty("readyForPublication").GetBoolean());
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>只复制真实打包读取的固定源码与原前端，不跟随链接，不带依赖、构建输出或私有目录。</summary>
    private static void CopySource(string source,string destination)
    {
        foreach(var relative in new[]{"Directory.Packages.props","global.json","LICENSE","backend/metadata/files.json","backend/metadata/security.json"})CopyFile(Path.Combine(source,relative),Path.Combine(destination,relative));
        foreach(var project in ApplicationPackager.FullFoundationProjects)CopyTree(Path.Combine(source,"backend/src",project),Path.Combine(destination,"backend/src",project));
        CopyTree(Path.Combine(source,"frontend/metarill_uni_admin"),Path.Combine(destination,"frontend/metarill_uni_admin"));
    }
    /// <summary>临时根由本测试独占；递归跳过依赖和私有目录，重解析点明确拒绝。</summary>
    private static void CopyTree(string source,string destination)
    {
        Directory.CreateDirectory(destination);
        foreach(var path in Directory.EnumerateFileSystemEntries(source))
        {
            var attributes=File.GetAttributes(path);if((attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("测试来源不允许链接。");
            var name=Path.GetFileName(path);
            if((attributes&FileAttributes.Directory)!=0)
            {
                if(name.StartsWith('.')||name is "bin" or "obj" or "node_modules" or "unpackage")continue;
                CopyTree(path,Path.Combine(destination,name));
            }
            else CopyFile(path,Path.Combine(destination,name));
        }
    }
    /// <summary>复制原字节，仅写本轮独占快照，不改变工作区资源。</summary>
    private static void CopyFile(string source,string destination){Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(source,destination);}
}
