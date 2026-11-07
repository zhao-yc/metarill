using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Metarill.Runtime.Files;
using Xunit;
using Xunit.Sdk;

namespace Metarill.Files.Tests;

/// <summary>真实S3用例默认明确跳过；显式提供pending租约则必须失败，不能伪装已批准。</summary>
public sealed class S3NativeFactAttribute:FactAttribute
{
    public S3NativeFactAttribute(){if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("METARILL_S3_NATIVE_LEASE")))Skip="需要当前明确批准的Server-200独占S3桶及固定编译租约。";}
}

/// <summary>正式用例使用实际公开FileObjectStorage与真实SDK；不替换S3客户端，不连接数据库。</summary>
public sealed class S3NativeAcceptanceTests
{
    [S3NativeFact]
    public async Task Server200_real_objects_are_immutable_verified_seekable_and_independently_cleaned()
    {
        var path=Environment.GetEnvironmentVariable("METARILL_S3_NATIVE_LEASE")??throw new InvalidOperationException("缺少S3正式租约。");
        var authorization=S3NativeAuthorization.Load(path);
        using var leaseLock=new FileStream(Path.Combine(authorization.Root,"lease.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,1,FileOptions.DeleteOnClose);
        authorization=S3NativeAuthorization.Load(path);
        var run=new S3NativeRun(authorization);await run.ExecuteAsync();
    }
}

internal sealed record S3NativeBody(long Length,string Sha256);
internal sealed record S3NativeAsset(string Key,IReadOnlyList<S3NativeBody> AllowedBodies);

/// <summary>单进程持有租约锁；只在已确认创建的独占桶内核对、读写和清理固定合成样本。</summary>
internal sealed class S3NativeRun
{
    private readonly S3NativeAuthorization authorization;
    private readonly List<S3NativeAsset> assets=[];
    private readonly List<string> cases=[];
    private readonly string attemptId;
    private readonly string root;
    private readonly string cache;
    private string stage="before-network";
    private string? journalHash;
    private string? failureType;
    private string? cleanupFailureType;
    private bool createAttempted,created,collision,cleared;
    private FileObjectStorage? storage;
    private S3FileStorageProfile profile=null!;
    private AmazonS3Client? admin;
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true};

    /// <summary>所有本地写入位于本轮新目录；租约状态先记录，不能让两个进程同时消费批准。</summary>
    internal S3NativeRun(S3NativeAuthorization authorization)
    {
        this.authorization=authorization;attemptId=(authorization.Lease.Attempts+1).ToString("D2",System.Globalization.CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N");
        root=Path.Combine(authorization.Root,"runs",attemptId);cache=Path.Combine(root,"cache");S3NativeAuthorization.RequireNoLinks(root);
        if(Directory.Exists(root))throw new InvalidOperationException("S3尝试目录已存在。");
        Directory.CreateDirectory(root);Directory.CreateDirectory(cache);
        authorization.WriteLease(authorization.Lease with{LeaseStatus="running",Attempts=authorization.Lease.Attempts+1,ActiveAttemptId=attemptId});
        Journal();
    }

    /// <summary>任何实际失败都保留阶段与类型；SDK异常正文和凭据不会进入Xunit输出或回执。</summary>
    internal async Task ExecuteAsync()
    {
        var passed=false;var cleanup=false;
        using var limit=new CancellationTokenSource(TimeSpan.FromMinutes(6));
        try
        {
            Check();stage="server-pin-and-private-credentials";Journal();
            var server=await ServerAsync(true,limit.Token);
            profile=new S3FileStorageProfile{Endpoint=authorization.Scope.Endpoint,Region=authorization.Scope.Region,Bucket=authorization.Scope.Bucket,Prefix=authorization.Scope.Prefix,
                AccessKey=server.AccessKey??"",SecretKey=server.SecretKey??"",ForcePathStyle=true,AllowHttp=true}.Snapshot();
            admin=Client();stage="preflight-bucket-absence";Journal();
            if(!await AbsentBucketAsync(admin,limit.Token)){collision=true;throw new InvalidOperationException("指定桶已存在，不能认领或清理。");}
            Check();stage="create-exclusive-bucket";createAttempted=true;Journal();
            await admin.PutBucketAsync(new PutBucketRequest{BucketName=profile.Bucket},limit.Token);created=true;Journal();
            var marker=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{purpose=S3NativeScope.PurposeValue,authorization.Scope.RunId,authorization.ScopeSha256,attemptId,authorization.Artifact.SourceCommit},Json));
            Register("ownership.json",marker);
            await PutAsync(admin,"ownership.json",marker,limit.Token);await VerifyAsync(admin,assets[0],limit.Token);
            storage=new FileObjectStorage(new FileLibraryOptions{RootDirectory=cache,MaxFileBytes=authorization.Scope.MaxObjectBytes,
                Storage=new(){DefaultProfile="native",S3Profiles=new(){{"native",profile}}}});
            await ProbeAsync(limit.Token);passed=true;
        }
        catch(Exception error){failureType=error.GetType().FullName;}
        finally
        {
            storage?.Dispose();admin?.Dispose();
            try
            {
                if(created){stage="cleanup-confirmed-owned-bucket";Journal();await CleanupAsync();cleanup=cleared;}
                else cleanup=!createAttempted&&!collision;
            }
            catch(Exception error){cleanupFailureType=error.GetType().FullName;}
            var state=passed&&cleanup?"ended":collision?"ended_conflict":!cleanup?"cleanup_failed":authorization.Lease.Attempts<authorization.Scope.MaxAttempts?"ready":"ended_failed";
            try{authorization.WriteLease(authorization.Lease with{LeaseStatus=state,ActiveAttemptId=null});}
            catch(Exception error){cleanupFailureType=error.GetType().FullName;cleanup=false;}
            // 只记录受控合成资产和来源指纹，不序列化profile或server完整响应。
            File.WriteAllText(Path.Combine(root,"receipt.json"),JsonSerializer.Serialize(new{authorization.Scope.RunId,authorization.Scope.Bucket,authorization.ScopeSha256,authorization.Artifact.SourceCommit,
                authorization.ArtifactSha256,attemptId,stage,passed,cleanup,bucketAbsentVerified=cleared,createAttempted,created,collision,failureType,cleanupFailureType,cases,assets},Json),Encoding.UTF8);
        }
        if(!passed||!cleanup)throw new XunitException("S3正式验收失败，未计通过；请核对本轮私有receipt.json中的阶段、类型和清理状态。");
    }

