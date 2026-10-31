using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Exports;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>实际编译并执行生成Service；SQL交真实FreeSql，行执行为内存替身，XLSX使用真实导出器。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class FixedRoleReadUseTests
{
    private const string Ns="Example.FixedRoleUses", Manager=FixedRoleCheckboxCompilation.ManagerPage, Role=FixedRoleAdminNameCompilation.RolePage;
    private const long LongId=9007199254740993L;
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){Converters={new StringLongJsonConverter()}};

    /// <summary>真实选择回执包含固定名称；只准来源list，保持长ID、原选择顺序和角色数组重复/缺名位置。</summary>
    [Fact]
    public async Task Generated_selected_rows_use_exact_list_action_and_bounded_names_without_credentials()
    {
        await using var f=await Fixture.Create();f.Auth.Set(Manager,"list");
        var result=JsonSerializer.SerializeToElement(await f.Call("YzManager","SelectionSelected"+Manager,new SelectionRowsRequest{SelectedIds=["103","101","101","102"]},f.Auth.Actor,CancellationToken.None),Json);
        Assert.Equal(new[]{"103","101","102"},result.EnumerateArray().Select(row=>row.GetProperty("id").GetString()));
        var main=result[1];Assert.Equal(new[]{LongId.ToString(CultureInfo.InvariantCulture),"2",LongId.ToString(CultureInfo.InvariantCulture),"3","4","5","6","7","999"},main.GetProperty("yzRoleIds").EnumerateArray().Select(item=>item.GetString()));
        var names=main.GetProperty("yzRoleNames").EnumerateArray().ToArray();Assert.Equal(9,names.Length);Assert.Equal("长ID角色",names[0].GetString());Assert.Equal("长ID角色",names[2].GetString());
        foreach(var index in new[]{1,3,4,5,6,8})Assert.Equal(JsonValueKind.Null,names[index].ValueKind);Assert.Equal("",names[7].GetString());
        Assert.Equal(0,result[0].GetProperty("yzRoleNames").GetArrayLength());Assert.Equal(JsonValueKind.Null,result[2].GetProperty("yzRoleNames").ValueKind);
        Assert.All(result.EnumerateArray(),row=>{Assert.False(row.TryGetProperty("password",out _));Assert.False(row.TryGetProperty("salt",out _));Assert.False(row.TryGetProperty("oldPassword",out _));});
        Assert.Equal(2,f.Rows.Commands.Count);Assert.Equal(1,f.Rows.MaxConcurrent);Assert.All(f.Auth.Requests,item=>Assert.Equal(Manager+":list",item));
        var sourceSql=f.Rows.Commands[0];foreach(var secret in new[]{"password","salt","old_password"})Assert.DoesNotContain(secret,sourceSql,StringComparison.OrdinalIgnoreCase);
        var namesSql=f.Rows.Commands[1];Assert.Contains("EXISTS",namesSql,StringComparison.OrdinalIgnoreCase);Assert.Contains("yz_admin_type",namesSql);Assert.Contains("yz_project_id",namesSql);Assert.Contains("delete_no",namesSql);
        var namesSelect=namesSql[..namesSql.IndexOf("FROM",StringComparison.OrdinalIgnoreCase)];Assert.DoesNotContain("is_super",namesSelect);Assert.DoesNotContain("yz_admin_type_id",namesSelect);
        foreach(var allowed in new[]{"create","update","detail","export"})
        {f.Reset();f.Auth.Set(Manager,allowed);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Call("YzManager","SelectionSelected"+Manager,new SelectionRowsRequest{SelectedIds=[]},f.Auth.Actor,CancellationToken.None));Assert.True(f.Rows.Commands.Count==0,"错误来源动作必须在读取源行前拒绝。");}
        f.Reset();f.Auth.Set(Manager,"list");await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Call("YzManager","SelectionSelected"+Manager,new SelectionRowsRequest{SelectedIds=["101"]},f.Auth.Actor with{AccountId=2},CancellationToken.None));Assert.True(f.Rows.Commands.Count==0);
        foreach(var id in new[]{"104","105","999"})
        {f.Reset();await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Call("YzManager","SelectionSelected"+Manager,new SelectionRowsRequest{SelectedIds=[id]},f.Auth.Actor,CancellationToken.None));Assert.Single(f.Rows.Commands);}
        f.Reset();f.Auth.Set(Role,"list");var roles=JsonSerializer.SerializeToElement(await f.Call("YzRole","SelectionSelected"+Role,new SelectionRowsRequest{SelectedIds=[LongId.ToString(CultureInfo.InvariantCulture),"2","4","5","6"]},f.Auth.Actor,CancellationToken.None),Json);
        Assert.Equal("本项目后台",roles[0].GetProperty("yzAdminTypeName").GetString());foreach(var i in new[]{1,2,3})Assert.Equal(JsonValueKind.Null,roles[i].GetProperty("yzAdminTypeName").ValueKind);Assert.Equal("本项目后台",roles[4].GetProperty("yzAdminTypeName").GetString());
        Assert.All(f.Auth.Requests,item=>Assert.Equal(Role+":list",item));
        // 批量上限沿原计划：201个唯一角色只做两次串行ID/name查询，超5000在发SQL前拒绝。
        f.Reset();f.Auth.Set(Manager,"list");var manager=f.Type("YzManager");var role=f.Type("YzRole");f.Rows.Data[role]=Enumerable.Range(1,201).Select(id=>f.Row(role,("Id",(long)id),("YzAdminTypeId",1L),("Name","角色"+id))).ToArray();
        var batches=Array.CreateInstance(manager,201);for(var i=0;i<201;i++)batches.SetValue(f.Row(manager,("Id",(long)i+1),("YzRoleIds","["+(i+1)+"]")),i);
        var projector=f.Service("YzManager").GetType().GetMethod("ProjectRoleNames"+Manager+"Async",BindingFlags.Instance|BindingFlags.NonPublic)!;
        await Result(projector.Invoke(f.Service("YzManager"),[batches,f.Auth.Actor,"list",CancellationToken.None,false])!);Assert.Equal(2,f.Rows.Commands.Count);Assert.Equal(1,f.Rows.MaxConcurrent);
        f.Reset();f.Auth.Set(Manager,"list");f.Auth.SuccessfulPermissionBudget=2;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Result(projector.Invoke(f.Service("YzManager"),[batches,f.Auth.Actor,"list",CancellationToken.None,false])!));Assert.Single(f.Rows.Commands);
        f.Reset();f.Auth.Set(Manager,"list");var empty=await f.Call("YzManager","SelectionSelected"+Manager,new SelectionRowsRequest(),f.Auth.Actor,CancellationToken.None);Assert.True(((System.Collections.ICollection)empty!).Count==0);Assert.True(f.Rows.Commands.Count==0);
        f.Reset();var excessive=Array.CreateInstance(manager,5001);for(var i=0;i<5001;i++)excessive.SetValue(f.Row(manager,("YzRoleIds","["+(i+1)+"]")),i);
        await Assert.ThrowsAsync<ArgumentException>(()=>Result(projector.Invoke(f.Service("YzManager"),[excessive,f.Auth.Actor,"list",CancellationToken.None,false])!));Assert.True(f.Rows.Commands.Count==0);
    }

    /// <summary>私有夹具才打开导出；冗余角色原ID JSON逐字写XLSX，后台单选写名称且只使用来源export权限。</summary>
    [Fact]
    public async Task Generated_export_keeps_original_id_json_and_admin_name_cells_with_export_only_authorization()
    {
        await using var f=await Fixture.Create();f.Auth.Set(Manager,"export");
        var export=(PageExportRequest)(await f.Call("YzManager","Export"+Manager,new PageRequest(),f.Auth.Actor,100,CancellationToken.None))!;
        Assert.Equal(3,export.Rows.Count);var roleIndex=export.Headers.ToList().IndexOf("角色");Assert.True(roleIndex>=0,"原角色表头必须存在。");
        Assert.Equal(f.IdJson,export.Rows[0][roleIndex]);Assert.Null(export.Rows[1][roleIndex]);Assert.Equal("[]",export.Rows[2][roleIndex]);Assert.Single(f.Rows.Commands);Assert.All(f.Auth.Requests,item=>Assert.Equal(Manager+":export",item));
        foreach(var secret in new[]{"password","salt","old_password"})Assert.DoesNotContain(secret,f.Rows.Commands[0],StringComparison.OrdinalIgnoreCase);
        var writer=new ExcelPageExportService(new(){RootDirectory=Path.Combine(f.Directory,"xlsx")});var ticket=await writer.CreateAsync(export,f.Auth.Actor);var content=await writer.OpenAsync(ticket,f.Auth.Actor);
        await using(content.Stream){using var workbook=new XLWorkbook(content.Stream);var sheet=workbook.Worksheet(1);Assert.Equal(f.IdJson,sheet.Cell(2,roleIndex+1).GetString());Assert.Equal(XLDataType.Text,sheet.Cell(2,roleIndex+1).DataType);Assert.True(sheet.Cell(3,roleIndex+1).IsEmpty());Assert.Equal("[]",sheet.Cell(4,roleIndex+1).GetString());}
        foreach(var allowed in new[]{"list","create","update"})
        {f.Reset();f.Auth.Set(Manager,allowed);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Call("YzManager","Export"+Manager,new PageRequest(),f.Auth.Actor,100,CancellationToken.None));Assert.True(f.Rows.Commands.Count==0);}
        f.Reset();f.Auth.Set(Role,"export");var adminExport=(PageExportRequest)(await f.Call("YzRole","Export"+Role,new PageRequest(),f.Auth.Actor,100,CancellationToken.None))!;
        var adminIndex=adminExport.Headers.ToList().IndexOf("后台类型");Assert.True(adminIndex>=0);Assert.Equal("本项目后台",adminExport.Rows[0][adminIndex]);Assert.Null(adminExport.Rows[1][adminIndex]);Assert.Null(adminExport.Rows[2][adminIndex]);Assert.Null(adminExport.Rows[3][adminIndex]);
        Assert.All(f.Auth.Requests,item=>Assert.Equal(Role+":export",item));Assert.Equal(2,f.Rows.Commands.Count);Assert.DoesNotContain("yz_project_id",f.Rows.Commands[1][..f.Rows.Commands[1].IndexOf("FROM",StringComparison.OrdinalIgnoreCase)]);
        var adminTicket=await writer.CreateAsync(adminExport,f.Auth.Actor);var adminFile=await writer.OpenAsync(adminTicket,f.Auth.Actor);await using(adminFile.Stream){using var book=new XLWorkbook(adminFile.Stream);Assert.Equal("本项目后台",book.Worksheet(1).Cell(2,adminIndex+1).GetString());Assert.True(book.Worksheet(1).Cell(3,adminIndex+1).IsEmpty());}
        // 真实ORM进入执行前事件提供SQL/参数，事件立即中止，不打开任何数据库连接。
        using var orm=new FreeSql.FreeSqlBuilder().UseConnectionFactory(FreeSql.DataType.MySql,()=>throw new InvalidOperationException("离线禁止连接")).UseGenerateCommandParameterWithLambda(true).UseAutoSyncStructure(false).Build();
        string? sql=null;object?[] parameters=[];orm.Aop.CurdBefore+=(_,args)=>{sql=args.Sql;parameters=args.DbParms.Select(parameter=>parameter.Value).ToArray();throw new CapturedSql();};
        using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=offline;Database=offline;User ID=unused;Password=unused;"},orm);f.Auth.Set(Role,"export");var service=f.NewService("YzRole",session);
        await Assert.ThrowsAsync<CapturedSql>(()=>f.CallOn(service,"Export"+Role,new PageRequest{Filters=new(){["name"]=JsonSerializer.SerializeToElement("筛选'%_")}},f.Auth.Actor,100,CancellationToken.None));
        Assert.NotNull(sql);Assert.DoesNotContain("筛选",sql);Assert.Contains(parameters,value=>value is string text&&text.Contains("筛选",StringComparison.Ordinal));Assert.Contains("yz_project_id",sql);Assert.Contains("delete_no",sql);
    }

    /// <summary>只解除经过固定证明的用途缺口；原开关、未知声明和Runtime A/平台自定义控件门禁不改变。</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Read_ir_tracks_readable_selection_names_and_keeps_other_gates(bool detailOnly)
    {
        var catalog=Catalog();var field=catalog.ConfigPageColumns.Single(item=>item.ConfigColumnId==FixedRoleCheckboxCompilation.RoleColumn);
        if(detailOnly){field.IsShow=0;field.Extra["isInGrid"]=JsonSerializer.SerializeToElement(0);}
        var model=GenerationModel.Create(catalog,Ns);var table=model.Tables.Single(item=>item.Source.Id==FixedRoleCheckboxCompilation.ManagerTable);var page=table.Pages.Single();
        var plan=ReadProjectionCompilation.ForPage(model,table,page).Selection;Assert.True(plan.CanCompile,string.Join(";",plan.Gaps.Select(gap=>gap.Reason)));Assert.Contains(plan.OutputFields,item=>item.Kind==ReadProjectionOutputKind.RoleNames);
        var gaps=GenerationCapabilityAudit.Collect(model);Assert.Contains(gaps,gap=>gap.PageId==Manager&&gap.Usage=="runtime");Assert.DoesNotContain(gaps,gap=>gap.PageId==Manager&&gap.Usage is "selection" or "export");Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
        catalog.ConfigColumns.Single(item=>item.Id==FixedRoleCheckboxCompilation.RoleColumn).ControlDetail="unknown_roles";Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
        var original=FixedRoleCheckboxTests.Catalog().ConfigPages.Single(item=>item.Id==Manager);Assert.Equal(0,original.IsExport);Assert.Equal(0,original.IsCopy);Assert.Equal("",original.Extra.GetValueOrDefault("importMethod").ToString());
    }

    /// <summary>用途修复不等于平台定制组件或安全宿主已经下发，原五处独立前端定制依赖门禁逐项保留。</summary>
    [Fact]
    public void Canonical_platform_custom_controls_and_pending_runtime_still_block_full()
    {
        var catalog=OriginalCatalog();
        var model=GenerationModel.Create(catalog,Ns);var customs=model.Tables.SelectMany(table=>table.Pages.SelectMany(page=>page.Readable.Where(column=>column.Source.ControlKind=="CustomControl").Select(column=>(page,column)))).ToArray();
        Assert.Equal(5,customs.Length);var gaps=GenerationCapabilityAudit.Collect(model);
        Assert.Contains(gaps,gap=>gap.PageId==Manager&&gap.Usage=="runtime");var denied=Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
        foreach(var item in customs)Assert.Contains("("+item.page.Source.Id+")："+item.column.Source.ControlDetail,denied.Message);
    }

    /// <summary>读取冻结源头元数据，不依赖当前工作目录或实库。</summary>
    private static MetadataCatalog OriginalCatalog([CallerFilePath]string file="")=>JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../metadata/platform.json"))),Json)!;
    /// <summary>仅在测试内打开元数据已有导出开关，不修改canonical；补齐固定原凭据图以验证SQL排除。</summary>
    private static MetadataCatalog Catalog([CallerFilePath]string file="")
    {
        var catalog=FixedRoleCheckboxTests.Catalog();var full=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../metadata/platform.json"))),Json)!;
        foreach(var id in new[]{ManagerCredentialCompilation.PasswordId,"1679698328187744257","1679698328317767681"}){catalog.ConfigColumns.Add(full.ConfigColumns.Single(item=>item.Id==id));catalog.ConfigPageColumns.Add(full.ConfigPageColumns.Single(item=>item.ConfigPageId==Manager&&item.ConfigColumnId==id));}
        foreach(var page in catalog.ConfigPages.Where(item=>item.Id is Manager or Role)){page.IsExport=1;page.Extra["exportMethod"]=JsonSerializer.SerializeToElement("list");page.Extra["type"]=JsonSerializer.SerializeToElement(2);}
        return catalog;
    }
    /// <summary>等待真实生成Service，保留异步原异常而非反射包装。</summary>
    private static async Task<object?> Result(object call){var task=(Task)call;await task;return task.GetType().GetProperty("Result")!.GetValue(task);}
    private sealed class CapturedSql:Exception;
    private sealed class SourceAuthorization:IRuntimeAuthorization
    {
        internal RuntimeActor Actor{get;}=new(1,"固定用途离线",false,"100","1");private string page="",action="";internal List<string> Requests{get;}=[];internal int SuccessfulPermissionBudget=int.MaxValue;
        /// <summary>每轮只开放指定来源页与动作，批次预算用于模拟请求中的撤权。</summary>
        internal void Set(string id,string operation){page=id;action=operation;SuccessfulPermissionBudget=int.MaxValue;Requests.Clear();}
        /// <summary>返回本夹具唯一固定身份，调用参数不能另选actor。</summary>
        public RuntimeActor RequireActor()=>Actor;
        /// <summary>逐次记录真实生成调用，错误页、动作或预算耗尽均明确拒绝。</summary>
        public void RequirePagePermission(string pageId,string operation){Requests.Add(pageId+":"+operation);if(pageId!=page||operation!=action||SuccessfulPermissionBudget--<=0)throw new UnauthorizedAccessException("仅允许当前来源动作。");}
    }
    /// <summary>隔离临时目录、真实生成/DI与内存行，不创建SQL表或后台账号。</summary>
    private sealed class Fixture:IAsyncDisposable
    {
        private const string BoundaryName="metarill-fixed-role-read-uses";internal readonly string Directory=Path.Combine(Path.GetTempPath(),BoundaryName,Guid.NewGuid().ToString("N"));
        internal readonly FixedRoleAdminNameTests.OfflineRows Rows=new();internal readonly SourceAuthorization Auth=new();private Assembly assembly=null!;private IFreeSql real=null!,orm=null!;private DatabaseSession session=null!;private ServiceProvider provider=null!;
        internal string IdJson=>"[\""+LongId+"\",\"2\",\""+LongId+"\",\"3\",\"4\",\"5\",\"6\",\"7\",\"999\"]";
        /// <summary>真实生成编译并通过生成注册创建服务；连接工厂始终拒绝打开数据库。</summary>
        internal static async Task<Fixture> Create(){var f=new Fixture();try{System.IO.Directory.CreateDirectory(f.Directory);new CSharpGenerator().Generate(Catalog(),Path.Combine(f.Directory,"generated"),Ns);f.assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(f.Directory);f.real=new FreeSql.FreeSqlBuilder().UseConnectionFactory(FreeSql.DataType.MySql,()=>throw new InvalidOperationException("离线禁止连接")).UseAutoSyncStructure(false).Build();f.orm=DispatchProxy.Create<IFreeSql,FixedRoleAdminNameTests.ReadOrmProxy>();((FixedRoleAdminNameTests.ReadOrmProxy)f.orm).Initialize(f.real,f.Rows);f.session=new(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=offline;Database=offline;User ID=unused;Password=unused;"},f.orm);var services=new ServiceCollection();services.AddSingleton(f.session);services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(878));services.AddSingleton<IRuntimeAuthorization>(f.Auth);f.assembly.GetType(Ns+".Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[services,null]);f.provider=services.BuildServiceProvider();f.Seed();return f;}catch{await f.DisposeAsync();throw;}}
        /// <summary>只定位本次生成程序集的固定实体类型。</summary>
        internal Type Type(string name)=>assembly.GetType(Ns+".Generated.Entities."+name,true)!;
        /// <summary>取出生成DI实际注册的Service，不手补服务实例或依赖。</summary>
        internal object Service(string name)=>provider.GetRequiredService(assembly.GetType(Ns+".Generated.Services."+name+"Service",true)!);
        /// <summary>SQL参数探针使用真实ORM会话，但仍由同一生成构造合同创建Service。</summary>
        internal object NewService(string name,DatabaseSession value){var mapper=Activator.CreateInstance(assembly.GetType(Ns+".Generated.Mappers."+name+"Mapper",true)!,value)!;return Activator.CreateInstance(Service(name).GetType(),[value,mapper,new SnowflakeIdGenerator(879),Array.Empty<IRecordMutationObserver>(),Array.Empty<IRecordMutationGuard>(),Auth])!;}
        /// <summary>调用生成服务公开方法，不经过自行实现的同名逻辑。</summary>
        internal Task<object?> Call(string name,string method,params object?[] args)=>CallOn(Service(name),method,args);
        /// <summary>异步方法统一等待结果，保留产品抛出的原始异常。</summary>
        internal Task<object?> CallOn(object service,string method,params object?[] args)=>Result(service.GetType().GetMethod(method+"Async")!.Invoke(service,args)!);
        /// <summary>仅清本轮观察记录，不修改源行或目标行。</summary>
        internal void Reset(){Rows.Commands.Clear();Rows.MaxConcurrent=0;Auth.Requests.Clear();}
        /// <summary>仅给已知强类型属性赋测试值，秘密标记仅用于反向证明不得出现在SQL/VO。</summary>
        internal object Row(Type type,params (string Name,object? Value)[] fields){var result=Activator.CreateInstance(type)!;foreach(var field in fields)type.GetProperty(field.Name)!.SetValue(result,field.Value);return result;}
        /// <summary>内存数据覆盖真实项目、跨项目、两级软删、缺目标和空名称；不执行任何SQL种子。</summary>
        private void Seed(){var a=Type("YzAdminType");Rows.Data[a]=[Row(a,("Id",1L),("Name","本项目后台"),("YzProjectId",100L)),Row(a,("Id",2L),("Name","外项目后台"),("YzProjectId",200L)),Row(a,("Id",3L),("Name","已删后台"),("YzProjectId",100L),("DeleteNo",DateTime.UtcNow))];var r=Type("YzRole");Rows.Data[r]=[Row(r,("Id",LongId),("Name","长ID角色"),("YzAdminTypeId",1L),("YzProjectId",100L)),Row(r,("Id",2L),("Name","不能读外项目名"),("YzAdminTypeId",2L),("YzProjectId",100L)),Row(r,("Id",3L),("Name","已删角色"),("YzAdminTypeId",1L),("YzProjectId",100L),("DeleteNo",DateTime.UtcNow)),Row(r,("Id",4L),("Name","已删后台下角色"),("YzAdminTypeId",3L),("YzProjectId",100L)),Row(r,("Id",5L),("Name","无后台角色"),("YzAdminTypeId",999L),("YzProjectId",100L)),Row(r,("Id",6L),("YzAdminTypeId",1L),("YzProjectId",100L)),Row(r,("Id",7L),("Name",""),("YzAdminTypeId",1L),("YzProjectId",100L))];var m=Type("YzManager");Rows.Data[m]=[Row(m,("Id",101L),("Name","账号甲"),("YzRoleIds",IdJson),("YzProjectId",100L),("Password","不得输出摘要"),("Salt","不得输出盐")),Row(m,("Id",102L),("Name","账号乙"),("YzProjectId",100L)),Row(m,("Id",103L),("Name","账号丙"),("YzRoleIds","[]"),("YzProjectId",100L)),Row(m,("Id",104L),("Name","外项目账号"),("YzProjectId",200L)),Row(m,("Id",105L),("Name","已删账号"),("YzProjectId",100L),("DeleteNo",DateTime.UtcNow))];}
        /// <summary>只释放本夹具依赖和随机临时目录，不接触仓库或外部数据库。</summary>
        public async ValueTask DisposeAsync(){provider?.Dispose();session?.Dispose();real?.Dispose();await TestDirectoryCleanup.DeleteAsync(Directory,Path.Combine(Path.GetTempPath(),BoundaryName));}
    }
}
