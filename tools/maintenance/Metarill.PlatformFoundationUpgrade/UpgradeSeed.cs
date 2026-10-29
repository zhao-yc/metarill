using System.Globalization;
using System.Text.Json;
using Metarill.Data;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
namespace Metarill.Tools.PlatformFoundationUpgrade;

/// <summary>窄配置补缺复用真实SqlRowStore/MetadataRepository；不生成或修改实体/Mapper/Service。</summary>
internal static class UpgradeSeed
{
    /// <summary>平台首锁与十三条全局保留锁后才插入；写后逐字段确认仍在同一事务，任何失败整笔回滚。</summary>
    internal static Task ApplyAsync(DatabaseSession session,UpgradeDefinition definition,string uuid,CancellationToken token)
    {
        var store=new SqlRowStore(session,definition.Registry.Platform);var repository=new MetadataRepository(store,definition.Registry);
        return session.InTransactionAsync(async()=>
        {
            await RequireIdentityAsync(session,uuid,token);await RequireProjectAsync(store,definition,true,token);
            foreach(var key in UpgradeDefinition.Keys.OrderBy(key=>key.Table,StringComparer.Ordinal).ThenBy(key=>long.Parse(key.Id,CultureInfo.InvariantCulture)))
                if((await store.ReadConfigurationSeedForUpdateAsync(key.Table,long.Parse(key.Id,CultureInfo.InvariantCulture),token)).Count!=0)throw new InvalidOperationException("结构阶段之后固定标识被占用，配置未覆盖。");
            await repository.SeedAsync(definition.Patch,token);await RequireRowsAsync(store,definition,token);return true;
        },token);
    }
    /// <summary>项目记录必须仍有效；包含墓碑检查，禁止把删除记录作为新平台复活。</summary>
    internal static async Task RequireProjectAsync(SqlRowStore store,UpgradeDefinition definition,bool locked,CancellationToken token)
    {
        var id=long.Parse(definition.Registry.Platform.YzProjectId,CultureInfo.InvariantCulture);
        var rows=locked?await store.ReadInitializationScopeForUpdateAsync("yz_project",id,token):await store.ReadIncludingDeletedAsync("yz_project",new Dictionary<string,object?>{{"id",id}},token);
        if(rows.Count!=1||rows[0].GetValueOrDefault("delete_no") is not null)throw new InvalidOperationException("有效平台项目不存在，停止升级。");
    }
    /// <summary>每次实际事务读取UUID及库名，不根据连接字符串猜测物理服务器。</summary>
    internal static async Task RequireIdentityAsync(DatabaseSession session,string uuid,CancellationToken token)
    {
        var identity=await session.Provider.ReadPhysicalDatabaseIdentityAsync(session.Options,session.CurrentTransaction??throw new InvalidOperationException("缺少身份核验事务。"),token);
        if(identity.ServerId!=uuid||identity.Database!="metarill_dev")throw new InvalidOperationException("数据库物理身份与审查范围不符。");
    }
    /// <summary>十三条只能有唯一有效记录，源中真实物理字段全部匹配；动态审计时间由原种子写入。</summary>
    internal static async Task<int> RequireRowsAsync(SqlRowStore store,UpgradeDefinition definition,CancellationToken token)
    {
        foreach(var key in UpgradeDefinition.Keys)
        {
            var rows=await store.ReadIncludingDeletedAsync(key.Table,new Dictionary<string,object?>{{"id",long.Parse(key.Id,CultureInfo.InvariantCulture)}},token);
            if(rows.Count!=1||rows[0].GetValueOrDefault("delete_no") is not null)throw new InvalidOperationException("升级元数据缺失或已删除。");
            foreach(var field in definition.StoredFields(key.Table,key.Id))
                if(Value(rows[0].GetValueOrDefault(field.Key))!=Value(field.Value))throw new InvalidOperationException("升级元数据内容与已审源不符："+key.Table+"/"+key.Id+"/"+field.Key+"；源类型="+field.Value.ValueKind+"，实际类型="+(rows[0].GetValueOrDefault(field.Key)?.GetType().Name??"null"));
        }
        return UpgradeDefinition.Keys.Length;
    }
    /// <summary>源JSON字符串ID与驱动整数按无损文本比较，null和空串不合并。</summary>
    private static string? Value(object? value)=>value switch{null=>null,JsonElement element=>element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined?null:element.ToString(),IFormattable number=>number.ToString(null,CultureInfo.InvariantCulture),_=>value.ToString()};
}
