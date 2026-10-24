using Metarill.Core.Metadata;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Api.Tests;

/// <summary>使用同一物理测试库验证自举和物理别名，事务回滚避免改动内置配置。</summary>
public sealed partial class ApiAcceptanceTests
{
    private sealed class RollbackProbe : Exception;
    [MySqlApiFact]
    public async Task Initialization_does_not_revive_deleted_builtin_metadata()
    {
        var (factory,client)=await Login();await using var disposable=factory;using var http=client;
        var store=factory.Services.GetRequiredService<SqlRowStore>();
        var repository=factory.Services.GetRequiredService<MetadataRepository>();
        var registry=factory.Services.GetRequiredService<MetadataRegistry>();
        var field=registry.Demo.ConfigPageColumns.First();var id=long.Parse(field.Id);
        await Assert.ThrowsAsync<RollbackProbe>(()=>store.Session.InTransactionAsync(async()=>
        {
            await store.UpdateAsync("config_page_column",id,new Dictionary<string,object?>{{"delete_no",DateTime.UtcNow}});
            await repository.SeedAsync(registry.Demo);
            Assert.Empty(await store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"id",id}}));
            var records=await store.ReadIncludingDeletedAsync("config_page_column",new Dictionary<string,object?>{{"id",id}});
            Assert.Single(records);Assert.NotNull(records[0]["delete_no"]);
            await Task.FromException(new RollbackProbe());
            return true;
        }));
    }
    [MySqlApiFact]
    public async Task Page_update_switch_controls_generated_save_permission()
    {
        var (factory,client)=await Login();await using var disposable=factory;using var http=client;
        var store=factory.Services.GetRequiredService<SqlRowStore>();
        var repository=factory.Services.GetRequiredService<MetadataRepository>();
        const long id=2200000000000100020;
        await Assert.ThrowsAsync<RollbackProbe>(()=>store.Session.InTransactionAsync(async()=>
        {
            await store.UpdateAsync("config_page",id,new Dictionary<string,object?>{{"is_edit",1},{"is_update",0},{"is_save",1}});
            Assert.Equal(0,(await repository.LoadProjectAsync(Project)).ConfigPages.Single(p=>p.Id==id.ToString()).IsSave);
            await store.UpdateAsync("config_page",id,new Dictionary<string,object?>{{"is_update",1}});
            Assert.Equal(1,(await repository.LoadProjectAsync(Project)).ConfigPages.Single(p=>p.Id==id.ToString()).IsSave);
            await Task.FromException(new RollbackProbe());
            return true;
        }));
    }
}
