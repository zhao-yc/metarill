using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Metarill.Files.Tests;

/// <summary>仅本轮独占桶的授权范围，固定服务器和预算；不接受任意端点、桶名或清理前缀。</summary>
internal sealed record S3NativeScope(string Purpose,string RunId,string Endpoint,string Region,string Bucket,string Prefix,
    string ContainerId,string ImageId,string ImageName,int MaxAttempts,int MaxObjects,long MaxObjectBytes)
{
    internal const string PurposeValue="s3-objects-native-v1";
    internal const string EndpointValue="http://192.168.31.200:8333";
    internal const string ImageValue="sha256:22e834ac4cec66d1337c09038140b7c92c0dff4962b56fedb18d6ca564b80f93";
    /// <summary>资源范围逐字段固定，改变名称或预算不能沿用旧批准摘要。</summary>
    internal void Validate()
    {
        if(Purpose!=PurposeValue||!Regex.IsMatch(RunId??"","\\A[a-f0-9]{32}\\z")||Endpoint!=EndpointValue||Region!="us-east-1"||
            Bucket!="metarill-native-"+RunId||Prefix!="objects"||!Regex.IsMatch(ContainerId??"","\\A[a-f0-9]{64}\\z")||
            ImageId!=ImageValue||ImageName!="yinzon-seaweedfs:4.48-object-acl-20261004"||
            MaxAttempts!=3||MaxObjects!=4||MaxObjectBytes!=1048576)throw new InvalidOperationException("S3验收资源范围与固定协议不一致。");
    }
}

/// <summary>批准针对固定资源摘要；每次尝试另有不可变来源/编译闭包，失败复验仍限同一桶和总次数。</summary>
internal sealed record S3NativeLease(string ScopeSha256,string ArtifactSha256,string AuthorizationStatus,string LeaseStatus,
    string ApprovalReference,DateTimeOffset? ApprovedAtUtc,DateTimeOffset? ApprovedUntilUtc,int Attempts,string? ActiveAttemptId);

/// <summary>固定源码归档和全部编译输出，正式用例必须从该闭包运行，不能偷读在改的工作区文件。</summary>
internal sealed record S3NativeArtifact(string SourceCommit,string SourceArchiveSha256,string BinaryRoot,Dictionary<string,string> Files);

/// <summary>批准/来源/路径门禁先于SSH、凭据读取、S3客户端和桶操作。</summary>
internal sealed class S3NativeAuthorization
{
    internal string Root{get;} internal string LeasePath=>Path.Combine(Root,"lease.json");
    internal S3NativeScope Scope{get;} internal S3NativeLease Lease{get;private set;} internal S3NativeArtifact Artifact{get;}
    internal string ScopeSha256{get;} internal string ArtifactSha256{get;}
    private string leaseHash;

    private S3NativeAuthorization(string root,S3NativeScope scope,S3NativeLease lease,S3NativeArtifact artifact,string scopeHash,string artifactHash,string hash)
    {Root=root;Scope=scope;Lease=lease;Artifact=artifact;ScopeSha256=scopeHash;ArtifactSha256=artifactHash;leaseHash=hash;}

    /// <summary>只返回已明确批准、有效且未消费完的租约；pending不能退化为跳过或真实批准。</summary>
    internal static void ValidateGrant(S3NativeScope scope,S3NativeLease lease,string scopeHash,string artifactHash,DateTimeOffset now)
    {
        scope.Validate();
        if(!IsHash(scopeHash)||!IsHash(artifactHash)||lease.AuthorizationStatus!="approved"||lease.LeaseStatus!="ready"||string.IsNullOrWhiteSpace(lease.ApprovalReference)||
            lease.ApprovalReference.Length>300||lease.ScopeSha256!=scopeHash||lease.ArtifactSha256!=artifactHash||
            lease.ApprovedAtUtc is not {} start||lease.ApprovedUntilUtc is not {} until||start>now||until<=now||until>start.AddDays(7)||
            lease.Attempts<0||lease.Attempts>=scope.MaxAttempts||lease.ActiveAttemptId is not null)
            throw new InvalidOperationException("S3正式验收尚无当前有效的明确授权，或租约/固定来源不匹配。");
    }

    /// <summary>先查授权再验闭包，所有正式资源仅从租约同目录读取，不从工作区或任意路径拼接。</summary>
    internal static S3NativeAuthorization Load(string path)
    {
        path=Path.GetFullPath(path);RequireNoLinks(path);
        if(Path.GetFileName(path)!="lease.json"||!File.Exists(path))throw new InvalidOperationException("S3租约路径无效。");
        var root=Path.GetDirectoryName(path)!;var scopePath=Path.Combine(root,"scope.json");var artifactPath=Path.Combine(root,"artifact.json");
        RequireNoLinks(scopePath);RequireNoLinks(artifactPath);
        var scopeBytes=File.ReadAllBytes(scopePath);var artifactBytes=File.ReadAllBytes(artifactPath);var leaseBytes=File.ReadAllBytes(path);
        var scope=Read<S3NativeScope>(scopeBytes);var lease=Read<S3NativeLease>(leaseBytes);
        var scopeHash=Hash(scopeBytes);var artifactHash=Hash(artifactBytes);ValidateGrant(scope,lease,scopeHash,artifactHash,DateTimeOffset.UtcNow);
        if(Path.GetFileName(root)!=scope.RunId)throw new InvalidOperationException("S3租约目录与本轮编号不一致。");
        var artifact=Read<S3NativeArtifact>(artifactBytes);var result=new S3NativeAuthorization(root,scope,lease,artifact,scopeHash,artifactHash,Hash(leaseBytes));
        result.VerifyArtifact();
        if(Path.GetFullPath(typeof(S3NativeAuthorization).Assembly.Location)!=Path.Combine(artifact.BinaryRoot,"Metarill.Files.Tests.dll"))
            throw new InvalidOperationException("正式S3用例没有从固定编译闭包启动。");
        return result;
    }