    /// <summary>零字节、小文件和1MiB文件均经实际公开对象层；候选覆盖必须被真实供应商412及产品共同拒绝。</summary>
    private async Task ProbeAsync(CancellationToken token)
    {
        var small=Enumerable.Range(0,8192).Select(i=>(byte)(i%251)).ToArray();
        foreach(var (label,bytes) in new[]{("zero",Array.Empty<byte>()),("small",small),("one-mib",Enumerable.Range(0,1048576).Select(i=>(byte)(i%251)).ToArray())})
        {
            Check();stage="public-storage-"+label;Journal();
            var key=storage!.CreateKey();var objectKey=profile.Prefix+"/"+key[(key.LastIndexOf(':')+1)..];Register(objectKey,bytes);
            var input=Path.Combine(root,label+".part");await File.WriteAllBytesAsync(input,bytes,token);
            await storage.CommitAsync(key,input,cache,bytes.Length,S3NativeAuthorization.Hash(bytes),token);
            await ReadProductAsync(key,bytes,token);cases.Add(label+"-actual-sha-seek-readonly");
            if(label=="small")
            {
                await storage.CommitAsync(key,input,cache,bytes.Length,S3NativeAuthorization.Hash(bytes),token);
                cases.Add("same-key-identical-retry");
                var different=(byte[])bytes.Clone();different[0]^=0x7f;Register(objectKey,different);
                var rejected=false;
                try{await PutAsync(admin!,objectKey,different,token);}
                catch(AmazonS3Exception error) when(error.StatusCode==HttpStatusCode.PreconditionFailed){rejected=true;}
                if(!rejected)throw new InvalidOperationException("S3未执行已声明的条件写，停止验收。");
                await ReadProductAsync(key,bytes,token);cases.Add("provider-if-none-match-412");
                var alternate=Path.Combine(root,"different.part");await File.WriteAllBytesAsync(alternate,different,token);rejected=false;
                try{await storage.CommitAsync(key,alternate,cache,different.Length,S3NativeAuthorization.Hash(different),token);}
                catch(InvalidOperationException){rejected=true;}
                if(!rejected)throw new InvalidOperationException("产品未拒绝不同内容覆盖。");
                await ReadProductAsync(key,bytes,token);cases.Add("product-rejects-different-bytes");
                rejected=false;
                try{await using var invalid=await storage.OpenAsync(key,cache,bytes.Length,new string('0',64),token);}
                catch(InvalidOperationException){rejected=true;}
                if(!rejected||Directory.EnumerateFiles(cache,"*.blob").Any())throw new InvalidOperationException("摘要拒绝或临时缓存清理失败。");
                cases.Add("wrong-digest-rejected-and-cache-removed");
            }
            Check();if(!await storage.DeleteAsync(key,cache,token))throw new InvalidOperationException("实际对象删除返回失败。");
            try{using var found=await admin!.GetObjectAsync(profile.Bucket,objectKey,token);throw new InvalidOperationException("删除后对象仍可读。");}
            catch(AmazonS3Exception error) when(error.StatusCode==HttpStatusCode.NotFound){}
            cases.Add(label+"-deleted-and-reread-404");
        }
    }

