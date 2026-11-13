namespace Metarill.Files.Tests;

/// <summary>全桶清单必须完整；核对全部字节之后才能进入删除阶段。</summary>
internal sealed record S3NativeInventory(bool Complete,IReadOnlyList<string> Keys);

/// <summary>真实清理及离线护栏共用同一执行顺序，不能先删合法子集再发现未知对象。</summary>
internal static class S3NativeCleanup
{
    internal static async Task ApplyAsync(IReadOnlyList<S3NativeAsset> assets,Func<Task<S3NativeInventory>> list,
        Func<S3NativeAsset,Task> verify,Func<string,Task> delete,Func<Task> deleteBucket,Func<Task<bool>> independentlyAbsent)
    {
        var inventory=await list();
        if(!inventory.Complete||inventory.Keys.Distinct(StringComparer.Ordinal).Count()!=inventory.Keys.Count||
            inventory.Keys.Any(key=>assets.All(asset=>asset.Key!=key)))throw new InvalidOperationException("S3清理清单不完整或含未知对象。");
        foreach(var key in inventory.Keys)await verify(assets.Single(asset=>asset.Key==key));
        foreach(var key in inventory.Keys)await delete(key);
        var empty=await list();if(!empty.Complete||empty.Keys.Count!=0)throw new InvalidOperationException("S3桶未清空，拒绝删桶。");
        await deleteBucket();
        if(!await independentlyAbsent())throw new InvalidOperationException("独立连接未能确认测试桶不存在。");
    }
}
