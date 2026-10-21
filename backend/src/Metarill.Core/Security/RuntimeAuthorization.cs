namespace Metarill.Core.Security;

/// <summary>HTTP、Agent 与生成控制器共用的真实服务端身份。</summary>
public sealed record RuntimeActor(long AccountId, string AccountName, bool IsAdministrator, string ProjectId, string AdminTypeId);

public interface IRuntimeAuthorization
{
    RuntimeActor RequireActor();
    /// <summary>仅真实宿主证明当前身份同项目、同后台的超级角色；旧授权器缺省不提供此特权。</summary>
    bool IsCurrentRealmSuper() => false;
    void RequirePagePermission(string pageId, string operation);
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
