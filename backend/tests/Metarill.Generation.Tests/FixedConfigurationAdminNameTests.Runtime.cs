using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class FixedConfigurationAdminNameTests
{
    private const string GeneratedNamespace="Example.ConfigurationAdmin";
    private static readonly JsonSerializerOptions Options=new(JsonSerializerDefaults.Web);
    /// <summary>错误父键、目标图及别名冲突不能借本次兼容扩大读取范围。</summary>
    [Theory]
    [InlineData("sql")] [InlineData("alias")] [InlineData("target-project")] [InlineData("target-name")]
    [InlineData("parent-key")] [InlineData("parent-link")] [InlineData("unexpected-parent")] [InlineData("source-id")]
    public void Changed_fixed_graph_keeps_an_explicit_gap(string kind)
    {
        var catalog=Catalog();var id=kind.StartsWith("parent-",StringComparison.Ordinal)?ChildPage:RootPage;var source=catalog.ConfigColumns.Single(c=>c.ConfigTableId==PageTable&&c.ColumnName=="yz_admin_type_id");
        if(kind=="sql")source.ControlDetail=source.ControlDetail[..^1]+",\"whereSql\":\"1=1\"}";
        if(kind=="alias")catalog.ConfigColumns.Add(new(){Id="conflicting-admin-name",ConfigTableId=PageTable,ColumnName="yz_admin_type_name",LogicalType="string"});
        if(kind=="target-project")catalog.ConfigColumns.Single(c=>c.Id=="1696799602636988418").LogicalType="string";
        if(kind=="target-name")catalog.ConfigColumns.Single(c=>c.Id=="1696799602439856130").IsVirtualColumn=1;
        if(kind=="parent-key")catalog.ConfigPages.Single(p=>p.Id==ChildPage).WhereSql="yz_admin_type_id=@parent_id";
        if(kind=="parent-link")catalog.ConfigColumns.Single(c=>c.Id=="1671100304320462849").LinkConfigTableId=FixedRoleAdminNameCompilation.AdminTable;
        if(kind=="unexpected-parent")catalog.ConfigPages.Single(p=>p.Id==RootPage).WhereSql="id=@parent_id";
        if(kind=="source-id"){var old=source.Id;source.Id="changed-admin-source";foreach(var f in catalog.ConfigPageColumns.Where(f=>f.ConfigColumnId==old))f.ConfigColumnId=source.Id;}
        var model=GenerationModel.Create(catalog,GeneratedNamespace);var table=model.Tables.Single(t=>t.Source.Id==PageTable);var page=table.Pages.Single(p=>p.Source.Id==id);
        Assert.Null(FixedRoleAdminNameCompilation.ForPage(model,table,page));
        // 两种无效图还会被既有关系编译器更早精确拒绝；不能把抛错改成成功能力清单。
        if(kind is "sql" or "unexpected-parent")Assert.Equal(kind=="sql"?"关系固定条件不能按已发布物理类型安全编译。":"关联父范围缺少明确目标表：id",Assert.Throws<InvalidOperationException>(()=>GenerationCapabilityAudit.Collect(model)).Message);
        else Assert.Contains(GenerationCapabilityAudit.Collect(model),gap=>gap.PageId==id&&gap.Column=="yz_admin_type_id");
    }
    /// <summary>隐藏来源不给输出，关闭原页面筛选也不保留兼容名称筛选；普通字段筛选白名单不变。</summary>
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Hidden_source_and_disabled_original_search_do_not_open_the_alias(bool hide)
    {
        var catalog=Catalog();var field=catalog.ConfigPageColumns.Single(f=>f.ConfigPageId==TablePage&&f.ConfigColumnId=="2200000000000000087");
        if(hide){field.IsShow=0;field.IsEdit=0;field.Extra["isInGrid"]=JsonSerializer.SerializeToElement(0);field.Extra["isInEdit"]=JsonSerializer.SerializeToElement(0);}else catalog.ConfigPages.Single(p=>p.Id==TablePage).Extra["isSearch"]=JsonSerializer.SerializeToElement(0);
        var model=GenerationModel.Create(catalog,GeneratedNamespace);var table=model.Tables.Single(t=>t.Source.Id==TableId);var page=table.Pages.Single();Assert.Null(FixedRoleAdminNameCompilation.LegacyNameFilter(model,table,page));Assert.Equal("",FixedRoleAdminNameRenderer.FilterCases(model,table,page));if(hide)Assert.Null(FixedRoleAdminNameCompilation.ForPage(model,table,page));
    }
    /// <summary>真实生成程序集执行当前项目补名、父范围及相关EXISTS；只替换终端行传输，不连接数据库。</summary>
    [Fact]
    public async Task Generated_configuration_pages_execute_original_aliases_filters_and_parent_scopes()
    {
        var catalog=Catalog();var keep=new HashSet<string>{TableId,PageTable,FixedRoleAdminNameCompilation.AdminTable};catalog.ConfigTables=catalog.ConfigTables.Where(t=>keep.Contains(t.Id)).ToList();catalog.ConfigColumns=catalog.ConfigColumns.Where(c=>keep.Contains(c.ConfigTableId)).ToList();catalog.ConfigPages=catalog.ConfigPages.Where(p=>keep.Contains(p.ConfigTableId)).ToList();var ids=catalog.ConfigColumns.Select(c=>c.Id).ToHashSet();var pages=catalog.ConfigPages.Select(p=>p.Id).ToHashSet();catalog.ConfigPageColumns=catalog.ConfigPageColumns.Where(f=>pages.Contains(f.ConfigPageId)&&ids.Contains(f.ConfigColumnId)).ToList();catalog.ConfigMenus=[];catalog.ConfigButtons=[];catalog.ConfigChildren=[];catalog.ConfigIndexes=[];catalog.ConfigIndexColumns=[];catalog.ConfigCustomPages=[];
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-configuration-admin-name");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),GeneratedNamespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var data=new FixedRoleAdminNameTests.OfflineRows();using var translation=new FreeSql.FreeSqlBuilder().UseConnectionFactory(FreeSql.DataType.MySql,()=>throw new InvalidOperationException("离线禁止连接数据库")).UseAutoSyncStructure(false).Build();var orm=DispatchProxy.Create<IFreeSql,FixedRoleAdminNameTests.ReadOrmProxy>();((FixedRoleAdminNameTests.ReadOrmProxy)orm).Initialize(translation,data);
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=offline;Database=offline;User ID=unused;Password=unused"},orm);var auth=new ConfigurationAuthorization(catalog.YzProjectId);var services=new ServiceCollection();services.AddSingleton(session);services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(964));services.AddSingleton<IRuntimeAuthorization>(auth);assembly.GetType(GeneratedNamespace+".Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[services,null]);using var provider=services.BuildServiceProvider();
            var project=long.Parse(catalog.YzProjectId,System.Globalization.CultureInfo.InvariantCulture);Type Entity(string name)=>assembly.GetType(GeneratedNamespace+".Generated.Entities."+name,true)!;object Row(Type type,long id,long? admin,long owner,long? parent=null){var row=Activator.CreateInstance(type)!;type.GetProperty("Id")!.SetValue(row,id);type.GetProperty("YzProjectId")!.SetValue(row,owner);type.GetProperty("YzAdminTypeId")!.SetValue(row,admin);if(parent is not null)type.GetProperty("ConfigTableId")!.SetValue(row,parent);return row;}
            var admins=Entity("YzAdminType");object Admin(long id,string name,long owner,bool deleted=false){var row=Activator.CreateInstance(admins)!;admins.GetProperty("Id")!.SetValue(row,id);admins.GetProperty("Name")!.SetValue(row,name);admins.GetProperty("YzProjectId")!.SetValue(row,owner);if(deleted)admins.GetProperty("DeleteNo")!.SetValue(row,DateTime.UtcNow);return row;}
            data.Data[admins]=[Admin(1,"平台后台",project),Admin(2,"外项目后台",project+1),Admin(3,"删除后台",project,true),Admin(4,"另一后台",project)];
            var source=Entity("ConfigPage");const long parent=9007199254740993;data.Data[source]=[Row(source,11,1,project,parent),Row(source,12,2,project,parent),Row(source,13,3,project,parent+1),Row(source,14,null,project,parent+1),Row(source,15,1,project+1,parent)];
            var serviceType=assembly.GetType(GeneratedNamespace+".Generated.Services.ConfigPageService",true)!;var service=provider.GetRequiredService(serviceType);auth.Page=RootPage;
            var result=await Await(serviceType.GetMethod("Page"+RootPage+"Async")!.Invoke(service,[new PageRequest(),auth.Actor,CancellationToken.None])!);var records=JsonSerializer.SerializeToElement(result,Options).GetProperty("records");Assert.Equal(4,records.GetArrayLength());Assert.Equal("平台后台",records[0].GetProperty("yzAdminTypeName").GetString());foreach(var record in records.EnumerateArray().Skip(1))Assert.Equal(JsonValueKind.Null,record.GetProperty("yzAdminTypeName").ValueKind);Assert.Equal(3,data.Commands.Count);Assert.All(auth.Requests,s=>Assert.Equal(RootPage+":list",s));
            data.Commands.Clear();var byAdmin=await Await(serviceType.GetMethod("Page"+RootPage+"Async")!.Invoke(service,[new PageRequest{Filters=new(){{"yzAdminTypeId",JsonSerializer.SerializeToElement("1")}}},auth.Actor,CancellationToken.None])!);var byAdminRows=JsonSerializer.SerializeToElement(byAdmin,Options).GetProperty("records");Assert.Single(byAdminRows.EnumerateArray());Assert.Equal("平台后台",byAdminRows[0].GetProperty("yzAdminTypeName").GetString());
            data.Commands.Clear();await Assert.ThrowsAsync<ArgumentException>(()=>Await(serviceType.GetMethod("Page"+RootPage+"Async")!.Invoke(service,[new PageRequest{Filters=new(){{"yzAdminTypeId",JsonSerializer.SerializeToElement("1 OR 1=1")}}},auth.Actor,CancellationToken.None])!));Assert.Empty(data.Commands);
            data.Commands.Clear();auth.Requests.Clear();auth.Page=ChildPage;var scoped=await Await(serviceType.GetMethod("Page"+ChildPage+"Async")!.Invoke(service,[new PageRequest{ParentId=parent.ToString(System.Globalization.CultureInfo.InvariantCulture)},auth.Actor,CancellationToken.None])!);var children=JsonSerializer.SerializeToElement(scoped,Options).GetProperty("records");Assert.Equal(2,children.GetArrayLength());Assert.All(data.Commands.Take(2),sql=>Assert.Contains("config_table_id",sql));Assert.All(auth.Requests,s=>Assert.Equal(ChildPage+":list",s));
            data.Commands.Clear();auth.Page=RootPage;auth.Operation="detail";var detail=await Await(serviceType.GetMethod("Detail"+RootPage+"Async")!.Invoke(service,[11L,null,auth.Actor,CancellationToken.None])!);Assert.Equal("平台后台",JsonSerializer.SerializeToElement(detail,Options).GetProperty("yzAdminTypeName").GetString());
            var controller=assembly.GetType(GeneratedNamespace+".Generated.Controllers.ConfigPagePage"+RootPage+"Controller",true)!;var legacy=(LegacyPageWriteContract)controller.GetField("LegacyWrite",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;var normalized=legacy.Normalize(JsonSerializer.SerializeToElement(new{id="11",yzAdminTypeId="1",yzAdminTypeName="客户端回显不得写入"}),LegacyPageWriteOperation.Update,catalog.YzProjectId,null);Assert.False(normalized.TryGetProperty("yzAdminTypeName",out _));Assert.Equal("1",normalized.GetProperty("yzAdminTypeId").GetString());
            var tables=Entity("ConfigTable");data.Data[tables]=[Row(tables,21,1,project),Row(tables,22,2,project),Row(tables,23,3,project),Row(tables,24,4,project),Row(tables,25,null,project)];var tableServiceType=assembly.GetType(GeneratedNamespace+".Generated.Services.ConfigTableService",true)!;var tableService=provider.GetRequiredService(tableServiceType);auth.Page=TablePage;auth.Operation="list";data.Commands.Clear();auth.Requests.Clear();
            var filtered=await Await(tableServiceType.GetMethod("Page"+TablePage+"Async")!.Invoke(tableService,[new PageRequest{Filters=new(){{"yzAdminTypeName",JsonSerializer.SerializeToElement("平台")}}},auth.Actor,CancellationToken.None])!);var selected=JsonSerializer.SerializeToElement(filtered,Options).GetProperty("records");Assert.Single(selected.EnumerateArray());Assert.Equal("平台后台",selected[0].GetProperty("yzAdminTypeName").GetString());Assert.All(data.Commands.Take(2),sql=>{Assert.Contains("exists",sql.ToLowerInvariant());Assert.Contains("yz_project_id",sql);Assert.Contains("delete_no",sql);});
            foreach(var bad in new[]{JsonSerializer.SerializeToElement(new[]{"平台"}),JsonSerializer.SerializeToElement(1)}){data.Commands.Clear();await Assert.ThrowsAsync<ArgumentException>(()=>Await(tableServiceType.GetMethod("Page"+TablePage+"Async")!.Invoke(tableService,[new PageRequest{Filters=new(){{"yzAdminTypeName",bad}}},auth.Actor,CancellationToken.None])!));Assert.Empty(data.Commands);}
            auth.Operation="denied";data.Commands.Clear();await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Await(tableServiceType.GetMethod("Page"+TablePage+"Async")!.Invoke(tableService,[new PageRequest(),auth.Actor,CancellationToken.None])!));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>解包真实异步调用，不吞掉原方法异常。</summary>
    private static async Task<object?> Await(object value){var task=(Task)value;await task;return task.GetType().GetProperty("Result")!.GetValue(task);}
    private sealed class ConfigurationAuthorization(string project):IRuntimeAuthorization
    {
        internal RuntimeActor Actor{get;}=new(1,"离线配置读取",false,project,"1");internal string Page=RootPage,Operation="list";internal List<string> Requests{get;}=[];
        public RuntimeActor RequireActor()=>Actor;
        public void RequirePagePermission(string pageId,string operation){Requests.Add(pageId+":"+operation);if(pageId!=Page||operation!=Operation)throw new UnauthorizedAccessException("仅允许真实来源页动作");}
    }
}
