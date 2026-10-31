using Xunit;

namespace Metarill.Files.Tests;

/// <summary>固定闭包的检查使用真实临时文件；不启动SDK、SSH或任何网络依赖。</summary>
public sealed class S3NativeArtifactTests
{
    /// <summary>全部文件集合、归档、路径与摘要必须一致，额外文件也不能被静默忽略。</summary>
    [Theory]
    [InlineData("good")][InlineData("archive")][InlineData("extra")][InlineData("missing")]
    [InlineData("bytes")][InlineData("escape")][InlineData("binaryRoot")][InlineData("commit")]
    public void Frozen_source_and_complete_binary_set_reject_drift(string scenario)
    {
        var root=Path.Combine(Path.GetTempPath(),"metarill-s3-artifact-"+Guid.NewGuid().ToString("N"));
        var commit=new string('a',40);var artifactRoot=Path.Combine(root,"artifacts",commit);var frozen=Path.Combine(artifactRoot,"frozen");
        Directory.CreateDirectory(Path.Combine(frozen,"native"));
        try
        {
            var archive=Path.Combine(artifactRoot,"source.zip");File.WriteAllText(archive,"仅合成归档摘要样本");
            var names=new[]{"Metarill.Files.Tests.dll","Metarill.Files.dll","AWSSDK.S3.dll","AWSSDK.Core.dll","native/server200-s3-info.py"};
            foreach(var name in names)File.WriteAllText(Path.Combine(frozen,name),"仅离线字节样本："+name);
            var files=names.ToDictionary(name=>name,name=>S3NativeAuthorization.HashFile(Path.Combine(frozen,name)),StringComparer.Ordinal);
            var artifact=new S3NativeArtifact(commit,S3NativeAuthorization.HashFile(archive),frozen,files);
            switch(scenario)
            {
                case "archive":File.AppendAllText(archive,"变更");break;
                case "extra":File.WriteAllText(Path.Combine(frozen,"unexpected.txt"),"额外文件");break;
                case "missing":File.Delete(Path.Combine(frozen,names[0]));break;
                case "bytes":File.AppendAllText(Path.Combine(frozen,names[0]),"变更");break;
                case "escape":files.Add("../outside",new string('a',64));break;
                case "binaryRoot":artifact=artifact with{BinaryRoot=Path.GetTempPath()};break;
                case "commit":artifact=artifact with{SourceCommit="main"};break;
            }
            if(scenario=="good")S3NativeAuthorization.VerifyArtifact(root,artifact);
            else Assert.Throws<InvalidOperationException>(()=>S3NativeAuthorization.VerifyArtifact(root,artifact));
        }
        finally
        {
            // root只来自系统临时目录和本测试新建GUID，绝不删除调用者或租约目录。
            if(Path.GetDirectoryName(root)!=Path.TrimEndingDirectorySeparator(Path.GetTempPath()))throw new InvalidOperationException("临时样本清理越界。");
            Directory.Delete(root,true);
        }
    }
}
