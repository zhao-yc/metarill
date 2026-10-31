using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Exports;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>仅专属配置能够启用真实验收；普通CI明确跳过，配置错误不得静默跳过。</summary>
public sealed class DerivedTargetNativeTheoryAttribute:TheoryAttribute
{
    /// <summary>只查看是否配置，不读取凭据、创建目录或打开连接。</summary>
    public DerivedTargetNativeTheoryAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("METARILL_DERIVED_TARGET_NATIVE_SETTINGS")))Skip="未配置本轮全局R0目标的专属批准租约，真实MySQL未执行。";}
}

/// <summary>固定四个原完整应用执行真实Production/Web；种子/核验/故障注入之外不直写业务。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class FreshGlobalDerivedTargetMySqlTests
{
    private const long Project=100,First=9007199254748501,ForeignId=9007199254749501;
    private const string Source="/api/admin/auto/cached_consumer/30/",Middle="/api/admin/auto/global_source/10/",Target="/api/admin/auto/global_target/20/",Files="/api/admin/sys/file-library/";
    /// <summary>后缀复用原四库协调器，含义明确为R0/string、R0/int64、R1/string、R1/int64；单选始终int64。</summary>
    [DerivedTargetNativeTheory]
    [InlineData(0,"string","CrossStringStringDatabase","cross_ss",870)]
    [InlineData(0,"int64","CrossStringLongDatabase","cross_sl",871)]
    [InlineData(1,"string","CrossLongStringDatabase","cross_ls",872)]
    [InlineData(1,"int64","CrossLongLongDatabase","cross_ll",873)]
    public async Task Derived_target_preserves_real_web_propagation_maintenance_and_rollback(int mode,string type,string property,string suffix,int worker)
    {
        var path=Environment.GetEnvironmentVariable("METARILL_DERIVED_TARGET_NATIVE_SETTINGS")!;
        Assert.Equal(Path.GetFullPath(path),Path.GetFullPath(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));
        using var document=JsonDocument.Parse(await File.ReadAllTextAsync(path));var settings=document.RootElement;var root=NonRedundantApplicationFixture.Root();
        var run=GlobalDerivedTargetNativeLease.Validate(settings,CSharpGenerator.SafePath(root,".local/global-derived-target-native/prepared"));
        var proof=settings.GetProperty("DerivedTargetPreparation").GetProperty(suffix).Deserialize<GlobalCachedPackageProof>()!;
        var catalog=GlobalDerivedTargetNativePreparation.Verify(proof,mode,type);var options=FreshInstallConfiguration.Read(property,suffix);
        var evidence=CSharpGenerator.SafePath(root,".local/global-derived-target-native/"+run+"/"+suffix);Directory.CreateDirectory(evidence);
        var copied=GlobalDerivedTargetNativePreparation.CopyForExecution(proof,Path.Combine(evidence,"execution/application"),mode,type);
        var package=copied.PackageRoot;var hostDirectory=CSharpGenerator.SafePath(package,"backend/Application.Host");
        var username="derived-target-owner-"+suffix;var password=Guid.NewGuid().ToString("N");FreshHost? host=null;var stage="实例/零表";var passed=false;
        using var session=new DatabaseSession(options);await Pin(session,settings.GetProperty("InstancePin"));Assert.Equal(0L,await Scalar(session,"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"));
        /// <summary>仅子进程环境传递秘密，正式身份和文件/导出根固定在本轮包外。</summary>
        ProcessStartInfo Info(string? operation=null)
        {
            var info=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);info.ArgumentList.Add(CSharpGenerator.SafePath(hostDirectory,"bin/Release/net10.0/Application.Host.dll"));if(operation is not null)info.ArgumentList.Add(operation);
            info.Environment["ASPNETCORE_ENVIRONMENT"]="Production";info.Environment["DOTNET_ENVIRONMENT"]="Production";info.Environment["Logging__LogLevel__Default"]="Warning";
            info.Environment["Database__Provider"]="mysql";info.Environment["Database__ExpectedDatabase"]=options.ExpectedDatabase;info.Environment["Database__ConnectionString"]=options.ConnectionString;
            info.Environment["Authentication__Mode"]="Web";info.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";info.Environment["Authentication__Bootstrap__Username"]=username;info.Environment["Authentication__Bootstrap__Password"]=password;info.Environment["Ids__WorkerId"]=worker.ToString(CultureInfo.InvariantCulture);
            info.Environment["Files__LegacyPublicBaseUrl"]="";info.Environment["Files__PublicContentProjectId"]="100";info.Environment["Files__RootDirectory"]=Path.Combine(evidence,"uploads");info.Environment["Exports__RootDirectory"]=Path.Combine(evidence,"exports");info.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;return info;
        }
        try
        {
            stage="普通启动及显式预览不改空库";host=await FreshHost.StartAsync(Info(),"/health");Assert.Equal(0L,await Scalar(session,"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"));await host.DisposeAsync();host=null;
            await NonRedundantApplicationFixture.RunAsync(Info("--schema-preview"));Assert.Equal(0L,await Scalar(session,"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"));
            stage="显式安装和重复初始化";await NonRedundantApplicationFixture.RunAsync(Info("--schema-apply"));await NonRedundantApplicationFixture.RunAsync(Info("--schema-apply"));Assert.Equal(FreshInstallConfiguration.ExpectedFullTableCount(catalog),await Scalar(session,"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"));Assert.Empty((await new SchemaService(session).PreviewAsync(catalog)).Changes);
            await NonRedundantApplicationFixture.RunAsync(Info("--initialize-security"));var security=new SqlRowStore(session,Model(package,"security"));var digest=SqlRowStore.Text(Assert.Single(await security.ReadAsync("yz_manager",Row("name",username))),"password");Assert.True(PasswordHasher.Verify(password,digest));await NonRedundantApplicationFixture.RunAsync(Info("--initialize-security"));Assert.Equal(digest,SqlRowStore.Text(Assert.Single(await security.ReadAsync("yz_manager",Row("name",username))),"password"));
            var store=new SqlRowStore(session,catalog);await store.InsertAsync("global_target",Row("id",First,"name","甲_%!名称'","status",1));await store.InsertAsync("global_target",Row("id",First+1,"name","乙名称","status",1));
            var ids=new SnowflakeIdGenerator(worker);var role=ids.NextId();var manager=ids.NextId();var reader="derived-target-reader-"+suffix;var secret=Guid.NewGuid().ToString("N");var grants=new Dictionary<string,long>(StringComparer.Ordinal);
            await session.InTransactionAsync(async()=>{await security.ReadForUpdateAsync("yz_project",Row("id",Project));await security.InsertAsync("yz_role",Row("id",role,"yz_project_id",Project,"yz_admin_type_id",9L,"name",reader,"is_super",0,"is_built_in",0));await security.InsertAsync("yz_manager",Row("id",manager,"yz_project_id",Project,"yz_admin_type_id",9L,"name",reader,"password",PasswordHasher.Hash(secret),"is_disabled",0,"yz_role_ids",JsonSerializer.Serialize(new[]{Id(role)})));foreach(var page in catalog.ConfigPages){var id=ids.NextId();grants.Add(page.Id,id);await security.InsertAsync("yz_role_permission",Row("id",id,"yz_project_id",Project,"yz_role_id",role,"config_page_id",long.Parse(page.Id,CultureInfo.InvariantCulture),"permission_values","[\"is_all\"]"));}return true;});
            host=await FreshHost.StartAsync(Info(),"/health");await FreshWebHttp.SendAsync(host.Client,HttpMethod.Post,Source+"page",new{},HttpStatusCode.Unauthorized);await GlobalDerivedTargetNativeHttp.LoginAsync(host.Client,username,password);
            using var client=new HttpClient{BaseAddress=host.Client.BaseAddress,Timeout=TimeSpan.FromSeconds(60)};var token=await GlobalDerivedTargetNativeHttp.LoginAsync(client,reader,secret);Assert.False((await FreshWebHttp.SendAsync(client,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data").GetProperty("isSuper").GetBoolean());
            stage="正式候选/保存/传播/回填/回滚";var probe=new Probe(client,host.Client,session,store,new SqlRowStore(session,Model(package,"files")),security,grants,mode,type,value=>stage=value);await probe.Exercise();
            stage="重启后原会话及注销";await host.DisposeAsync();host=null;host=await FreshHost.StartAsync(Info(),"/health");host.Client.DefaultRequestHeaders.Add("loginStateId",token);await FreshWebHttp.SendAsync(host.Client,HttpMethod.Get,Source+"detail/"+probe.RecordId);await FreshWebHttp.SendAsync(host.Client,HttpMethod.Post,"/api/admin/sys/yz_manager/exit_login",new{});await FreshWebHttp.SendAsync(host.Client,HttpMethod.Get,Source+"detail/"+probe.RecordId,expected:HttpStatusCode.Unauthorized);
            GlobalDerivedTargetNativePreparation.Verify(proof,mode,type);GlobalDerivedTargetNativePreparation.Verify(copied,mode,type,true);Assert.Empty((await new SchemaService(session).PreviewAsync(catalog)).Changes);
            passed=true;await Receipt("receipt",new{run,suffix,mode,type,passed,productCommit=GlobalDerivedTargetNativePreparation.ProductCommit,formalWeb=true,ordinaryAccount=true,developmentAuthentication=false,authorizationInjected=false,globalTargetDerivedName=true,middlePhysicalNameAbsent=true,innerKeyType=GlobalDerivedTargetNativePreparation.InnerType(mode,type),independentRoles=true,middleForeignKeySwitch=true,softDelete=true,r0MaintenanceRejected=true,sourceProjectIsolation=true,physicalStorageChecked=true,renamePropagation=true,maintenanceBackfill=mode==1,crossBatchConsumers=mode==1?130:0,lateMaintenanceRollback=mode==1,repeatedMaintenanceZero=true,originalImportExport=true,sourceRevocation=true,targetSelectionQueryInheritance=true,independentTargetRevocation=false,sourceWriteContractPreserved=true,latePropagationRollback=mode==1,lateFileRollback=true,persistentSession=true,browserExecuted=false,concurrencyExecuted=false,performanceExecuted=false,cleanupByFreshLease=true});
        }
        catch(Exception error){await Receipt("failure",new{run,suffix,stage,errorType=error.GetType().Name,passed=false,cleanupByFreshLease=true});throw;}
        finally{if(host is not null)await host.DisposeAsync();if(!passed)await Receipt("failed",new{run,suffix,passed=false,cleanupByFreshLease=true});}
        /// <summary>回执不保存请求、凭据或连接，失败位置供冻结重验使用。</summary>
        Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
    /// <summary>真实普通账号调用原接口；SQL只核验、种子和本轮约束故障注入。</summary>
    private sealed class Probe(HttpClient client,HttpClient owner,DatabaseSession session,SqlRowStore store,SqlRowStore files,SqlRowStore security,Dictionary<string,long> grants,int mode,string type,Action<string> stage)
    {
        internal string RecordId="";private string middleId="",otherMiddleId="",selectedId="";private string? name="甲_%!名称'",selectedName="甲_%!名称'";
        /// <summary>真实R0中间表不存名称；分别证明当前读取、双角色、关联切换、软删、原维护及晚批事务。</summary>
        internal async Task Exercise()
        {
            stage("R0物理列与候选/保存");Assert.Equal(0L,await Scalar(session,"SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='global_source' AND COLUMN_NAME='global_target_name'"));
            middleId=(await Post(owner,Middle+"add",new{globalTargetId=Id(First),status=1})).GetProperty("id").GetString()!;otherMiddleId=(await Post(owner,Middle+"add",new{globalTargetId=Id(First+1),status=1})).GetProperty("id").GetString()!;selectedId=middleId;
            await CheckMiddle(First);
            var candidates=await Post(client,Source+"findNameIdGlobalSource34/page",new{pageNo=1,pageSize=1,keyword=name});Assert.Equal("1",candidates.GetProperty("total").ToString());Assert.Equal(middleId,Assert.Single(candidates.GetProperty("records").EnumerateArray()).GetProperty("id").GetString());
            await Post(client,Source+"findNameIdGlobalSource34/selected",new{selectedIds=new[]{middleId}});
            var bytes=Encoding.UTF8.GetBytes("全局R0实时名称正式文件");var file=await Upload(bytes);var added=await Post(client,Source+"add",new{name="正式消费",globalSourceId=middleId,selectedGlobalSourceId=middleId,attachments=new[]{file}});RecordId=added.GetProperty("id").GetString()!;Check(added);await CheckStorage();await Download(file,bytes);await References(file,1);
            var foreign=Row("id",ForeignId,"yz_project_id",101L,"name","其它项目保持","global_source_id",Key(middleId),"selected_global_source_id",long.Parse(middleId,CultureInfo.InvariantCulture));if(mode==1){foreign["global_source_global_target_name"]="其它项目历史";foreign["selected_global_source_global_target_name"]="其它项目历史";}await store.InsertAsync("cached_consumer",foreign);var foreignBefore=JsonSerializer.Serialize(await Physical("cached_consumer",Id(ForeignId)));
            var forged=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(added.GetRawText())!;forged["globalSourceGlobalTargetName"]=JsonSerializer.SerializeToElement("伪造");forged["selectedGlobalSourceGlobalTargetName"]=JsonSerializer.SerializeToElement("伪造");Check(await Post(client,Source+"update",forged));
            // 两个控件引用不同中间行；改第一个上游不能覆盖另一角色的名称。
            stage("独立角色与目标改名");selectedId=otherMiddleId;selectedName="乙名称";Check(await Post(client,Source+"update",new{id=RecordId,selectedGlobalSourceId=selectedId}));
            name="改名_%!甲'";await Post(owner,Target+"update",new{id=Id(First),name});Check(await Detail());await CheckMiddle(First);await CheckStorage();await ForeignUnchanged();
            var filtered=await Post(client,Source+"page",new PageRequest{Filters=new(){{"globalSourceGlobalTargetName",JsonSerializer.SerializeToElement(name)}}});Assert.Equal("1",filtered.GetProperty("total").ToString());Assert.Equal(RecordId,Assert.Single(filtered.GetProperty("records").EnumerateArray()).GetProperty("id").GetString());
            // R0外键切换和第二个终点改名都必须传到已有R1副本，不能等待维护重算。
            stage("中间关联切换与软删");name=selectedName;await Post(owner,Middle+"update",new{id=middleId,globalTargetId=Id(First+1)});Check(await Detail());await CheckMiddle(First+1);await CheckStorage();
            name=selectedName="关联后乙名称";await Post(owner,Target+"update",new{id=Id(First+1),name});Check(await Detail());await CheckStorage();
            await FreshWebHttp.SendAsync(owner,HttpMethod.Delete,Target,new{id=Id(First+1)});name=selectedName=null;Check(await Detail());await CheckStorage();await ForeignUnchanged();
            // 仅恢复本轮合成墓碑以继续其它用例；恢复动作本身不冒充产品恢复API。
            await store.UpdateAsync("global_target",First+1,Row("delete_no",null));name=selectedName="恢复乙名称";await Post(owner,Target+"update",new{id=Id(First+1),name});Check(await Detail());await CheckStorage();
            name="改名_%!甲'";await Post(owner,Middle+"update",new{id=middleId,globalTargetId=Id(First)});Check(await Detail());await CheckMiddle(First);selectedId=middleId;selectedName=name;Check(await Post(client,Source+"update",new{id=RecordId,selectedGlobalSourceId=selectedId}));await CheckStorage();
            stage("原维护表范围与回填");var options=await Post(owner,"/api/admin/sys/system_maintenance/tables",new{});Assert.Equal(mode==1?new[]{"2","3"}:new[]{"2"},options.EnumerateArray().Select(row=>row.GetProperty("id").GetString()!).Order(StringComparer.Ordinal));
            var before=await Snapshot();await FreshWebHttp.SendAsync(owner,HttpMethod.Post,"/api/admin/sys/system_maintenance/backfill_join_columns",new{configTableId="1"},HttpStatusCode.BadRequest);Assert.Equal(before,await Snapshot());
            if(mode==1)
            {
                await store.UpdateAsync("cached_consumer",long.Parse(RecordId,CultureInfo.InvariantCulture),Row("global_source_global_target_name","故意旧名","selected_global_source_global_target_name","故意旧名"));
                Assert.True((await Backfill("3")).GetProperty("affectedRows").GetInt32()>0);Check(await Detail());await CheckStorage();Assert.Equal(0,(await Backfill("3")).GetProperty("affectedRows").GetInt32());
            }
            else{await FreshWebHttp.SendAsync(owner,HttpMethod.Post,"/api/admin/sys/system_maintenance/backfill_join_columns",new{configTableId="3"},HttpStatusCode.BadRequest);Assert.Equal(0,(await Backfill("2")).GetProperty("affectedRows").GetInt32());}
            await ForeignUnchanged();
            stage("原Excel追加/导出及整批回滚");await Post(client,Source+"importDatas30",new{requestJson=new[]{Row("名称","原导入","原下拉",name,"原单选",name)}});before=await Snapshot();await FreshWebHttp.SendAsync(client,HttpMethod.Post,Source+"importDatas30",new{requestJson=new[]{Row("名称","整批第一","原下拉",name,"原单选",name),Row("名称","整批失败","原下拉","不存在","原单选",name)}},HttpStatusCode.BadRequest);Assert.Equal(before,await Snapshot());
            var ticket=(await Post(client,Source+"initExport",new PageRequest{SelectedIds=[RecordId]})).GetString()!;using(var response=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+Uri.EscapeDataString(ticket))){Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var book=new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));var sheet=book.Worksheet(1);var headers=sheet.Row(1).CellsUsed().ToDictionary(cell=>cell.GetString(),cell=>cell.Address.ColumnNumber);Assert.Equal(name,sheet.Row(2).Cell(headers["原下拉"]).GetString());Assert.Equal(name,sheet.Row(2).Cell(headers["原单选"]).GetString());}
            if(mode==1)await ExerciseLateBatches();
            stage("复制与晚期文件整笔回滚");var copy=(await Post(client,Source+"copy/"+RecordId,new{})).GetProperty("id").GetString()!;await References(file,2);var next=await Upload(Encoding.UTF8.GetBytes("晚期失败附件"));await Sql("ALTER TABLE `yz_file_ref` ADD CONSTRAINT `mt_gdt_late_file` CHECK (`file_id` <> "+long.Parse(next,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)+")");try{before=await Snapshot();await FreshWebHttp.SendAsync(client,HttpMethod.Post,Source+"update",new{id=RecordId,name="不应提交消费",globalSourceId=otherMiddleId,selectedGlobalSourceId=otherMiddleId,attachments=new[]{next}},HttpStatusCode.InternalServerError);Assert.Equal(before,await Snapshot());}finally{await Sql("ALTER TABLE `yz_file_ref` DROP CHECK `mt_gdt_late_file`");}Check(await Detail());await References(file,2);
            // type=1仍继承来源查询；来源撤权后List、单选及保护文件都应拒绝。
            stage("原查询继承与来源撤权");await Grant("10","[]");before=await Snapshot();await Post(client,Source+"findNameIdGlobalSource34/selected",new{selectedIds=new[]{middleId}});await Post(client,Source+"findNameIdSelectedGlobalSource38/selected",new{selectedIds=new[]{middleId}});Assert.Equal(before,await Snapshot());Check(await Post(client,Source+"update",new{id=RecordId,globalSourceId=middleId,selectedGlobalSourceId=middleId}));await Grant("10","[\"is_all\"]");
            await Grant("30","[]");await FreshWebHttp.SendAsync(client,HttpMethod.Post,Source+"page",new{},HttpStatusCode.Forbidden);await FreshWebHttp.SendAsync(client,HttpMethod.Post,Source+"findNameIdGlobalSource34/selected",new{selectedIds=new[]{middleId}},HttpStatusCode.Forbidden);await FreshWebHttp.SendAsync(client,HttpMethod.Post,Source+"findNameIdSelectedGlobalSource38/selected",new{selectedIds=new[]{middleId}},HttpStatusCode.Forbidden);await Download(file,bytes,HttpStatusCode.Unauthorized);await Grant("30","[\"is_all\"]");await FreshWebHttp.SendAsync(client,HttpMethod.Delete,Source,new{id=copy});await References(file,1);await ForeignUnchanged();
            /// <summary>任何全局改名、回填或失败操作均不能写其它来源项目的物理记录。</summary>
            async Task ForeignUnchanged()=>Assert.Equal(foreignBefore,JsonSerializer.Serialize(await Physical("cached_consumer",Id(ForeignId))));
        }
        /// <summary>130条专属R1合成消费者跨过128行边界；传播和原维护在末批失败时必须整笔回滚。</summary>
        private async Task ExerciseLateBatches()
        {
            stage("130消费者改名传播及末批回滚");for(var index=0;index<130;index++)await store.InsertAsync("cached_consumer",Row("id",ForeignId+2000+index,"yz_project_id",100L,"name",index==129?"跨批阻断":"跨批样本"+index,"global_source_id",Key(middleId),"selected_global_source_id",long.Parse(middleId,CultureInfo.InvariantCulture),"global_source_global_target_name",name,"selected_global_source_global_target_name",selectedName));
            name=selectedName="跨批改名";await Post(owner,Target+"update",new{id=Id(First),name});Check(await Detail());await CheckStorage();await BatchNames(name);
            const string blocked="不应提交改名";await Sql("ALTER TABLE `cached_consumer` ADD CONSTRAINT `mt_gdt_late_name` CHECK (`name` <> '跨批阻断' OR `global_source_global_target_name` <> '"+blocked+"')");try{var before=await Snapshot();await FreshWebHttp.SendAsync(owner,HttpMethod.Post,Target+"update",new{id=Id(First),name=blocked},HttpStatusCode.InternalServerError);Assert.Equal(before,await Snapshot());}finally{await Sql("ALTER TABLE `cached_consumer` DROP CHECK `mt_gdt_late_name`");}
            stage("130消费者原维护及末批回滚");for(var index=0;index<130;index++)await store.UpdateAsync("cached_consumer",ForeignId+2000+index,Row("global_source_global_target_name","故意旧名","selected_global_source_global_target_name","故意旧名"));
            await Sql("ALTER TABLE `cached_consumer` ADD CONSTRAINT `mt_gdt_late_fill` CHECK (`name` <> '跨批阻断' OR `global_source_global_target_name` <> '跨批改名')");try{var before=await Snapshot();await FreshWebHttp.SendAsync(owner,HttpMethod.Post,"/api/admin/sys/system_maintenance/backfill_join_columns",new{configTableId="3"},HttpStatusCode.InternalServerError);Assert.Equal(before,await Snapshot());}finally{await Sql("ALTER TABLE `cached_consumer` DROP CHECK `mt_gdt_late_fill`");}
            Assert.Equal(260,(await Backfill("3")).GetProperty("affectedRows").GetInt32());Assert.Equal(0,(await Backfill("3")).GetProperty("affectedRows").GetInt32());await BatchNames(name);
        }
        /// <summary>批量核验真实物理副本而非只比较一条主记录。</summary>
        private async Task BatchNames(string? expected)
        {var rows=(await store.ReadIncludingDeletedAsync("cached_consumer")).Where(row=>SqlRowStore.Id(row)>=ForeignId+2000&&SqlRowStore.Id(row)<ForeignId+2130).ToArray();Assert.Equal(130,rows.Length);Assert.All(rows,row=>{Assert.Equal(expected,Convert.ToString(row["global_source_global_target_name"],CultureInfo.InvariantCulture));Assert.Equal(expected,Convert.ToString(row["selected_global_source_global_target_name"],CultureInfo.InvariantCulture));});}
        /// <summary>物理中间键保持原类型和精度，逻辑名称只能由原HTTP详情返回。</summary>
        private async Task CheckMiddle(long target)
        {var row=await Physical("global_source",middleId);Assert.False(row.ContainsKey("global_target_name"));Assert.Equal(Id(target),SqlRowStore.Text(row,"global_target_id"));if(GlobalDerivedTargetNativePreparation.InnerType(mode,type)=="string")Assert.IsType<string>(row["global_target_id"]);else Assert.IsType<long>(row["global_target_id"]);var detail=(await FreshWebHttp.SendAsync(owner,HttpMethod.Get,Middle+"detail/"+middleId)).GetProperty("data");Assert.Equal(name,detail.GetProperty("globalTargetName").GetString());}
        /// <summary>物理List键沿元数据真实类型写入种子，不将字符串精度测试变成隐式数值转换。</summary>
        private object Key(string id)=>type=="string"?id:long.Parse(id,CultureInfo.InvariantCulture);
        /// <summary>接口回显保留精确字符串ID和两个只读名称。</summary>
        private void Check(JsonElement row){Assert.Equal(middleId,row.GetProperty("globalSourceId").GetString());Assert.Equal(selectedId,row.GetProperty("selectedGlobalSourceId").GetString());Assert.Equal(name,row.GetProperty("globalSourceGlobalTargetName").GetString());Assert.Equal(selectedName,row.GetProperty("selectedGlobalSourceGlobalTargetName").GetString());}
        /// <summary>R0不能产生实体名称存储，R1必须实际写入；两种键均按原类型精确读取。</summary>
        private async Task CheckStorage()
        {var row=await Physical("cached_consumer",RecordId);Assert.Equal("100",SqlRowStore.Text(row,"yz_project_id"));Assert.Equal(middleId,SqlRowStore.Text(row,"global_source_id"));Assert.Equal(selectedId,SqlRowStore.Text(row,"selected_global_source_id"));foreach(var(column,expected)in new[]{("global_source_global_target_name",name),("selected_global_source_global_target_name",selectedName)}){if(mode==0)Assert.False(row.ContainsKey(column));else Assert.Equal(expected,row[column] is{} value?Convert.ToString(value,CultureInfo.InvariantCulture):null);}}
        /// <summary>所有表与文件引用包括墓碑形成稳定摘要，失败不能只比较主记录回显。</summary>
        private async Task<string> Snapshot(){var rows=new SortedDictionary<string,object>(StringComparer.Ordinal);foreach(var table in new[]{"global_target","global_source","cached_consumer"})rows[table]=(await store.ReadIncludingDeletedAsync(table)).OrderBy(row=>SqlRowStore.Id(row)).ToArray();foreach(var table in new[]{"yz_file_ref","yz_file_library","yz_file_library_folder"})rows[table]=(await files.ReadIncludingDeletedAsync(table)).OrderBy(row=>SqlRowStore.Id(row)).ToArray();return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows))));}
        /// <summary>核验物理数据只走本轮已核实例的参数化存储。</summary>
        private async Task<IReadOnlyDictionary<string,object?>> Physical(string table,string id)=>Assert.Single(await store.ReadIncludingDeletedAsync(table,Row("id",long.Parse(id,CultureInfo.InvariantCulture))));
        /// <summary>正文和状态仍由统一Web合同检查。</summary>
        private static async Task<JsonElement> Post(HttpClient sender,string path,object value)=>(await FreshWebHttp.SendAsync(sender,HttpMethod.Post,path,value)).GetProperty("data");
        /// <summary>详情复用原控制器，不用SQL回显替代读取链。</summary>
        private async Task<JsonElement> Detail()=>(await FreshWebHttp.SendAsync(client,HttpMethod.Get,Source+"detail/"+RecordId)).GetProperty("data");
        /// <summary>原维护按钮使用正式超级账号与固定表ID，普通记录写入仍由普通账号执行。</summary>
        private Task<JsonElement> Backfill(string table)=>Post(owner,"/api/admin/sys/system_maintenance/backfill_join_columns",new{configTableId=table});
        /// <summary>撤权仅修改本轮新库角色明确页，不触及既有平台配置。</summary>
        private Task Grant(string page,string values)=>security.UpdateAsync("yz_role_permission",grants[page],Row("permission_values",values));
        /// <summary>保护附件走原计划、multipart、完成三步，不能直接创建文件引用。</summary>
        private async Task<string> Upload(byte[] bytes){var id=(await Post(client,Files+"upload-plan",new{originalName="derived-target.txt",mimeType="text/plain",sizeBytes=bytes.Length,configColumnId="39"})).GetProperty("fileId").GetString()!;using var content=new MultipartFormDataContent();content.Add(new ByteArrayContent(bytes),"file","derived-target.txt");await FreshWebHttp.SendAsync(client,HttpMethod.Post,Files+"local-upload/"+id,content);await Post(client,Files+"complete",new{fileId=id});return id;}
        /// <summary>实际保护下载比较字节，撤权时保留原401合同。</summary>
        private async Task Download(string id,byte[] bytes,HttpStatusCode status=HttpStatusCode.OK){using var response=await client.GetAsync(Files+"protected-content/"+id);Assert.Equal(status,response.StatusCode);if(status==HttpStatusCode.OK)Assert.Equal(bytes,await response.Content.ReadAsByteArrayAsync());}
        /// <summary>当前文件引用严格归属源项目和原页。</summary>
        private async Task References(string id,int count){var rows=await files.ReadAsync("yz_file_ref",Row("file_id",long.Parse(id,CultureInfo.InvariantCulture)));Assert.Equal(count,rows.Count);Assert.All(rows,row=>{Assert.Equal("100",SqlRowStore.Text(row,"yz_project_id"));Assert.Equal("30",SqlRowStore.Text(row,"page_id"));});}
        /// <summary>固定约束仅影响本轮新库，SQL正文与异常内容不写回执。</summary>
        private async Task Sql(string sql){await using var connection=new MySqlConnection(session.Options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;await command.ExecuteNonQueryAsync();}
    }
    /// <summary>DDL之前同时核对随机库、Server-200实例主机及UUID。</summary>
    private static async Task Pin(DatabaseSession session,JsonElement pin){await using var connection=new MySqlConnection(session.Options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var row=await command.ExecuteReaderAsync();Assert.True(await row.ReadAsync()&&row.GetString(0)==session.Options.ExpectedDatabase&&row.GetString(1)==pin.GetProperty("hostname").GetString()&&row.GetString(2)==pin.GetProperty("serverUuid").GetString(),"实例pin不符，未执行DDL。");}
    /// <summary>仅使用核准会话只读表数量。</summary>
    private static async Task<long> Scalar(DatabaseSession session,string sql){await using var connection=new MySqlConnection(session.Options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;return Convert.ToInt64(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture);}
    /// <summary>基础设施元数据只读本完整包，不接受外部目录。</summary>
    private static MetadataCatalog Model(string package,string name)=>JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(CSharpGenerator.SafePath(package,"backend/metadata/"+name+".json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    /// <summary>种子及篡改使用既有存储；不手写实体、Mapper或CRUD。</summary>
    private static Dictionary<string,object?> Row(params object?[] pairs){var row=new Dictionary<string,object?>(StringComparer.Ordinal);for(var i=0;i<pairs.Length;i+=2)row.Add((string)pairs[i]!,pairs[i+1]);return row;}
    /// <summary>JSON边界ID始终使用十进制字符串。</summary>
    private static string Id(long value)=>value.ToString(CultureInfo.InvariantCulture);
}