    /// <summary>读取实际字节并验证SHA；不把远端内容打印到断言，也不只信任ETag或元数据。</summary>
    private async Task ReadProductAsync(string key,byte[] expected,CancellationToken token)
    {
        await using(var read=await storage!.OpenAsync(key,cache,expected.Length,S3NativeAuthorization.Hash(expected),token))
        {
            if(read.CanWrite||!read.CanSeek||read.Length!=expected.Length)throw new InvalidOperationException("实际文件流不满足原只读/Seek合同。");
            using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[65536];long total=0;
            while(true){var count=await read.ReadAsync(buffer,token);if(count==0)break;total+=count;if(total>expected.Length)throw new InvalidOperationException("对象读取超出计划。");digest.AppendData(buffer,0,count);}
            if(total!=expected.Length||Convert.ToHexStringLower(digest.GetHashAndReset())!=S3NativeAuthorization.Hash(expected))throw new InvalidOperationException("S3实际字节校验失败。");
            if(expected.Length>0){read.Seek(expected.Length/2,SeekOrigin.Begin);if(read.ReadByte()!=expected[expected.Length/2])throw new InvalidOperationException("S3 Seek读取错误。");}
            var denied=false;try{read.WriteByte(0);}catch(NotSupportedException){denied=true;}if(!denied)throw new InvalidOperationException("S3缓存错误开放写入。");
        }
        if(Directory.EnumerateFiles(cache,"*.blob").Any())throw new InvalidOperationException("只读流关闭后遗留缓存。");
    }

