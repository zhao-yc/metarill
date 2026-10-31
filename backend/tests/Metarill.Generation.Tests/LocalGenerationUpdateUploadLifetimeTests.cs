using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Metarill.Core.Contracts;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class LocalGenerationUpdateTests
{
    /// <summary>即使重算包摘要，候选缺上传期限的发布模型仍拒绝，目标逐字保持。</summary>
    [Theory][InlineData("missing")][InlineData("required")][InlineData("type")][InlineData("precision")][InlineData("default")][InlineData("physical")][InlineData("time")][InlineData("malformed")]
    public void Candidate_requires_fixed_upload_deadline_metadata(string change)
    {
        var(target,candidate)=Packages(Catalog(),Catalog());var updater=new LocalGenerationUpdater();Assert.True(updater.Preview(target,candidate,All).CanApply);var before=SourceSnapshot(target);
        MutateUploadMetadata(candidate,change);Assert.Contains(FileUploadLifetime.DeadlineColumn,Assert.Throws<InvalidOperationException>(()=>updater.Preview(target,candidate,All)).Message);Assert.Equal(before,SourceSnapshot(target));
    }
    /// <summary>明确的旧目录模拟可预览/应用升级，结构变更仍留给显式Schema流程。</summary>
    [Fact]
    public void Previous_target_without_deadline_metadata_can_upgrade_without_ddl()
    {
        var(target,candidate)=Packages(Catalog(),Catalog());MutateUploadMetadata(target,"missing");var updater=new LocalGenerationUpdater();var plan=updater.Preview(target,candidate,["sync-runtime"]);Assert.True(plan.CanApply);Assert.Contains(plan.Files,file=>file.Path=="backend/metadata/files.json"&&file.Action=="replace");updater.Apply(plan,plan.PlanSha256);
        Assert.Equal(File.ReadAllBytes(Path.Combine(candidate,"backend/metadata/files.json")),File.ReadAllBytes(Path.Combine(target,"backend/metadata/files.json")));var again=updater.Preview(target,candidate,All);Assert.Equal("unchanged",updater.Apply(again,again.PlanSha256).Status);
    }
    /// <summary>只改本独占完整包的固定元数据并重算外层摘要，不删除归属或绕过校验。</summary>
    private static void MutateUploadMetadata(string root,string change)
    {
        const string relative="backend/metadata/files.json";var path=Path.Combine(root,relative);var model=JsonNode.Parse(File.ReadAllText(path))!;var columns=model["configColumns"]!.AsArray();var column=columns.Single(item=>item!["columnName"]!.GetValue<string>()==FileUploadLifetime.DeadlineColumn)!;
        if(change=="missing")columns.Remove(column);if(change=="required")column["isDbNotNull"]=1;if(change=="type")column["logicalType"]="string";if(change=="precision")column["columnType"]="datetime(0)";if(change=="default")column["defaultValue"]="now()";if(change=="physical")column["columnType"]="nvarchar";if(change=="time")column["columnType"]="time(6)";if(change=="malformed")column["columnType"]="datetime(6) NULL";File.WriteAllText(path,model.ToJsonString(Json));
        var marker=Path.Combine(root,".metarill-package.json");var package=JsonNode.Parse(File.ReadAllText(marker))!;package["files"]!.AsArray().Single(item=>item!["path"]!.GetValue<string>()==relative)!["sha256"]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();File.WriteAllText(marker,package.ToJsonString(Json));
    }
}
