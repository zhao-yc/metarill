using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;

namespace Metarill.Runtime;

public sealed class BootstrapService(DatabaseSession session,SchemaService schema,SqlRowStore store,MetadataRepository metadata,MetadataRegistry registry,ProjectPhysicalModels physicalModels)
{
    /// <summary>只能由显式初始化命令调用，普通服务启动从不执行 DDL。</summary>
    public async Task InitializeAsync(string username,string password,DatabaseOptions businessOptions,CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(username)||password.Length<12)throw new InvalidOperationException("初始化需要明确管理员账号及至少12位密码。");
        var plan=await schema.PreviewAsync(registry.Platform,cancellationToken);
        if(plan.Changes.Count>0)await schema.ApplyAsync(plan,cancellationToken);
        await metadata.SeedAsync(registry.Platform,cancellationToken);
        await metadata.SeedAsync(registry.Demo,cancellationToken);
        await session.InTransactionAsync(async () =>
        {
            if((await store.ReadAsync("yz_project",new Dictionary<string,object?>{{"id",long.Parse(registry.Platform.YzProjectId)}},cancellationToken)).Count==0)
                await store.InsertAsync("yz_project",new Dictionary<string,object?>{{"id",long.Parse(registry.Platform.YzProjectId)},{"name","Metarill"},{"yz_yzp_version_id",Metarill.Core.Metadata.TemplateVersionCatalog.CurrentValue},{"db_type",2},{"db_name",session.Options.ExpectedDatabase}},cancellationToken);
            if((await store.ReadAsync("yz_project",new Dictionary<string,object?>{{"id",long.Parse(registry.Demo.YzProjectId)}},cancellationToken)).Count==0)
                await store.InsertAsync("yz_project",new Dictionary<string,object?>{{"id",long.Parse(registry.Demo.YzProjectId)},{"name","示例项目"},{"yz_yzp_version_id",Metarill.Core.Metadata.TemplateVersionCatalog.CurrentValue},{"db_type",2},{"db_name",businessOptions.ExpectedDatabase}},cancellationToken);
            if((await store.ReadAsync("yz_role",new Dictionary<string,object?>{{"id",1L}},cancellationToken)).Count==0)
                await store.InsertAsync("yz_role",new Dictionary<string,object?>{{"id",1L},{"name","平台管理员"},{"is_super",1},{"is_built_in",1},{"yz_project_id",long.Parse(registry.Platform.YzProjectId)},{"yz_admin_type_id",long.Parse(registry.Platform.YzAdminTypes[0].Id)}},cancellationToken);
            if((await store.ReadAsync("yz_manager",new Dictionary<string,object?>{{"name",username}},cancellationToken)).Count==0)
                await store.InsertAsync("yz_manager",new Dictionary<string,object?>{{"id",1L},{"name",username},{"real_name","管理员"},{"password",PasswordHasher.Hash(password)},{"yz_role_ids","[\"1\"]"},{"is_disabled",0},{"yz_project_id",long.Parse(registry.Platform.YzProjectId)},{"yz_admin_type_id",long.Parse(registry.Platform.YzAdminTypes[0].Id)}},cancellationToken);
            if((await store.ReadAsync("yz_manager_devices",new Dictionary<string,object?>{{"id",1L}},cancellationToken)).Count==0)
                await store.InsertAsync("yz_manager_devices",new Dictionary<string,object?>{{"id",1L},{"yz_manager_id",1L},{"name","默认开发设备"},{"yz_project_id",long.Parse(registry.Platform.YzProjectId)}},cancellationToken);
            return true;
        },cancellationToken);
        using var business=new DatabaseSession(businessOptions);
        var businessSchema=new SchemaService(business);
        var businessPlan=await businessSchema.PreviewAsync(physicalModels.Compose(registry.Demo),cancellationToken);
        if(businessPlan.Changes.Count>0)await businessSchema.ApplyAsync(businessPlan,cancellationToken);
    }
}
