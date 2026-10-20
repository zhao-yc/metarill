using System.Diagnostics;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Core.Persistence;
using Metarill.Data;
using Metarill.Data.Schema;
using Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Metarill.Generation.Tests;

/// <summary>固定原账号页的真实生成、用途投影与原Vue合同；不连接数据库。</summary>
public sealed class ManagerCredentialCompatibilityTests
{
    private static readonly JsonSerializerOptions Json=Options();
    private static JsonSerializerOptions Options(){var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);options.Converters.Add(new StringLongJsonConverter());return options;}
    [Theory]
    [InlineData(null)][InlineData("")][InlineData("********")][InlineData("不足十二位")]
    [InlineData("pbkdf2-sha256$350000$abc$def")][InlineData("$argon2id$not-a-new-password")]
    public void New_password_rejects_empty_masks_and_encoded_inputs(string? value)=>Assert.Throws<ArgumentException>(()=>ManagerCredentialValue.Input(value));

    [Fact]
    public void Password_limit_digest_and_legacy_presence_are_distinct()
    {
        Assert.Equal(new string('a',12),ManagerCredentialValue.Input(new string('a',12)));Assert.Equal(new string('a',50),ManagerCredentialValue.Input(new string('a',50)));
        Assert.Throws<ArgumentException>(()=>ManagerCredentialValue.Input(new string('a',51)));
        Assert.Throws<InvalidOperationException>(()=>ManagerCredentialValue.RequireDigest("合法长度但没有经过摘要处理"));
        var digest=Metarill.Runtime.Security.PasswordHasher.Hash("合法新密码需要至少十二个字符");ManagerCredentialValue.RequireDigest(digest);
        var old=ManagerCredentialValue.NormalizeLegacy(Parse("{\"id\":\"7\",\"password\":\"********\",\"salt\":\"\",\"oldPassword\":null}"),LegacyPageWriteOperation.Update);
        Assert.False(old.TryGetProperty("password",out _));Assert.False(old.TryGetProperty("salt",out _));
        Assert.True(ManagerCredentialValue.NormalizeLegacy(Parse("{\"password\":null}"),LegacyPageWriteOperation.Update).GetProperty("password").ValueKind==JsonValueKind.Null);
        Assert.Equal("********",ManagerCredentialValue.NormalizeLegacy(Parse("{\"password\":\"********\"}"),LegacyPageWriteOperation.Create).GetProperty("password").GetString());
        foreach(var json in new[]{"{\"salt\":\"不能由客户设置\"}","{\"oldPassword\":{}}","{\"password\":\"********\",\"password\":\"********\"}"})
            Assert.Throws<ArgumentException>(()=>ManagerCredentialValue.NormalizeLegacy(Parse(json),LegacyPageWriteOperation.Update));
    }
    [Theory]
    [InlineData("0",1)][InlineData("1",0)][InlineData("\"0\"",1)][InlineData("\"1\"",0)][InlineData("null",null)]
    public void Enabled_wire_maps_only_at_fixed_boundary(string json,int? disabled)
    {Assert.Equal(disabled,ManagerCredentialValue.Filter(Parse(json)));Assert.Equal(disabled,ManagerCredentialValue.ToDisabled(disabled is null?null:1-disabled));Assert.Equal(disabled is null?null:1-disabled,ManagerCredentialValue.ToEnabled(disabled));}
    [Theory]
    [InlineData("2")][InlineData("-0")][InlineData("1.0")][InlineData("1e0")][InlineData("true")][InlineData("[]")][InlineData("{}")][InlineData("\" 1\"")]
    public void Enabled_filter_rejects_coercion(string json)=>Assert.Throws<ArgumentException>(()=>ManagerCredentialValue.Filter(Parse(json)));

    /// <summary>额外同表页有凭据声明时，实际固定Scope拒绝；IR和发布门禁也必须保留缺口。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void Extra_account_page_credentials_are_not_authorized_by_table_name(bool changedId)
    {
        var catalog=Catalog();var source=catalog.ConfigPages.Single(page=>page.Id==ManagerCredentialCompilation.PageId);
        source.Id="900000000000000001";foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId==ManagerCredentialCompilation.PageId))field.ConfigPageId=source.Id;
        if(changedId){var password=catalog.ConfigColumns.Single(c=>c.Id==ManagerCredentialCompilation.PasswordId);var old=password.Id;password.Id="900000000000000002";foreach(var field in catalog.ConfigPageColumns.Where(f=>f.ConfigColumnId==old))field.ConfigColumnId=password.Id;}
        var model=GenerationModel.Create(catalog,"Example.ExtraCredential");var table=model.Tables.Single(t=>t.Source.Id==ManagerCredentialCompilation.TableId);var page=table.Pages.Single();
        Assert.True(ManagerCredentialCompilation.Unsupported(table,page));Assert.Contains(GenerationCapabilityAudit.Collect(model),gap=>gap.PageId==page.Source.Id&&gap.Reason.Contains("凭据"));
        Assert.DoesNotContain(ReadProjectionCompilation.ForPage(model,table,page).Detail.PhysicalColumns,c=>ManagerCredentialCompilation.IsCredential(table,c));
        Assert.Contains("ManagerCredentialValue.RequirePublishedPage(false)",BackendRenderer.Render(model).Single(file=>file.Path=="Services/YzManagerService.g.cs").Content);
    }

    /// <summary>凭据不展示也不能用作范围、筛选或排序探针，原页本身同样不获得这种权限。</summary>
    [Theory]
    [InlineData("filter")][InlineData("sort")][InlineData("default-order")][InlineData("scope")][InlineData("control")]
    public void Hidden_credential_queries_or_changed_controls_remain_explicitly_unsupported(string kind)
    {
        var catalog=Catalog();var page=catalog.ConfigPages.Single(p=>p.Id==ManagerCredentialCompilation.PageId);var password=catalog.ConfigColumns.Single(c=>c.Id==ManagerCredentialCompilation.PasswordId);
        var field=catalog.ConfigPageColumns.Single(f=>f.ConfigPageId==page.Id&&f.ConfigColumnId==password.Id);
        field.IsEdit=0;field.IsShow=0;field.Extra["isInEdit"]=JsonSerializer.SerializeToElement(0);field.Extra["isInGrid"]=JsonSerializer.SerializeToElement(0);
        if(kind=="filter")field.Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        if(kind=="sort")field.Extra["isSort"]=JsonSerializer.SerializeToElement(1);
        if(kind=="default-order")page.OrderBy="password asc";
        if(kind=="scope"){page.WhereSql="password=@parent_id";password.LinkConfigTableId=FixedRoleCheckboxCompilation.AdminTable;}
        if(kind=="control"){password.ControlKind="ConstList";field.IsEdit=1;field.Extra["isInEdit"]=JsonSerializer.SerializeToElement(1);}
        var model=GenerationModel.Create(catalog,"Example.CredentialProbe");var owner=model.Tables.Single(t=>t.Source.Id==ManagerCredentialCompilation.TableId);var actual=owner.Pages.Single();
        Assert.True(ManagerCredentialCompilation.Unsupported(owner,actual));Assert.Contains(GenerationCapabilityAudit.Collect(model),gap=>gap.PageId==actual.Source.Id&&gap.Reason.Contains("凭据"));
        Assert.Contains("ManagerCredentialValue.RequirePublishedPage(false)",BackendRenderer.Render(model).Single(file=>file.Path=="Services/YzManagerService.g.cs").Content);
    }

    /// <summary>真实脚本在独立公开输入副本重建，仅恢复原稳定三个控件，不执行结构同步。</summary>
    [Fact]
    public async Task Source_builder_restores_only_three_original_controls_without_physical_changes()
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-manager-builder");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(directory,"scripts"));Directory.CreateDirectory(Path.Combine(directory,"backend/metadata"));var root=SourceRoot();
            File.Copy(Path.Combine(root,"scripts/build-platform-metadata.mjs"),Path.Combine(directory,"scripts/build-platform-metadata.mjs"));
            foreach(var file in new[]{"platform.json","platform-source.json","platform-schema-reference.json","identifiers.json","demo.json"})File.Copy(Path.Combine(root,"backend/metadata",file),Path.Combine(directory,"backend/metadata",file));
            var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("NODE_HOST_PATH")??"node"){WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add(Path.Combine(directory,"scripts/build-platform-metadata.mjs"));
            using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));await process.WaitForExitAsync(timeout.Token);Assert.True(process.ExitCode==0,await stdout+await stderr);
            var rebuilt=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(directory,"backend/metadata/platform.json")),Json)!;
            var ids=new[]{"1679698326770069506","1679698328187744258","1679698328317767682"};
            var fields=rebuilt.ConfigPageColumns.Where(field=>rebuilt.ConfigColumns.Single(column=>column.Id==field.ConfigColumnId).ColumnName.Contains("password",StringComparison.Ordinal)||rebuilt.ConfigColumns.Single(column=>column.Id==field.ConfigColumnId).ColumnName=="salt").ToArray();
            Assert.Equal(ids.Order(),fields.Select(field=>field.Id).Order());Assert.All(fields,field=>Assert.Equal(ManagerCredentialCompilation.PageId,field.ConfigPageId));
            var physical=MySqlSchemaPlanner.Compile(rebuilt,"offline_credential");rebuilt.ConfigPageColumns.RemoveAll(field=>ids.Contains(field.Id));Assert.Empty(MySqlSchemaPlanner.Compare(physical,MySqlSchemaPlanner.Compile(rebuilt,"offline_credential")));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>编译真实生成程序集并执行DTO、VO、私有Query/Order和原Vue；不是镜像模型断言。</summary>
    [Fact]
    public async Task Generated_original_account_form_keeps_masks_presence_wire_and_safe_projections()
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-manager-credential");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var catalog=Catalog();var model=GenerationModel.Create(catalog,"Example.ManagerCredential");var table=model.Tables.Single(t=>t.Source.Id==ManagerCredentialCompilation.TableId);var page=table.Pages.Single();
            var projection=ReadProjectionCompilation.ForPage(model,table,page);Assert.DoesNotContain(projection.List.PhysicalColumns.Concat(projection.Detail.PhysicalColumns),c=>ManagerCredentialCompilation.IsCredential(table,c));
            Assert.Equal(3,projection.Detail.OutputFields.Count(field=>field.Kind==ReadProjectionOutputKind.CredentialEcho));Assert.DoesNotContain(projection.List.OutputFields,field=>field.Kind==ReadProjectionOutputKind.CredentialEcho);
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.ManagerCredential");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var root="Example.ManagerCredential.Generated.";var request=assembly.GetType(root+"DTOs.YzManagerPage6031906822946816UpdateRequest",true)!;
            var omitted=JsonSerializer.Deserialize("{\"id\":\"7\"}",request,Json)!;var clear=JsonSerializer.Deserialize("{\"id\":\"7\",\"password\":null,\"isDisabled\":null}",request,Json)!;
            Assert.False(((Optional<string?>)request.GetProperty("Password")!.GetValue(omitted)!).IsSpecified);Assert.True(((Optional<string?>)request.GetProperty("Password")!.GetValue(clear)!).IsSpecified);Assert.Null(((Optional<string?>)request.GetProperty("Password")!.GetValue(clear)!).Value);
            Assert.Null(request.GetProperty("Salt"));Assert.Null(request.GetProperty("OldPassword"));Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize("{\"id\":\"7\",\"salt\":\"x\"}",request,Json));
            // 旧客户端传输清单保留原空回显以送后端校验；它不能代替严格DTO的写授权。
            var transport=JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(Path.Combine(directory,"generated/frontend/common/yzp_runtime/page-write-schema.generated.json")));
            var sent=transport.GetProperty("pages").GetProperty(page.Source.Id).GetProperty("updateFields").EnumerateArray().Select(item=>item.GetString()).ToArray();
            Assert.Contains("password",sent);Assert.Contains("salt",sent);Assert.Contains("oldPassword",sent);
            var agentSchema=typeof(Metarill.Runtime.Security.PasswordHasher).Assembly.GetType("Metarill.Runtime.Agent.AgentJsonSchema",true)!;
            var machine=(Dictionary<string,object?>)agentSchema.GetMethod("Create",BindingFlags.Public|BindingFlags.Static)!.Invoke(null,[request,0])!;
            var machineFields=Assert.IsType<Dictionary<string,object?>>(machine["properties"]);Assert.True(machineFields.ContainsKey("password"));Assert.False(machineFields.ContainsKey("salt"));Assert.False(machineFields.ContainsKey("oldPassword"));Assert.Equal(false,machine["additionalProperties"]);
            var controllerType=assembly.GetType(root+"Controllers.YzManagerPage6031906822946816Controller",true)!;
            using(var services=new ServiceCollection().AddOptions().Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options=>options.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter())).BuildServiceProvider())
            {
                var controller=(ControllerBase)Activator.CreateInstance(controllerType,[null,null])!;controller.ControllerContext=new(){HttpContext=new DefaultHttpContext{RequestServices=services}};
                var boundaryMethod=controllerType.GetMethod("LegacyRequest",BindingFlags.NonPublic|BindingFlags.Instance)!.MakeGenericMethod(request);var who=new RuntimeActor(1,"旧表单",false,catalog.YzProjectId,"1");
                var normalized=boundaryMethod.Invoke(controller,[Parse("{\"id\":\"7\",\"password\":\"********\",\"salt\":\"\",\"oldPassword\":\"\"}"),LegacyPageWriteOperation.Update,who,null])!;
                Assert.False(((Optional<string?>)request.GetProperty("Password")!.GetValue(normalized)!).IsSpecified);
                Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(()=>boundaryMethod.Invoke(controller,[Parse("{\"id\":\"7\",\"salt\":\"伪盐\"}"),LegacyPageWriteOperation.Update,who,null])).InnerException);
            }
            var entityType=assembly.GetType(root+"Entities.YzManager",true)!;var entity=Activator.CreateInstance(entityType)!;
            foreach(var pair in new Dictionary<string,object?>{{"Id",7L},{"Name","原账号"},{"Password","摘要绝不能回显"},{"Salt","盐绝不能回显"},{"OldPassword","旧摘要绝不能回显"},{"IsDisabled",0},{"YzAdminTypeId",1L},{"YzRoleIds","[]"}})entityType.GetProperty(pair.Key)!.SetValue(entity,pair.Value);
            var voType=assembly.GetType(root+"VOs.YzManagerPage6031906822946816Vo",true)!;var method=voType.GetMethod("FromEntity",[entityType,typeof(JsonElement?),typeof(bool)])!;
            var detail=JsonSerializer.SerializeToElement(method.Invoke(null,[entity,null,true]),voType,Json);Assert.Equal("********",detail.GetProperty("password").GetString());Assert.Equal("",detail.GetProperty("salt").GetString());Assert.Equal("",detail.GetProperty("oldPassword").GetString());Assert.Equal(1,detail.GetProperty("isDisabled").GetInt32());
            var list=JsonSerializer.SerializeToElement(method.Invoke(null,[entity,null,false]),voType,Json);foreach(var key in new[]{"password","salt","oldPassword"})Assert.False(list.TryGetProperty(key,out _));
            Assert.DoesNotContain("摘要",detail.GetRawText());Assert.DoesNotContain("盐绝",detail.GetRawText());
            var opens=0;using var orm=new FreeSql.FreeSqlBuilder().UseConnectionFactory(FreeSql.DataType.MySql,()=>new DeniedConnection(()=>opens++)).UseAutoSyncStructure(false).Build();
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="metarill_business_test",ConnectionString="Server=offline;Database=metarill_business_test;User ID=unused;Password=unused;"},orm);
            var serviceType=assembly.GetType(root+"Services.YzManagerService",true)!;var mapper=Activator.CreateInstance(assembly.GetType(root+"Mappers.YzManagerMapper",true)!,session);
            var args=new object?[serviceType.GetConstructors().Single().GetParameters().Length];args[0]=session;args[1]=mapper;args[2]=new SnowflakeIdGenerator(874);args[3]=Array.Empty<IRecordMutationObserver>();var service=Activator.CreateInstance(serviceType,args)!;
            var filter=new PageRequest{Filters=new(){["isDisabled"]=Parse("1")},OrderBy=new(){Column="isDisabled",Order="ascending"}};var actor=new RuntimeActor(1,"离线原账号",false,catalog.YzProjectId,catalog.YzAdminTypes[0].Id);
            object Query()=>serviceType.GetMethod("Query"+page.Source.Id,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(service,[filter,actor])!;
            var ordered=serviceType.GetMethod("Order"+page.Source.Id,BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[Query(),filter])!;var sql=(string)typeof(ManagerCredentialCompatibilityTests).GetMethod(nameof(Sql),BindingFlags.NonPublic|BindingFlags.Static)!.MakeGenericMethod(entityType).Invoke(null,[ordered])!;
            Assert.Contains("is_disabled",sql);Assert.Contains("1 -",sql);Assert.DoesNotContain("password",sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("salt",sql,StringComparison.OrdinalIgnoreCase);
            string? detailSql=null;orm.Aop.CurdBefore+=(_,args)=>{detailSql=args.Sql;throw new OfflineSqlCapturedException();};
            var read=(Task)serviceType.GetMethod("Detail"+page.Source.Id+"Async")!.Invoke(service,[7L,null,actor,CancellationToken.None])!;Assert.NotNull(await Record.ExceptionAsync(()=>read));Assert.NotNull(detailSql);Assert.Equal(0,opens);Assert.DoesNotContain("password",detailSql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("salt",detailSql,StringComparison.OrdinalIgnoreCase);
            filter.Filters["isDisabled"]=Parse("2");Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(()=>Query()).InnerException);
            using(var writeSession=new DatabaseSession(session.Options,orm,new TransactionOnlyProvider()))
            {
                var writeArgs=(object?[])args.Clone();writeArgs[0]=writeSession;writeArgs[1]=Activator.CreateInstance(assembly.GetType(root+"Mappers.YzManagerMapper",true)!,writeSession);var writeService=Activator.CreateInstance(serviceType,writeArgs)!;
                var createType=assembly.GetType(root+"DTOs.YzManagerPage6031906822946816CreateRequest",true)!;
                foreach(var raw in new[]{"{}","{\"password\":null}","{\"password\":\"\"}","{\"password\":\"********\"}","{\"password\":\"合法新密码必须经过实际摘要钩子\"}"})
                {
                    var create=JsonSerializer.Deserialize(raw,createType,Json)!;var task=(Task)serviceType.GetMethod("Create"+page.Source.Id+"Async")!.Invoke(writeService,[create,null,actor,CancellationToken.None])!;
                    var failure=await Record.ExceptionAsync(()=>task);Assert.NotNull(failure);Assert.True(failure is ArgumentException or InvalidOperationException,failure.ToString());Assert.Null(writeSession.CurrentTransaction);
                }
                Assert.Equal(0,opens);
            }
            var source=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/YzManagerService.g.cs"));var export=source.Split("Export"+page.Source.Id+"Async")[1].Split("public Task")[0];Assert.DoesNotContain("entity.Password",export);Assert.DoesNotContain("entity.Salt",export);Assert.DoesNotContain("entity.OldPassword",export);
            Assert.Contains("ManagerCredentialValue.RequireDigest(entity.Password)",source);Assert.Contains("ManagerCredentialValue.Input(request.Password.Value)",source);
            var vue=Path.Combine(directory,"generated/frontend/pages/yzp/yz_manager-detail-6031906822946816.vue");var text=await File.ReadAllTextAsync(vue);Assert.Contains("v-model='entity.password' maxlength='50'",text);Assert.Contains("name='password' required",text);Assert.Contains("v-model='entity.salt'",text);Assert.Contains("v-model='entity.oldPassword'",text);
            var response=Path.Combine(directory,"response.json");await File.WriteAllTextAsync(response,detail.GetRawText());await RunVue(vue,response);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>从正式源裁出原角色链并恢复原三个凭据与启用字段，保留稳定ID和物理长度。</summary>
    internal static MetadataCatalog Catalog()
    {
        var catalog=FixedRoleCheckboxTests.Catalog();var source=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(SourceRoot(),"backend/metadata/platform.json")),Json)!;
        var names=new[]{"password","salt","old_password","is_disabled"};foreach(var column in source.ConfigColumns.Where(c=>c.ConfigTableId==ManagerCredentialCompilation.TableId&&names.Contains(c.ColumnName)))
        {catalog.ConfigColumns.Add(column);catalog.ConfigPageColumns.AddRange(source.ConfigPageColumns.Where(f=>f.ConfigColumnId==column.Id&&f.ConfigPageId==ManagerCredentialCompilation.PageId));}
        var page=catalog.ConfigPages.Single(p=>p.Id==ManagerCredentialCompilation.PageId);page.IsExport=1;page.Extra["exportMethod"]=JsonSerializer.SerializeToElement("edit");
        var enabled=catalog.ConfigPageColumns.Single(f=>f.ConfigPageId==page.Id&&source.ConfigColumns.Single(c=>c.Id==f.ConfigColumnId).ColumnName=="is_disabled");enabled.Extra["isSearch"]=JsonSerializer.SerializeToElement(1);enabled.Extra["isSort"]=JsonSerializer.SerializeToElement(1);
        return catalog;
    }
    /// <summary>仅提供本机事务壳，真实生成服务必须在任何ORM写入前拒绝缺失的哈希钩子。</summary>
    private sealed class TransactionOnlyProvider:IDatabaseProvider
    {
        private readonly MySqlDatabaseProvider dialect=new();public string Name=>"mysql";public DatabaseCapabilities Capabilities=>dialect.Capabilities;
        public void Validate(DatabaseOptions options){}public string QuoteIdentifier(string value)=>dialect.QuoteIdentifier(value);public string ParameterPlaceholder(string value)=>"@"+value;
        public IFreeSql CreateOrm(DatabaseOptions options)=>throw new NotSupportedException();public DbConnection CreateConnection(DatabaseOptions options)=>new TransactionConnection();
    }
    private sealed class TransactionConnection:DbConnection
    {
        private ConnectionState state;[System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="";public override string Database=>"offline";public override string DataSource=>"offline";public override string ServerVersion=>"offline";public override ConnectionState State=>state;
        public override void Open()=>state=ConnectionState.Open;public override void Close()=>state=ConnectionState.Closed;public override void ChangeDatabase(string name)=>throw new NotSupportedException();protected override DbCommand CreateDbCommand()=>throw new NotSupportedException();protected override DbTransaction BeginDbTransaction(IsolationLevel isolation)=>new Transaction(this);
        private sealed class Transaction(TransactionConnection connection):DbTransaction{public override IsolationLevel IsolationLevel=>IsolationLevel.RepeatableRead;protected override DbConnection DbConnection=>connection;public override void Commit(){}public override void Rollback(){}}
    }
    /// <summary>SQL在FreeSql CurdBefore截断；任何真实Open都记录并拒绝。</summary>
    private sealed class OfflineSqlCapturedException:Exception;
    private sealed class DeniedConnection(Action opening):DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="Server=offline;Database=metarill_business_test;";
        public override string Database=>"metarill_business_test";public override string DataSource=>"offline";public override string ServerVersion=>"8.4.11";public override ConnectionState State=>ConnectionState.Closed;
        public override void ChangeDatabase(string value)=>throw new InvalidOperationException("离线测试禁止切换库");public override void Open(){opening();throw new InvalidOperationException("离线测试禁止连接");}public override void Close(){}
        protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>throw new InvalidOperationException("离线测试禁止事务");protected override DbCommand CreateDbCommand()=>throw new InvalidOperationException("离线测试禁止命令");
    }
    private static string Sql<T>(object query)where T:class=>((FreeSql.ISelect<T>)query).ToSql("1");
    private static JsonElement Parse(string value)=>JsonSerializer.Deserialize<JsonElement>(value);
    private static string SourceRoot([CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
    /// <summary>使用本克隆已有Vue依赖真实挂载，不增加项目依赖契约。</summary>
    private static async Task RunVue(string vue,string response,[CallerFilePath]string file="")
    {var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("NODE_HOST_PATH")??"node"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var value in new[]{Path.Combine(Path.GetDirectoryName(file)!,"ManagerCredentialVueProbe.mjs"),vue,response})start.ArgumentList.Add(value);using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));await process.WaitForExitAsync(timeout.Token);Assert.True(process.ExitCode==0,await stdout+await stderr);}
}