    /// <summary>核对完整文件集合、每份字节及原源码zip；未知新增/缺失/链接文件也拒绝。</summary>
    internal void VerifyArtifact()=>VerifyArtifact(Root,Artifact);

    /// <summary>同一闭包校验可由离线真实文件反例调用，不需创建SDK客户端。</summary>
    internal static void VerifyArtifact(string root,S3NativeArtifact artifact)
    {
        if(artifact.SourceCommit is not {} sourceCommit||!Regex.IsMatch(sourceCommit,"\\A[a-f0-9]{40}\\z")||!IsHash(artifact.SourceArchiveSha256)||artifact.Files is null||artifact.Files.Count<5)
            throw new InvalidOperationException("S3固定来源声明无效。");
        var artifactRoot=Path.Combine(root,"artifacts",sourceCommit);var expected=Path.Combine(artifactRoot,"frozen");
        if(artifact.BinaryRoot!=expected)throw new InvalidOperationException("S3固定编译路径无效。");
        RequireNoLinks(expected);var archive=Path.Combine(artifactRoot,"source.zip");RequireNoLinks(archive);
        if(HashFile(archive)!=artifact.SourceArchiveSha256)throw new InvalidOperationException("S3原源码归档发生变化。");
        var actual=Directory.EnumerateFiles(expected,"*",SearchOption.AllDirectories).Select(file=>Path.GetRelativePath(expected,file).Replace('\\','/')).ToHashSet(StringComparer.Ordinal);
        if(!actual.SetEquals(artifact.Files.Keys))throw new InvalidOperationException("S3编译闭包文件集合发生变化。");
        foreach(var (relative,digest) in artifact.Files)
        {
            if(Path.IsPathRooted(relative)||relative.Contains('\\')||relative.Split('/').Any(part=>part is "" or "." or "..")||!IsHash(digest))throw new InvalidOperationException("S3编译闭包路径或摘要无效。");
            var file=Path.Combine(expected,relative);RequireNoLinks(file);if(HashFile(file)!=digest)throw new InvalidOperationException("S3固定编译文件发生变化。");
        }
        foreach(var required in new[]{"Metarill.Files.Tests.dll","Metarill.Files.dll","AWSSDK.S3.dll","AWSSDK.Core.dll","native/server200-s3-info.py"})
            if(!actual.Contains(required))throw new InvalidOperationException("S3固定闭包缺少必要文件。");
    }

    /// <summary>开始和结束状态使用摘要比较后替换；不能覆盖用户或另一个进程新改的租约。</summary>
    internal void WriteLease(S3NativeLease value)
    {
        if(HashFile(LeasePath)!=leaseHash)throw new InvalidOperationException("S3租约已被其它操作修改，保留现场。");
        var bytes=JsonSerializer.SerializeToUtf8Bytes(value,Json);
        var temporary=LeasePath+".tmp-"+Guid.NewGuid().ToString("N");RequireNoLinks(temporary);
        using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes);stream.Flush(true);}
        File.Move(temporary,LeasePath,true);Lease=value;leaseHash=Hash(bytes);
    }

    /// <summary>运行阶段仍核对批准、作用域、来源和本次编号，过期/撤销不能继续对象变更。</summary>
    internal void RequireRunning(string attemptId)
    {
        if(HashFile(LeasePath)!=leaseHash||HashFile(Path.Combine(Root,"scope.json"))!=ScopeSha256||HashFile(Path.Combine(Root,"artifact.json"))!=ArtifactSha256||
            Lease.AuthorizationStatus!="approved"||Lease.LeaseStatus!="running"||Lease.ActiveAttemptId!=attemptId||Lease.ApprovedUntilUtc is not {} until||until<=DateTimeOffset.UtcNow)
            throw new InvalidOperationException("S3运行授权或固定输入已经变化。");
    }

    /// <summary>配置只接收严格JSON，重复成员不得被最后值覆盖；错误不带入原文。</summary>
    internal static T Read<T>(byte[] bytes)
    {
        try{using var doc=JsonDocument.Parse(bytes);Unique(doc.RootElement);return JsonSerializer.Deserialize<T>(bytes,Json)??throw new InvalidOperationException("S3配置为空。");}
        catch(JsonException){throw new InvalidOperationException("S3租约或固定来源JSON无效。");}
    }

    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true,PropertyNameCaseInsensitive=false,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    /// <summary>对象和数组递归检查重名，避免审批显示值与实际解析值不同。</summary>
    private static void Unique(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Object)
        {var seen=new HashSet<string>(StringComparer.Ordinal);foreach(var property in value.EnumerateObject()){if(!seen.Add(property.Name))throw new InvalidOperationException("S3配置包含重复字段。");Unique(property.Value);}}
        if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())Unique(item);
    }

    /// <summary>现存祖先均不能为符号链接/重解析点，冻结文件和租约不得跳出原目录。</summary>
    internal static void RequireNoLinks(string path)
    {
        for(var current=Path.GetFullPath(path);current is not null;current=Path.GetDirectoryName(current))
            if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("S3验收路径不能包含符号链接。");
    }

    /// <summary>摘要必须是完整小写SHA-256，不允许空值或尾随换行。</summary>
    internal static bool IsHash(string? value)=>value is not null&&Regex.IsMatch(value,"\\A[a-f0-9]{64}\\z");
    /// <summary>租约及固定清单按原始UTF8字节计算摘要。</summary>
    internal static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    /// <summary>大文件以流计算摘要，不把整个运行时DLL读入内存。</summary>
    internal static string HashFile(string path){using var input=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(input));}
}
