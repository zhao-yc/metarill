using Metarill.Core.Contracts;
using System.Globalization;
using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Metadata;

public sealed partial class MetadataWorkbenchBusiness
{
    /// <summary>原添加联接字段页的候选响应；仅查询配置，不查询业务数据或执行DDL。</summary>
    public Task<JoinColumnCandidates> JoinColumnCandidatesAsync(JsonElement request,CancellationToken token=default)
    {
        MetadataWorkbenchRules.Object(request,"configTableId");var table=MetadataWorkbenchRules.Id(request,"configTableId");RequireJoinWorkbenchWeb();
        return InMetadataTransactionAsync(async()=> (await ReadJoinScopeAsync(table,token)).Snapshot.Detail,token);
    }
    /// <summary>项目锁内重算候选、先校验整批、再通过统一元数据联动保存；不自动同步数据库或生成。</summary>
    public Task<object> SaveJoinColumnsAsync(JsonElement request,CancellationToken token=default)
    {
        MetadataWorkbenchRules.Object(request,"configTableId","selections");var table=MetadataWorkbenchRules.Id(request,"configTableId");if(!request.TryGetProperty("selections",out var selections))throw new InvalidOperationException("请选择联接字段。");RequireJoinWorkbenchWeb();
        return InMetadataTransactionAsync<object>(async()=>
        {
            var state=await ReadJoinScopeAsync(table,token);var plans=JoinColumnWorkbenchRules.Select(selections,state.Snapshot);var maximum=state.Scope.Columns.Select(row=>Convert.ToInt32(row.GetValueOrDefault("sort_no")??0,CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();
            if((long)maximum+10L*plans.Count>int.MaxValue)throw new InvalidOperationException("字段排序号已达到上限，请先调整排序。");
            var actor=authorization.RequireAdministrator();var created=new List<Dictionary<string,object?>>();
            foreach(var plan in plans)
            {
                maximum+=10;var id=ids.NextId();var values=new Dictionary<string,object?>
                {
                    ["id"]=id,["config_table_id"]=table,["config_table_name"]=SqlRowStore.Text(state.Scope.Table,"name"),["yz_project_id"]=long.Parse(actor.ProjectId,CultureInfo.InvariantCulture),
                    ["name"]=string.IsNullOrWhiteSpace(plan.Field.Name)?plan.Field.ColumnName:plan.Field.Name,["column_name"]=plan.Field.JoinColumnName,["column_full_name"]=plan.Field.ColumnFullName,["join_table"]=plan.Field.JoinTable,
                    ["is_join_column"]=1,["is_virtual_column"]=0,["col_span"]=1,["mobile_list_page_col_span"]=1,["sort_no"]=maximum,["is_copy"]="0",["is_db_not_null"]=0,
                    ["default_value"]="",["query_default_value"]="",["init_value"]="",["match_type"]="",["width"]="",["align"]="",["visible_condition"]="",["column_constrain"]="",["save_option"]="",["display_option"]="",["trim_option"]="",["description"]="",["developer_remarks"]="",["filter_control_kind"]="",
                    ["insert_time"]=BusinessCalendar.Now,["update_time"]=BusinessCalendar.Now,["insert_manager_id"]=actor.AccountId,["update_manager_id"]=actor.AccountId
                };
                foreach(var key in new[]{"column_type","column_length","logical_type","control_kind","control_detail"})values[key]=SqlRowStore.Text(plan.Target,key);
                foreach(var key in new[]{"precision","scale"})values[key]=plan.Target.GetValueOrDefault(key);
                if(string.IsNullOrWhiteSpace(Convert.ToString(values["column_type"],CultureInfo.InvariantCulture)))values["column_type"]="nvarchar";
                foreach(var key in new[]{"is_auto_post_back","is_auto_post_back_query","is_hide_select_grid","is_auto_update","is_sort"})values[key]=0;
                ConfigurationColumnDefaults.Fill(values,registry.Platform);await store.InsertAsync("config_column",values,token);
                await configurationMutations.BeforeCommitAsync(new RecordMutationContext("6217715532627968","config_column",id,actor.ProjectId,values,false,store.Session.CurrentTransaction!,true),token);
                var row=(await store.ReadAsync("config_column",new Dictionary<string,object?>{{"id",id}},token)).Single();created.Add(row.ToDictionary(pair=>MetadataRegistry.Camel(pair.Key),pair=>pair.Key=="id"||pair.Key.EndsWith("_id",StringComparison.Ordinal)?pair.Value is null?null:Convert.ToString(pair.Value,CultureInfo.InvariantCulture):pair.Value));
            }
            return new{createdCount=created.Count,createdColumns=created};
        },token);
    }
    /// <summary>只接受真实网页登录管理员，设备授权与所选项目不能成为配置写入资格。</summary>
    private void RequireJoinWorkbenchWeb(){if(authorization.RequireIdentity().GrantId is not null)throw new UnauthorizedAccessException("联接字段工作台需要网页登录管理员。");authorization.RequireAdministrator();}
    /// <summary>配置聚合沿既有项目锁串行；候选表、页和列均限定到同一真实项目。</summary>
    private async Task<(TableScope Scope,JoinColumnSnapshot Snapshot)> ReadJoinScopeAsync(long table,CancellationToken token)
    {
        var scope=await ReadTableScopeAsync(table,token);var project=MetadataWorkbenchRules.ParseId(authorization.RequireActor().ProjectId);var filter=new Dictionary<string,object?>{{"yz_project_id",project}};
        var tables=await store.ReadForUpdateAsync("config_table",filter,token);var pages=await store.ReadForUpdateAsync("config_page",filter,token);var columns=await store.ReadForUpdateAsync("config_column",filter,token);
        return(scope,JoinColumnWorkbenchRules.Build(scope.Table,tables,columns,pages));
    }
}
