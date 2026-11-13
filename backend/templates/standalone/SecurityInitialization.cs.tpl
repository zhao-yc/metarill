using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;

namespace {{NAMESPACE}}.Host;

/// <summary>只由显式命令调用；不执行DDL、页面种子或既有凭据重置。</summary>
internal sealed class ApplicationSecurityInitialization(SqlRowStore store,IIdGenerator ids,MetadataCatalog metadata)
{
    internal async Task<string> InitializeAsync(IConfiguration configuration,CancellationToken cancellationToken)
    {
        var username=configuration["Authentication:Bootstrap:Username"];var password=configuration["Authentication:Bootstrap:Password"];
        if(string.IsNullOrWhiteSpace(username)||username.Length>50||password is null||password.Length is <12 or >1024)throw new InvalidOperationException("安全初始化需要私有配置中的用户名与12至1024位密码；不接受命令行凭据。");
        var adminText=configuration["Authentication:Bootstrap:AdminTypeId"];
        if(string.IsNullOrWhiteSpace(adminText)&&metadata.YzAdminTypes.Count==1)adminText=metadata.YzAdminTypes[0].Id;
        var definition=metadata.YzAdminTypes.SingleOrDefault(item=>item.Id==adminText&&item.YzProjectId==metadata.YzProjectId)??throw new InvalidOperationException("安全初始化必须明确选择包内后台，不能由请求指定项目或后台。");
        if(!long.TryParse(metadata.YzProjectId,NumberStyles.None,CultureInfo.InvariantCulture,out var project)||project<=0||!long.TryParse(definition.Id,NumberStyles.None,CultureInfo.InvariantCulture,out var admin)||admin<=0)throw new InvalidOperationException("包内项目或后台标识无效。");
        return await store.Session.InTransactionAsync(async()=>
        {
            // 固定项目主键串行化同应用的并发初始化；缺失时并发INSERT冲突直接回滚，不自动重试。
            await store.ReadForUpdateAsync("yz_project",new Dictionary<string,object?>{{"id",project}},cancellationToken);
            var projectRows=await store.ReadIncludingDeletedAsync("yz_project",new Dictionary<string,object?>{{"id",project}},cancellationToken);
            if(projectRows.Count>1||projectRows.Any(row=>row.GetValueOrDefault("delete_no") is not null))throw Conflict();
            if(projectRows.Count==0)await store.InsertAsync("yz_project",new Dictionary<string,object?>{{"id",project},{"name",metadata.YzProjectName},{"yz_yzp_version_id",TemplateVersionCatalog.CurrentValue},{"insert_time",DateTime.UtcNow}},cancellationToken);
            if((await store.ReadForUpdateAsync("yz_project",new Dictionary<string,object?>{{"id",project}},cancellationToken)).Count!=1)throw Conflict();
            var admins=await store.ReadIncludingDeletedAsync("yz_admin_type",new Dictionary<string,object?>{{"id",admin}},cancellationToken);
            if(admins.Count>1||admins.Any(row=>row.GetValueOrDefault("delete_no") is not null||SqlRowStore.Id(row,"yz_project_id")!=project))throw Conflict();
            if(admins.Count==0)await store.InsertAsync("yz_admin_type",new Dictionary<string,object?>{{"id",admin},{"name",definition.Name},{"yz_project_id",project},{"insert_time",DateTime.UtcNow}},cancellationToken);
            var accounts=await store.ReadIncludingDeletedAsync("yz_manager",new Dictionary<string,object?>{{"name",username}},cancellationToken);
            if(accounts.Count!=0)
            {
                if(accounts.Count!=1||accounts[0].GetValueOrDefault("delete_no") is not null||!AuthenticationService.AccountActive(accounts[0])||SqlRowStore.Id(accounts[0],"yz_admin_type_id")!=admin)throw Conflict();
                var roles=await store.ReadAsync("yz_role",new Dictionary<string,object?>{{"yz_admin_type_id",admin}},cancellationToken);
                // 既有账号只能证明已经完成初始化，不能因重跑命令重新挂角色、复活或恢复配置密码。
                var roleIds=RoleIds(SqlRowStore.Text(accounts[0],"yz_role_ids"));
                if(!roles.Any(row=>roleIds.Contains(SqlRowStore.Text(row,"id"))&&SqlRowStore.Text(row,"is_super")=="1"&&SqlRowStore.Text(row,"is_built_in")=="1"))throw Conflict();
                return "unchanged";
            }
            var role=ids.NextId();var manager=ids.NextId();if(role<=1||manager<=1||role==manager)throw new InvalidOperationException("初始化必须生成新的独立账号和角色标识。");
            await store.InsertAsync("yz_role",new Dictionary<string,object?>{{"id",role},{"name","应用管理员"},{"is_super",1},{"is_built_in",1},{"yz_admin_type_id",admin},{"yz_project_id",project},{"insert_time",DateTime.UtcNow}},cancellationToken);
            await store.InsertAsync("yz_manager",new Dictionary<string,object?>{{"id",manager},{"name",username},{"real_name","应用管理员"},{"password",PasswordHasher.Hash(password)},{"is_disabled",0},{"yz_admin_type_id",admin},{"yz_project_id",project},{"yz_role_ids",JsonSerializer.Serialize(new[]{role.ToString(CultureInfo.InvariantCulture)})},{"insert_time",DateTime.UtcNow}},cancellationToken);
            return "initialized";
        },cancellationToken);
    }
    /// <summary>只验证已有规范字符串ID，不把损坏或浮点集合转成初始管理员授权。</summary>
    private static HashSet<string> RoleIds(string encoded)
    {
        try{using var json=JsonDocument.Parse(encoded);if(json.RootElement.ValueKind!=JsonValueKind.Array||json.RootElement.GetArrayLength()>1000)return [];
            var values=json.RootElement.EnumerateArray().ToArray();if(values.Any(value=>value.ValueKind!=JsonValueKind.String||!long.TryParse(value.GetString(),NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0||id.ToString(CultureInfo.InvariantCulture)!=value.GetString()))return [];
            return values.Select(value=>value.GetString()!).ToHashSet(StringComparer.Ordinal);
        }catch(JsonException){return [];}
    }
    private static InvalidOperationException Conflict()=>new("安全初始化发现既有、删除或归属冲突记录，未重置凭据、复活账号或补绑角色。");
}
