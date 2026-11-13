using System.Data.Common;
using System.Globalization;
using Metarill.Core.Metadata;
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
        // 显式初始化与平台仓储共用完整物理图，包含登录审计等未进入种子目录的基础设施。
        var plan=await schema.PreviewAsync(physicalModels.Compose(registry.Platform),cancellationToken);
        if(plan.Changes.Count>0)await schema.ApplyAsync(plan,cancellationToken);
        await metadata.SeedAsync(registry.Platform,cancellationToken);
        await metadata.SeedAsync(registry.Demo,cancellationToken);
        await session.InTransactionAsync(async () =>
        {
            await SeedProjectAsync(registry.Platform,"Metarill","metarill","metarill.platform",session.Options.ExpectedDatabase,cancellationToken);
            await SeedProjectAsync(registry.Demo,"示例项目","demo","metarill.demo",businessOptions.ExpectedDatabase,cancellationToken);
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

    /// <summary>首次播种填齐原详情表单；重复初始化尊重已存在的记录及墓碑。</summary>
    private async Task SeedProjectAsync(MetadataCatalog catalog,string name,string shortName,string packageName,string database,CancellationToken cancellationToken)
    {
        var id=long.Parse(catalog.YzProjectId,CultureInfo.InvariantCulture);
        var filters=new Dictionary<string,object?> { ["id"]=id };
        if((await store.ReadIncludingDeletedAsync("yz_project",filters,cancellationToken)).Count!=0)return;

        // 原连接栏保存可编辑的数据库引用。真实服务连接及凭据始终来自服务端配置。
        var databaseReference=new DbConnectionStringBuilder { ["Database"]=database }.ConnectionString;
        var primaryColor=string.IsNullOrWhiteSpace(catalog.PrimaryColor)?"#0ab3ad":catalog.PrimaryColor;
        await store.InsertAsync("yz_project",new Dictionary<string,object?>
        {
            ["id"]=id,
            ["name"]=name,
            ["mysql_conn_str"]=databaseReference,
            ["short_name"]=shortName,
            ["db_name"]=database,
            ["context_root"]="/api",
            ["eclipse_base_name"]=packageName,
            ["yz_yzp_version_id"]=TemplateVersionCatalog.CurrentValue,
            ["db_type"]=2,
            ["primary_color"]=primaryColor
        },cancellationToken);
    }
}
