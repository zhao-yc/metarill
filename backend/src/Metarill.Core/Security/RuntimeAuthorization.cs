using System.Data.Common;

namespace Metarill.Core.Security;

/// <summary>HTTP、Agent 与生成控制器共用的真实服务端身份。</summary>
public sealed record RuntimeActor(long AccountId, string AccountName, bool IsAdministrator, string ProjectId, string AdminTypeId);

public interface IRuntimeAuthorization
{
    RuntimeActor RequireActor();
    /// <summary>仅真实宿主证明当前身份同项目、同后台的超级角色；旧授权器缺省不提供此特权。</summary>
    bool IsCurrentRealmSuper() => false;
    void RequirePagePermission(string pageId, string operation);
    /// <summary>业务按钮是独立资源；旧宿主缺省拒绝，不能退化为页面query/update权限。</summary>
    void RequireButtonPermission(string pageId,string buttonId)=>throw new NotSupportedException("当前授权器尚未接入业务按钮授权。");
    /// <summary>锁记录和实际更新前必须使用同一事务重新证明当前按钮与范围；旧宿主不能缺省放行。</summary>
    Task RequireButtonCurrentAsync(string pageId,string buttonId,RuntimeActor actor,DbTransaction transaction,CancellationToken cancellationToken=default)
        =>throw new NotSupportedException("当前授权器尚未接入按钮锁后当前权限。");
    /// <summary>按钮文件写入只在当前事务证明之后执行回调，旧宿主不能借请求快照放行。</summary>
    Task WithButtonFileCurrentAsync(string pageId,string buttonId,RuntimeActor actor,DbTransaction transaction,Func<Task> action,CancellationToken cancellationToken=default)
        =>throw new NotSupportedException("当前授权器尚未接入按钮文件来源当前权限。");
    /// <summary>按固定按钮授权解析记录范围；不能借其它页面动作返回无限制。</summary>
    ExactDataManageValues ResolveButtonExactDataManage(string pageId,string buttonId,string managerField)=>throw new NotSupportedException("当前授权器尚未接入业务按钮数据范围。");
    /// <summary>文件只接受直接/真实父子来源；旧授权器保持原直接查询合同，新继承宿主必须显式覆盖。</summary>
    void RequireFileSourcePagePermission(string pageId)=>RequirePagePermission(pageId,"detail");
    /// <summary>文件的范围来源独立于选择页查询，旧授权器兼容原直接页解析。</summary>
    ExactDataManageValues ResolveFileSourceExactDataManage(string pageId,string managerField)=>ResolveExactDataManage(pageId,"detail",managerField);
    /// <summary>配置了精确范围的生成入口必须由真实宿主提供决策；旧实现不能缺省声明无限制。</summary>
    ExactDataManageValues ResolveExactDataManage(string pageId,string operation,string managerField)=>throw new NotSupportedException("当前授权器尚未接入精确数据管理。");
}
public interface IIdGenerator { long NextId(); }

/// <summary>应用内单调雪花标识；时钟倒退拒绝生成，节点号由部署配置固定。</summary>
public sealed class SnowflakeIdGenerator(int workerId = 1) : IIdGenerator
{
    private readonly object sync = new();
    private long lastTime = -1;
    private long sequence;
    public long NextId()
    {
        lock (sync)
        {
            if (workerId < 0 || workerId > 1023) throw new InvalidOperationException("节点号必须介于 0 与 1023。");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1577836800000L;
            if (timestamp < lastTime) throw new InvalidOperationException("系统时钟回退，请校准后重试。");
            sequence = timestamp == lastTime ? (sequence + 1) & 4095 : 0;
            if (sequence == 0 && timestamp == lastTime)
            {
                do { timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1577836800000L; } while (timestamp <= lastTime);
            }
            lastTime = timestamp;
            return (timestamp << 22) | ((long)workerId << 12) | sequence;
        }
    }
}
