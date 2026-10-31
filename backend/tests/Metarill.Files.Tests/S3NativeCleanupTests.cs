using Xunit;

namespace Metarill.Files.Tests;

/// <summary>只执行真实清理协调器的内存回调，核对未知/晚笔失败时不会误删或宣称核零。</summary>
public sealed class S3NativeCleanupTests
{
    /// <summary>每种失败都保留其准确阶段；特别是晚笔摘要失败时前一个合法对象也不能删除。</summary>
    [Theory]
    [InlineData("unknown")][InlineData("duplicate")][InlineData("truncated")][InlineData("lateHash")]
    [InlineData("leftover")][InlineData("notAbsent")][InlineData("good")]
    public async Task Cleanup_verifies_every_object_before_delete_and_requires_independent_zero(string scenario)
    {
        var assets=new[]{new S3NativeAsset("a",[new(1,new string('a',64))]),new S3NativeAsset("b",[new(1,new string('b',64))])};
        var events=new List<string>();var lists=0;var deleted=new List<string>();var bucketDeleted=false;
        Task<S3NativeInventory> List()
        {
            events.Add("list");lists++;
            if(lists>1)return Task.FromResult(new S3NativeInventory(true,scenario=="leftover"?["foreign"]:[]));
            return Task.FromResult(new S3NativeInventory(scenario!="truncated",scenario=="unknown"?["a","foreign"]:scenario=="duplicate"?["a","a"]:["a","b"]));
        }
        Task Verify(S3NativeAsset asset){events.Add("verify-"+asset.Key);if(scenario=="lateHash"&&asset.Key=="b")throw new InvalidOperationException("合成晚笔摘要不匹配");return Task.CompletedTask;}
        Task Delete(string key){events.Add("delete-"+key);deleted.Add(key);return Task.CompletedTask;}
        Task Bucket(){events.Add("delete-bucket");bucketDeleted=true;return Task.CompletedTask;}
        Task<bool> Absent(){events.Add("independent-head");return Task.FromResult(scenario!="notAbsent");}
        var operation=S3NativeCleanup.ApplyAsync(assets,List,Verify,Delete,Bucket,Absent);
        if(scenario=="good")await operation;else await Assert.ThrowsAsync<InvalidOperationException>(()=>operation);
        if(scenario is "unknown" or "duplicate" or "truncated" or "lateHash")Assert.Empty(deleted);
        if(scenario is "unknown" or "duplicate" or "truncated" or "lateHash" or "leftover")Assert.False(bucketDeleted);
        if(scenario=="good")Assert.Equal(new[]{"list","verify-a","verify-b","delete-a","delete-b","list","delete-bucket","independent-head"},events);
    }
}