    /// <summary>先登记每个对象所有允许的合成字节，意外覆盖为另一已计划样本时也能精确核验后清理。</summary>
    private void Register(string key,byte[] bytes)
    {
        if(bytes.LongLength>authorization.Scope.MaxObjectBytes||key!="ownership.json"&&!System.Text.RegularExpressions.Regex.IsMatch(key,"\\Aobjects/[a-f0-9]{32}\\.blob\\z"))throw new InvalidOperationException("S3对象超出本轮范围。");
        var index=assets.FindIndex(asset=>asset.Key==key);var body=new S3NativeBody(bytes.LongLength,S3NativeAuthorization.Hash(bytes));
        if(index<0){if(assets.Count>=authorization.Scope.MaxObjects)throw new InvalidOperationException("S3对象数量超出批准预算。");assets.Add(new(key,[body]));}
        else if(!assets[index].AllowedBodies.Contains(body))assets[index]=assets[index] with{AllowedBodies=assets[index].AllowedBodies.Append(body).ToArray()};
        Journal();
    }

    /// <summary>清理先完整列出并逐字核对，不删除未知对象；桶删后用新的SDK连接独立确认404。</summary>
    private async Task CleanupAsync()
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));var token=timeout.Token;Check();authorization.VerifyArtifact();await ServerAsync(false,token);
        using var cleaner=Client();
        async Task<S3NativeInventory> List()
        {
            Check();var value=await cleaner.ListObjectsV2Async(new ListObjectsV2Request{BucketName=profile.Bucket,MaxKeys=16},token);
            return new(value.IsTruncated==false,(value.S3Objects??[]).Select(item=>item.Key).ToArray());
        }
        await S3NativeCleanup.ApplyAsync(assets,List,asset=>VerifyAsync(cleaner,asset,token),
            async key=>{Check();await cleaner.DeleteObjectAsync(profile.Bucket,key,token);},
            async()=>{Check();await cleaner.DeleteBucketAsync(profile.Bucket,token);},
            async()=>{using var independent=Client();return await AbsentBucketAsync(independent,token);});
        cleared=true;
    }

    /// <summary>独立SDK真实GET按计划大小有界校验；读取完所有对象前不进行任何删除。</summary>
    private async Task VerifyAsync(AmazonS3Client client,S3NativeAsset asset,CancellationToken token)
    {
        using var response=await client.GetObjectAsync(profile.Bucket,asset.Key,token);
        if(!asset.AllowedBodies.Any(body=>body.Length==response.ContentLength))throw new InvalidOperationException("S3清理对象大小与样本不一致。");
        using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var bytes=new byte[65536];long length=0;
        while(true){var count=await response.ResponseStream.ReadAsync(bytes,token);if(count==0)break;length+=count;if(length>authorization.Scope.MaxObjectBytes)throw new InvalidOperationException("S3清理对象超过上限。");digest.AppendData(bytes,0,count);}
        if(!asset.AllowedBodies.Contains(new(length,Convert.ToHexStringLower(digest.GetHashAndReset()))))throw new InvalidOperationException("S3清理对象实际字节不匹配。");
    }

    /// <summary>只有明确404代表桶不存在；403、超时或其它异常不能当作可创建或已核零。</summary>
    private async Task<bool> AbsentBucketAsync(AmazonS3Client client,CancellationToken token)
    {
        try{await client.HeadBucketAsync(new HeadBucketRequest{BucketName=authorization.Scope.Bucket},token);return false;}
        catch(AmazonS3Exception error) when(error.StatusCode==HttpStatusCode.NotFound){return true;}
    }

    /// <summary>标记及负向探针也使用真实条件PUT，不允许同名覆盖；不是替代产品客户端。</summary>
    private async Task PutAsync(AmazonS3Client client,string key,byte[] bytes,CancellationToken token)
    {
        Check();using var input=new MemoryStream(bytes,false);
        await client.PutObjectAsync(new PutObjectRequest{BucketName=profile.Bucket,Key=key,InputStream=input,AutoCloseStream=false,IfNoneMatch="*",ContentType="application/octet-stream"},token);
    }

    /// <summary>管理/核验连接不自动重试；可选响应校验由本工具实际字节SHA再次验证，产品连接仍使用原公开实现。</summary>
    private AmazonS3Client Client()=>new(new BasicAWSCredentials(profile.AccessKey,profile.SecretKey),new AmazonS3Config
    {ServiceURL=profile.Endpoint,AuthenticationRegion=profile.Region,ForcePathStyle=true,MaxErrorRetry=0,Timeout=TimeSpan.FromSeconds(20),
        RequestChecksumCalculation=Amazon.Runtime.RequestChecksumCalculation.WHEN_REQUIRED,ResponseChecksumValidation=Amazon.Runtime.ResponseChecksumValidation.WHEN_REQUIRED});

    /// <summary>本轮编号、授权与日志均保持不变才继续，防止复用旧状态或覆盖外部修改。</summary>
    private void Check()
    {
        authorization.RequireRunning(attemptId);
        if(journalHash is not null&&S3NativeAuthorization.HashFile(Path.Combine(root,"journal.json"))!=journalHash)throw new InvalidOperationException("S3本轮所有权日志已变化。");
    }

    /// <summary>意图在请求前持久化，内容只含测试对象摘要，不保存密钥或服务端异常正文。</summary>
    private void Journal()
    {
        var path=Path.Combine(root,"journal.json");
        if(journalHash is not null&&S3NativeAuthorization.HashFile(path)!=journalHash)throw new InvalidOperationException("S3所有权日志被外部修改。");
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new{attemptId,authorization.ScopeSha256,authorization.ArtifactSha256,stage,createAttempted,created,assets},Json);
        using var output=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None);output.Write(bytes);output.Flush(true);journalHash=S3NativeAuthorization.Hash(bytes);
    }

    /// <summary>严格SSH主机校验读取固定容器指纹；凭据只在获批后读入当前进程内存，不写配置或日志。</summary>
    private async Task<S3ServerReply> ServerAsync(bool credentials,CancellationToken token)
    {
        Check();var script=Path.Combine(authorization.Artifact.BinaryRoot,"native","server200-s3-info.py");
        if(S3NativeAuthorization.HashFile(script)!=authorization.Artifact.Files["native/server200-s3-info.py"])throw new InvalidOperationException("S3只读服务检查器发生变化。");
        var request=Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new{action=credentials?"credentials":"inspect",expected=new{containerId=authorization.Scope.ContainerId,imageId=authorization.Scope.ImageId}}));
        var start=new ProcessStartInfo("ssh"){RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
        foreach(var arg in new[]{"-o","BatchMode=yes","-o","StrictHostKeyChecking=yes","-o","ConnectTimeout=8","deve-server.yinzon.com","python3","-",request})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new InvalidOperationException("只读SSH检查未启动。");var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        try
        {
            await process.StandardInput.WriteAsync(await File.ReadAllTextAsync(script,token));process.StandardInput.Close();await process.WaitForExitAsync(token);
            var raw=await output;await errors;
            if(process.ExitCode!=0||raw.Length>16384)throw new InvalidOperationException("只读S3服务检查失败。");
            var reply=JsonSerializer.Deserialize<S3ServerReply>(raw,Json)??throw new InvalidOperationException("只读S3服务回执缺失。");
            if(reply.Host!="Server-200"||reply.Address!="192.168.31.200"||reply.Endpoint!=authorization.Scope.Endpoint||reply.ContainerId!=authorization.Scope.ContainerId||reply.ImageId!=authorization.Scope.ImageId||reply.ImageName!=authorization.Scope.ImageName)
                throw new InvalidOperationException("S3服务器或服务指纹与租约不一致。");
            return reply;
        }
        catch
        {
            if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);}
            await output;await errors;throw new InvalidOperationException("S3只读服务核验失败，未回显凭据。");
        }
    }

    /// <summary>此类型可能短暂持有现有服务凭据，默认格式化也必须脱敏。</summary>
    private sealed record S3ServerReply(string Host,string Address,string ContainerId,string ImageId,string ImageName,string Endpoint,string? AccessKey,string? SecretKey)
    {public override string ToString()=>"S3ServerReply { 私有服务配置 }";}
}
