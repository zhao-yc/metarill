using Metarill.Core.Contracts;
using System.Globalization;
using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Metadata;

public sealed partial class MetadataWorkbenchBusiness
{
    /// <summary>只读解析与校验，不能借此执行DDL或生成代码。</summary>
    public Task<BatchColumnPreview> PreviewBatchColumnsAsync(JsonElement request,CancellationToken cancellationToken=default)
    {
        var input=BatchRequest(request);
        return InMetadataTransactionAsync(async()=>
        {
            var scope=await ReadTableScopeAsync(input.TableId,cancellationToken);
            return await PrepareColumnsAsync(scope,input.Content,cancellationToken);
        },cancellationToken);
    }
    /// <summary>专门配置领域入口保留原禁用的普通新增页；锁内重检、配置行与afterAdd整体提交。</summary>
    public Task<object> SaveBatchColumnsAsync(JsonElement request,CancellationToken cancellationToken=default)
    {
        var input=BatchRequest(request);
        return InMetadataTransactionAsync<object>(async()=>
        {
            var scope=await ReadTableScopeAsync(input.TableId,cancellationToken);
            var preview=await PrepareColumnsAsync(scope,input.Content,cancellationToken);
            if(!preview.Valid)throw new BatchColumnValidationException(preview);
            var maximum=scope.Columns.Count==0?0:scope.Columns.Max(row=>Convert.ToInt32(row.GetValueOrDefault("sort_no")??0,CultureInfo.InvariantCulture));
            if((long)maximum+10L*preview.Rows.Count>int.MaxValue){BatchColumnParser.Error(preview.Rows[^1],"sortNo","INVALID_ORDER","字段排序号已达到上限，请先调整排序");throw new BatchColumnValidationException(BatchColumnParser.Preview(preview.Rows));}
            var actor=authorization.RequireAdministrator();var added=new List<string>();
            foreach(var row in preview.Rows)
            {
                var id=ids.NextId();maximum+=10;
                var values=new Dictionary<string,object?>
                {
                    ["id"]=id,["config_table_id"]=input.TableId,["config_table_name"]=SqlRowStore.Text(scope.Table,"name"),["yz_project_id"]=long.Parse(actor.ProjectId),
                    ["name"]=row.Name,["column_name"]=row.ColumnName,["column_type"]=row.ColumnType,["logical_type"]=row.LogicalType,["column_length"]=row.ColumnType is "text" or "longtext"?row.ColumnType:row.ColumnLength,
                    ["control_kind"]=row.ControlKind,["control_detail"]=row.ControlDetail,["join_table"]=row.JoinTable,["link_config_table_id"]=row.LinkTableId,["precision"]=row.Precision,["scale"]=row.Scale,
                    ["sort_no"]=maximum,["col_span"]=1,["mobile_list_page_col_span"]=1,["default_value"]="",["insert_time"]=BusinessCalendar.Now,["update_time"]=BusinessCalendar.Now,["insert_manager_id"]=actor.AccountId,["update_manager_id"]=actor.AccountId
                };
                ConfigurationColumnDefaults.Fill(values,registry.Platform);
                await store.InsertAsync("config_column",values,cancellationToken);
                await configurationMutations.BeforeCommitAsync(new RecordMutationContext("6217715532627968","config_column",id,actor.ProjectId,values,false,store.Session.CurrentTransaction!,true),cancellationToken);
                added.Add(id.ToString(CultureInfo.InvariantCulture));
            }
            return new{savedCount=added.Count,columnIds=added,configuration="saved",database="pending",generation="pending"};
        },cancellationToken);
    }
    /// <summary>原字段工作台读取当前表配置，不能按任意客户端SQL查询。</summary>
    public Task<object> WorkbenchColumnsAsync(JsonElement request,CancellationToken cancellationToken=default)
    {
        MetadataWorkbenchRules.Object(request,"config_table_id");var tableId=MetadataWorkbenchRules.Id(request,"config_table_id");
        return InMetadataTransactionAsync<object>(async()=>
        {
            var scope=await ReadTableScopeAsync(tableId,cancellationToken);
            return scope.Columns.OrderBy(row=>Convert.ToInt32(row.GetValueOrDefault("sort_no")??0,CultureInfo.InvariantCulture)).ThenBy(row=>SqlRowStore.Id(row)).Select(row=>new{id=SqlRowStore.Text(row,"id"),name=SqlRowStore.Text(row,"name"),columnName=SqlRowStore.Text(row,"column_name"),columnType=SqlRowStore.Text(row,"column_type"),controlKind=SqlRowStore.Text(row,"control_kind")}).ToArray();
        },cancellationToken);
    }
    /// <summary>保持原表单字段名，同时严格拒绝客户端注入归属或系统字段。</summary>
    private static (long TableId,string Content) BatchRequest(JsonElement request)
    {MetadataWorkbenchRules.Object(request,"config_table_id","content");return(MetadataWorkbenchRules.Id(request,"config_table_id"),MetadataWorkbenchRules.Text(request,"content"));}
    /// <summary>既有字段与同项目关系在锁内再次读取，预检成功不是免校验保存凭据。</summary>
    private async Task<BatchColumnPreview> PrepareColumnsAsync(TableScope scope,string content,CancellationToken cancellationToken)
    {
        var rows=BatchColumnParser.Parse(SqlRowStore.Id(scope.Table),content);
        var names=scope.Columns.Select(row=>SqlRowStore.Text(row,"column_name")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actor=authorization.RequireAdministrator();
        var tables=await store.ReadForUpdateAsync("config_table",new Dictionary<string,object?>{{"yz_project_id",long.Parse(actor.ProjectId)}},cancellationToken);
        var columnCache=new Dictionary<long,List<Dictionary<string,object?>>>();
        foreach(var row in rows)
        {
            if(names.Contains(row.ColumnName))BatchColumnParser.Error(row,"columnName","ALREADY_EXISTS","数据库字段名已存在于当前表");
            if(row.ControlKind!="List"&&row.JoinTable.Length==0)continue;
            JsonElement? detail=null;
            if(row.ControlDetail.Length>0)
                try{using var parsed=JsonDocument.Parse(row.ControlDetail);if(parsed.RootElement.ValueKind!=JsonValueKind.Object)throw new JsonException();detail=parsed.RootElement.Clone();}
                catch(JsonException){BatchColumnParser.Error(row,"controlDetail","INVALID_RELATION","关联控件明细必须是JSON对象");continue;}
            string Value(string key)=>detail is{}element&&element.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()!:"";
            var targetName=row.JoinTable.Length>0?row.JoinTable:Value("table");var targetId=Value("tableId");
            var target=targetId.Length>0?tables.SingleOrDefault(table=>SqlRowStore.Text(table,"id")==targetId):tables.FirstOrDefault(table=>SqlRowStore.Text(table,"table_name")==targetName);
            if(target is null||(targetName.Length>0&&SqlRowStore.Text(target,"table_name")!=targetName)){BatchColumnParser.Error(row,"controlDetail","INVALID_RELATION","关联表必须存在且属于当前项目");continue;}
            var nameField=Value("nameField");if(nameField.Length==0)nameField="name";
            var id=SqlRowStore.Id(target);
            if(!columnCache.TryGetValue(id,out var columns)){columns=await store.ReadForUpdateAsync("config_column",new Dictionary<string,object?>{{"config_table_id",id}},cancellationToken);columnCache[id]=columns;}
            if(!columns.Any(column=>SqlRowStore.Text(column,"column_name")==nameField&&SqlRowStore.Text(column,"yz_project_id")==actor.ProjectId)){BatchColumnParser.Error(row,"controlDetail","INVALID_RELATION","关联显示字段不存在于当前项目目标表");continue;}
            row.LinkTableId=id;row.JoinTable=SqlRowStore.Text(target,"table_name");
            if(row.ControlKind=="List")
            {
                row.LogicalType="int64";row.ColumnType="bigint";row.ColumnLength="";
                row.ControlDetail=JsonSerializer.Serialize(new{table=row.JoinTable,tableId=id.ToString(CultureInfo.InvariantCulture),nameField,whereSql=Value("whereSql"),orderBy=Value("orderBy")},JsonDefaults.Options);
            }
        }
        return BatchColumnParser.Preview(rows);
    }
}
