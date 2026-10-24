using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Data.Schema;
using Metarill.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Api.Tests;

/// <summary>原联接字段接口的真实网页登录、事务及元数据联动；不创建业务物理表或生成页面。</summary>
public sealed class JoinColumnWorkbenchMySqlTests
{
    private const string Candidates="/admin/custom/config_table/join_column_candidates",Save="/admin/custom/config_table/save_join_columns";
    /// <summary>原请求读取候选并批量保存，自动页面字段与原完整路径/类型保持，重复保存不产生额外记录。</summary>
    [MySqlApiFact]
    public async Task Original_json_routes_save_join_metadata_and_page_fields_in_one_transaction()
    {
        await using var f=await MaterializedDerivedApiFixture.CreateAsync();var source=await f.CreateTable("join_source");var target=await f.CreateTable("join_target");var targetFields=await f.AddColumns(target,"备注 notes\n代码 code");var name=SqlRowStore.Text(await f.Row("config_table",target),"table_name");
        var relation=Assert.Single(await f.AddColumns(source,JsonSerializer.Serialize(new[]{new{name="客户",columnName=name+"_id",columnType="bigint",columnLength="",controlKind="List",controlDetail=JsonSerializer.Serialize(new{table=name,tableId=target.ToString(CultureInfo.InvariantCulture),nameField="name",whereSql="id > 0",orderBy="id desc"})}})));
        var beforeSchema=(await new SchemaService(f.Session).InspectSnapshotAsync(f.Registry.Platform)).Fingerprint;
        var result=MaterializedDerivedApiFixture.Data(await f.Post(Candidates,new{configTableId=source.ToString(CultureInfo.InvariantCulture)}));var group=result.GetProperty("groups").EnumerateArray().Single(item=>item.GetProperty("sourceColumnId").GetString()==relation.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(target.ToString(CultureInfo.InvariantCulture),group.GetProperty("targetTableId").GetString());Assert.Equal(new[]{"yz_project_id","notes","code"},group.GetProperty("fields").EnumerateArray().Select(item=>item.GetProperty("columnName").GetString()));
        var selections=new[]{new{sourceColumnId=relation.ToString(CultureInfo.InvariantCulture),targetColumnId=targetFields[0].ToString(CultureInfo.InvariantCulture)},new{sourceColumnId=relation.ToString(CultureInfo.InvariantCulture),targetColumnId=targetFields[1].ToString(CultureInfo.InvariantCulture)}};
        var saved=MaterializedDerivedApiFixture.Data(await f.Post(Save,new{configTableId=source.ToString(CultureInfo.InvariantCulture),selections}));await f.Capture();Assert.Equal(2,saved.GetProperty("createdCount").GetInt32());
        var ids=saved.GetProperty("createdColumns").EnumerateArray().Select(MaterializedDerivedApiFixture.Id).ToArray();Assert.Equal(2,ids.Length);
        for(var i=0;i<ids.Length;i++)
        {
            var row=await f.Row("config_column",ids[i]);var fieldName=i==0?"notes":"code";Assert.Equal(name+"_"+fieldName,SqlRowStore.Text(row,"column_name"));Assert.Equal(name+"."+fieldName,SqlRowStore.Text(row,"column_full_name"));Assert.Equal("1",SqlRowStore.Text(row,"is_join_column"));Assert.Equal("0",SqlRowStore.Text(row,"is_virtual_column"));
            var mappings=await f.Store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_column_id",ids[i]}});Assert.NotEmpty(mappings);
            foreach(var mapping in mappings){Assert.Equal(name+"_"+fieldName,SqlRowStore.Text(mapping,"config_column_column_name"));Assert.Equal(SqlRowStore.Text(row,"sort_no"),SqlRowStore.Text(mapping,"config_column_sort_no"));Assert.Equal(f.Project.ToString(CultureInfo.InvariantCulture),SqlRowStore.Text(mapping,"yz_project_id"));}
        }
        var before=await f.Snapshot();Assert.NotEqual(200,(await f.Post(Save,new{configTableId=source.ToString(CultureInfo.InvariantCulture),selections})).GetProperty("code").GetInt32());Assert.Equal(before,await f.Snapshot());
        var refreshed=MaterializedDerivedApiFixture.Data(await f.Post(Candidates,new{configTableId=source.ToString(CultureInfo.InvariantCulture)}));Assert.Equal(new[]{"yz_project_id"},refreshed.GetProperty("groups").EnumerateArray().Single(item=>item.GetProperty("sourceColumnId").GetString()==relation.ToString(CultureInfo.InvariantCulture)).GetProperty("fields").EnumerateArray().Select(item=>item.GetProperty("columnName").GetString()));
        Assert.Equal(beforeSchema,(await new SchemaService(f.Session).InspectSnapshotAsync(f.Registry.Platform)).Fingerprint);await f.SaveEvidence("join-interface-verified",new{source,target,relation,createdIds=ids,metadataOnly=true,unchangedSchema=true,foreignSelectionsRejected=false});await f.CompleteAsync();
    }
    /// <summary>任一非法选择、重复键和外项目表拒绝，已验证的前一条选择不能产生部分元数据或页面字段。</summary>
    [MySqlApiFact]
    public async Task Invalid_or_foreign_join_selections_do_not_partially_write_configuration()
    {
        await using var f=await MaterializedDerivedApiFixture.CreateAsync();var source=await f.CreateTable("join_invalid_source");var target=await f.CreateTable("join_invalid_target");var targetField=Assert.Single(await f.AddColumns(target,"备注 notes"));var foreign=await f.ForeignTable();var tableName=SqlRowStore.Text(await f.Row("config_table",target),"table_name");
        var relation=Assert.Single(await f.AddColumns(source,JsonSerializer.Serialize(new[]{new{name="关联",columnName=tableName+"_id",columnType="bigint",controlKind="List",controlDetail=JsonSerializer.Serialize(new{table=tableName,tableId=target.ToString(CultureInfo.InvariantCulture),nameField="name"})}})));
        var before=await f.Snapshot();
        foreach(var selections in new object[]{new[]{new{sourceColumnId=relation.ToString(),targetColumnId=targetField.ToString()},new{sourceColumnId=relation.ToString(),targetColumnId="999999999999"}},new[]{new{sourceColumnId=relation.ToString(),targetColumnId=targetField.ToString()},new{sourceColumnId=relation.ToString(),targetColumnId=targetField.ToString()}}})
        {Assert.NotEqual(200,(await f.Post(Save,new{configTableId=source.ToString(),selections})).GetProperty("code").GetInt32());Assert.Equal(before,await f.Snapshot());}
        Assert.NotEqual(200,(await f.Post(Candidates,new{configTableId=foreign.ToString()})).GetProperty("code").GetInt32());Assert.Equal(before,await f.Snapshot());
        using(var request=new StringContent("{\"configTableId\":\""+source+"\",\"configTableId\":\""+foreign+"\"}",System.Text.Encoding.UTF8,"application/json"))
        using(var response=await f.Client.PostAsync("/api"+Candidates,request))Assert.NotEqual(200,(await MaterializedDerivedApiFixture.Body(response)).GetProperty("code").GetInt32());
        Assert.Equal(before,await f.Snapshot());await f.SaveEvidence("join-invalid-verified",new{source,target,relation,atomicRejection=true,foreignTableRejected=true,duplicateJsonRejected=true});await f.CompleteAsync();
    }
    /// <summary>第一列和其页面映射已写后，在第二列分配ID时故障，真实MySQL事务必须回滚整个批次。</summary>
    [MySqlApiFact]
    public async Task Tail_failure_rolls_back_first_join_column_and_its_automatic_page_fields()
    {
        var ids=new TailFailureIds();await using var f=await MaterializedDerivedApiFixture.CreateAsync(services=>services.Replace(ServiceDescriptor.Singleton<IIdGenerator>(ids)));
        var source=await f.CreateTable("join_rollback_source");var target=await f.CreateTable("join_rollback_target");var fields=await f.AddColumns(target,"备注 notes\n代码 code");var name=SqlRowStore.Text(await f.Row("config_table",target),"table_name");
        var relation=Assert.Single(await f.AddColumns(source,JsonSerializer.Serialize(new[]{new{name="关联",columnName=name+"_id",columnType="bigint",controlKind="List",controlDetail=JsonSerializer.Serialize(new{table=name,tableId=target.ToString(CultureInfo.InvariantCulture),nameField="name"})}})));
        var pageCount=(await f.Store.ReadAsync("config_page",new Dictionary<string,object?>{{"config_table_id",source}})).Count;Assert.True(pageCount>0);var before=await f.Snapshot();
        ids.Arm(1+pageCount);
        try
        {
            var response=await f.Post(Save,new{configTableId=source.ToString(CultureInfo.InvariantCulture),selections=fields.Select(field=>new{sourceColumnId=relation.ToString(CultureInfo.InvariantCulture),targetColumnId=field.ToString(CultureInfo.InvariantCulture)}).ToArray()});Assert.NotEqual(200,response.GetProperty("code").GetInt32());Assert.Equal(1,ids.Failures);Assert.Equal(1+pageCount,ids.IssuedDuringArmed);
        }
        finally{ids.Disarm();}
        Assert.Equal(before,await f.Snapshot());await f.SaveEvidence("join-tail-rollback",new{source,target,relation,pageCount,firstColumnAndMappingsRolledBack=true,ids.IssuedDuringArmed,ids.Failures});await f.CompleteAsync();
    }
    /// <summary>故障只在当前fixture明确武装后生效，未武装时使用正常雪花ID，不修改产品数据库或钩子。</summary>
    private sealed class TailFailureIds:IIdGenerator
    {
        private readonly SnowflakeIdGenerator actual=new(829);private int? remaining;
        internal int IssuedDuringArmed{get;private set;}internal int Failures{get;private set;}
        /// <summary>只在目标保存前武装指定次数，确保故障发生于第二列。</summary>
        internal void Arm(int count){remaining=count;IssuedDuringArmed=0;Failures=0;}
        /// <summary>后续读取及精确清理恢复正常ID行为。</summary>
        internal void Disarm()=>remaining=null;
        /// <summary>返回真实雪花ID，倒计数耗尽时注入唯一可观测尾部故障。</summary>
        public long NextId(){if(remaining is{}left){if(left==0){Failures++;throw new InvalidOperationException("测试注入联接批次尾部故障。");}remaining=left-1;IssuedDuringArmed++;}return actual.NextId();}
    }

}
