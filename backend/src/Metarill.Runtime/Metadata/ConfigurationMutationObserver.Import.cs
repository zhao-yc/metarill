using Metarill.Core.Persistence;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Metadata;

public sealed partial class ConfigurationMutationObserver
{
    /// <summary>明确的源结构导入入口：只复用对象校验，不执行普通新增表的补列/建页面联动。</summary>
    internal async Task ValidateImportedTableAsync(long tableId, long project, CancellationToken token)
    {
        var transaction = store.Session.CurrentTransaction ?? throw new InvalidOperationException("导入校验必须加入配置事务。");
        await LockProjectAsync(project, token);
        var table = await OwnedRowAsync("config_table", tableId, project, token);
        await ValidateAndNormalize(new("", "config_table", tableId, project.ToString(System.Globalization.CultureInfo.InvariantCulture), table, false, transaction, true), project, token);
        foreach (var column in await store.ReadForUpdateAsync("config_column", new Dictionary<string,object?> { ["config_table_id"] = tableId }, token))
        {
            RequireProject(column, project);
            await ValidateAndNormalize(new("", "config_column", SqlRowStore.Id(column), project.ToString(System.Globalization.CultureInfo.InvariantCulture), column, false, transaction, true), project, token);
        }
        if ((await store.ReadAsync("config_page", new Dictionary<string,object?> { ["config_table_id"] = tableId }, token)).Count != 0)
            throw new InvalidOperationException("配置导入不会隐式创建或覆盖页面。");
    }
}
