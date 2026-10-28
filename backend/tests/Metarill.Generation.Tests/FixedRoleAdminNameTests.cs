using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>真实生成代码、FreeSql翻译与内存行/Vue执行名称合同，不冒充数据库验收。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class FixedRoleAdminNameTests
{
    private const string Ns="Example.RoleAdminName";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);

    /// <summary>原List声明已存在，计算名称只增加VO/用途输出，不增加物理字段或可写DTO。</summary>
    [Fact]
    public void Original_fixed_graph_has_explicit_computed_names_for_list_and_detail()
    {
        var catalog=Catalog();var before=JsonSerializer.Serialize(catalog);var (model,table,page)=Model(catalog);
        Assert.NotNull(FixedRoleAdminNameCompilation.ForPage(model,table,page));
        var plans=ReadProjectionCompilation.ForPage(model,table,page);
        foreach(var plan in new[]{plans.List,plans.Detail})
        {
            Assert.True(plan.CanCompile,string.Join(";",plan.Gaps.Select(item=>item.Reason)));
            Assert.Contains(plan.OutputFields,item=>item.JsonName==FixedRoleAdminNameModel.Alias&&item.Kind==ReadProjectionOutputKind.RoleAdminName);
            Assert.DoesNotContain(plan.PhysicalColumns,item=>item.Source.ColumnName=="yz_admin_type_name");
        }
        Assert.DoesNotContain(GenerationCapabilityAudit.Collect(model),item=>item.Column=="yz_admin_type_id");
        new FrontendPackager().Validate(catalog);Assert.Equal(before,JsonSerializer.Serialize(catalog));
    }

    /// <summary>分别隐藏列表或详情时，另一用途的可读性不能让本用途查询后台名称。</summary>
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void List_and_detail_name_usage_follow_their_own_original_flags(bool listOnly)
    {
        var catalog=Catalog();var field=Field(catalog);
        if(listOnly){field.IsEdit=0;field.Extra["isInEdit"]=JsonSerializer.SerializeToElement(0);}else{field.IsShow=0;field.Extra["isInGrid"]=JsonSerializer.SerializeToElement(0);}
        var (model,table,page)=Model(catalog);var plans=ReadProjectionCompilation.ForPage(model,table,page);
        Assert.Equal(listOnly,plans.List.OutputFields.Any(item=>item.JsonName==FixedRoleAdminNameModel.Alias));Assert.Equal(!listOnly,plans.Detail.OutputFields.Any(item=>item.JsonName==FixedRoleAdminNameModel.Alias));
        var service=BackendRenderer.Render(model).Single(file=>file.Path=="Services/YzRoleService.g.cs").Content;
        Assert.Equal(listOnly,service.Contains("actor, \"list\", cancellationToken)",StringComparison.Ordinal));Assert.Equal(!listOnly,service.Contains("actor, \"detail\", cancellationToken)",StringComparison.Ordinal));
    }

    /// <summary>冻结原生成模板的DOM/事件和样式，只允许已确认的ID展示绑定改为原名称。</summary>
    [Theory]
    [InlineData("list")] [InlineData("detail")]
    public void Original_template_and_style_are_unchanged_except_the_name_binding(string purpose)
    {
        var root=SourceRoot();
        var catalog=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(root,"backend/metadata/platform.json")),Json)!;
        var path="frontend/pages/yzp/yz_role-"+purpose+"-"+FixedRoleAdminNameCompilation.RolePage+".vue";
        var current=UniappRenderer.Render(GenerationModel.Create(catalog,Ns)).Single(file=>file.Path==path).Content.Replace("\r\n","\n",StringComparison.Ordinal);
        static string Template(string text)=>text[..text.IndexOf("<script",StringComparison.Ordinal)];
        static string Style(string text)=>text[text.IndexOf("<style",StringComparison.Ordinal)..];
        static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        // 摘要来自精确d2c1398的受管原模板，不能读取再生成后的新文件当成旧基线而形成循环证明。
        var expected=purpose=="list"?("149228BDF6FD9B4B1182AA42FFA7B6833E41AA8CAB774C911F5B9745464FAC8B","381B366BA6A47E60F6E9A2EE4360112A5E3C410F44CBB89AD163C373B00CB46E"):("FD970869AEA02A8C6CD87CB4BDF701219E89BB1FBFB01DC3C7A956D259117EE9","23B59EDA8DC1CCDAB83C137BD9438A02D5C7919D576FE7983FC3786C5DEB9B5C");
        Assert.Equal(expected.Item1,Hash(Template(current).Replace("yzAdminTypeName","yzAdminTypeId",StringComparison.Ordinal)));Assert.Equal(expected.Item2,Hash(Style(current)));
    }

    /// <summary>错误映射、隐藏冲突和非物理图不能因普通List候选存在而自动通过名称证明。</summary>
    [Theory]
    [InlineData("sql")] [InlineData("unknown")] [InlineData("duplicate")] [InlineData("overlay")] [InlineData("control")] [InlineData("logical")]
    [InlineData("join")] [InlineData("virtual")] [InlineData("link")] [InlineData("target-name")] [InlineData("target-project")] [InlineData("alias")]
    public void Invalid_fixed_alias_proof_keeps_a_real_capability_gap(string kind)
    {
        var catalog=Catalog();var source=catalog.ConfigColumns.Single(item=>item.Id==FixedRoleAdminNameCompilation.AdminColumn);
        if(kind=="sql")source.ControlDetail=source.ControlDetail.Replace("\"whereSql\": \"\"","\"whereSql\": \"1=1\"",StringComparison.Ordinal);
        if(kind=="unknown")source.ControlDetail=source.ControlDetail[..^1]+",\"sql\":\"select 1\"}";
        if(kind=="duplicate")source.ControlDetail=source.ControlDetail[..^1]+",\"nameField\":\"name\"}";
        if(kind=="overlay")Field(catalog).Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"nameField\":\"secret\"}");
        if(kind=="control")Field(catalog).Extra["controlKind"]=JsonSerializer.SerializeToElement("TextArea");
        if(kind=="logical")source.LogicalType="string";
        if(kind=="join")source.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        if(kind=="virtual")source.IsVirtualColumn=1;
        if(kind=="link")source.LinkConfigTableId=FixedRoleAdminNameCompilation.RoleTable;
        if(kind=="target-name")catalog.ConfigColumns.Single(item=>item.Id=="1696799602439856130").IsVirtualColumn=1;
        if(kind=="target-project")catalog.ConfigColumns.Single(item=>item.Id=="1696799602636988418").LogicalType="string";
        if(kind=="alias")catalog.ConfigColumns.Add(new(){Id="test-alias",ConfigTableId=source.ConfigTableId,ColumnName="yz_admin_type_name",LogicalType="string"});
        var (model,table,page)=Model(catalog);
        Assert.Null(FixedRoleAdminNameCompilation.ForPage(model,table,page));
        Assert.Contains(GenerationCapabilityAudit.Collect(model),item=>item.PageId==page.Source.Id&&item.Column==source.ColumnName);
        Assert.False(ReadProjectionCompilation.ForPage(model,table,page).Detail.CanCompile);
        Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
    }

    /// <summary>已证明的选择/导出用途不再误报缺口，不改变原角色页关闭的开关或隐藏字段读取范围。</summary>
    [Theory]
    [InlineData("selection",true)] [InlineData("export",true)] [InlineData("hidden-selection",false)] [InlineData("hidden-export",false)]
    public void Enabled_unfinished_uses_keep_precise_gaps(string usage,bool expected)
    {
        var catalog=Catalog();var p=catalog.ConfigPages.Single(item=>item.Id==FixedRoleAdminNameCompilation.RolePage);
        if(usage.EndsWith("selection",StringComparison.Ordinal))p.Extra["type"]=JsonSerializer.SerializeToElement(2);else p.IsExport=1;
        if(usage.StartsWith("hidden",StringComparison.Ordinal))catalog.ConfigPageColumns.Remove(Field(catalog));
        var (model,table,page)=Model(catalog);var gaps=GenerationCapabilityAudit.Collect(model).Where(item=>item.Column=="yz_admin_type_id").ToArray();
        Assert.DoesNotContain(gaps,item=>item.Usage is "selection" or "export");
        if(expected)new FrontendPackager().Validate(catalog);
        else {Assert.Null(FixedRoleAdminNameCompilation.ForPage(model,table,page));Assert.DoesNotContain(ReadProjectionCompilation.ForPage(model,table,page).List.OutputFields,item=>item.JsonName==FixedRoleAdminNameModel.Alias);}
    }

    /// <summary>实际生成程序集执行批量补名；真实FreeSql只翻译SQL，内存行替身不执行数据库。</summary>
    [Fact]
    public async Task Generated_projection_batches_scope_alias_and_original_vue_are_executable()
    {
        var catalog=Catalog();catalog.ConfigPages.Single(item=>item.Id==FixedRoleAdminNameCompilation.RolePage).IsCopy=1;
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-role-admin-name");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var generated=Path.Combine(directory,"generated");new CSharpGenerator().Generate(catalog,generated,Ns);
            var source=File.ReadAllText(Path.Combine(generated,"Services/YzRoleService.g.cs"));
            foreach(var action in new[]{"list","detail","create","update","copy"})Assert.Contains("actor, \""+action+"\", cancellationToken)",source);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var entity=assembly.GetType(Ns+".Generated.Entities.YzRole",true)!;var vo=assembly.GetType(Ns+".Generated.VOs.YzRolePage"+FixedRoleAdminNameCompilation.RolePage+"Vo",true)!;
            Assert.Null(entity.GetProperty("YzAdminTypeName"));Assert.NotNull(vo.GetProperty("YzAdminTypeName"));
            var controller=assembly.GetType(Ns+".Generated.Controllers.YzRolePage"+FixedRoleAdminNameCompilation.RolePage+"Controller",true)!;
            var legacy=(LegacyPageWriteContract)controller.GetField("LegacyWrite",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
            var normalized=legacy.Normalize(Parse("{\"id\":\"9007199254740993\",\"yzAdminTypeId\":\"1\",\"yzAdminTypeName\":\"不能写入的显示文本\"}"),LegacyPageWriteOperation.Update,catalog.YzProjectId,null);
            Assert.False(normalized.TryGetProperty("yzAdminTypeName",out _));Assert.Equal("1",normalized.GetProperty("yzAdminTypeId").GetString());
            Assert.Throws<ArgumentException>(()=>legacy.Normalize(Parse("{\"id\":\"1\",\"yzAdminTypeNames\":\"未知别名\"}"),LegacyPageWriteOperation.Update,catalog.YzProjectId,null));
            var dto=assembly.GetType(Ns+".Generated.DTOs.YzRolePage"+FixedRoleAdminNameCompilation.RolePage+"UpdateRequest",true)!;
            Assert.Null(dto.GetProperty("YzAdminTypeName"));Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize("{\"id\":1,\"yzAdminTypeName\":\"x\"}",dto,Json));

            var db=new OfflineRows();using var realOrm=new FreeSql.FreeSqlBuilder().UseConnectionFactory(FreeSql.DataType.MySql,()=>throw new InvalidOperationException("离线禁止连接数据库")).UseAutoSyncStructure(false).Build();
            var orm=DispatchProxy.Create<IFreeSql,ReadOrmProxy>();((ReadOrmProxy)orm).Initialize(realOrm,db);
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="metarill_business_test",ConnectionString="Server=offline;Database=metarill_business_test;User ID=unused;Password=unused;"},orm);
            var auth=new SourceAuthorization();var serviceType=assembly.GetType(Ns+".Generated.Services.YzRoleService",true)!;
            var services=new ServiceCollection();services.AddSingleton(session);services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(869));services.AddSingleton<IRuntimeAuthorization>(auth);
            assembly.GetType(Ns+".Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[services,null]);
            using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();var service=scope.ServiceProvider.GetRequiredService(serviceType);
            var project=serviceType.GetMethod("ProjectRoleAdminNames"+FixedRoleAdminNameCompilation.RolePage+"Async",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var mapper=Activator.CreateInstance(assembly.GetType(Ns+".Generated.Mappers.YzRoleMapper",true)!,session)!;
            var missing=Activator.CreateInstance(serviceType,[session,mapper,new SnowflakeIdGenerator(870),Array.Empty<IRecordMutationObserver>(),Array.Empty<IRecordMutationGuard>(),null])!;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Result(project.Invoke(missing,[Rows(entity,[1]),auth.Actor,"list",CancellationToken.None])!));Assert.Empty(db.Commands);
            var inputs=Rows(entity,Enumerable.Range(1,205).Select(i=>(long?)i).Concat(new long?[]{1,null,999}).ToArray());
            var admin=assembly.GetType(Ns+".Generated.Entities.YzAdminType",true)!;
            db.Data[admin]=new[]{Admin(admin,1,"平台后台",100),Admin(admin,200,"边界后台",100),Admin(admin,201,"第二批后台",100),Admin(admin,999,"外项目后台",200),Admin(admin,3,"已删除后台",100,true)};
            var result=await Result(project.Invoke(service,[inputs,auth.Actor,"list",CancellationToken.None])!);
            Assert.Equal(2,db.Commands.Count);Assert.Equal(1,db.MaxConcurrent);var values=JsonSerializer.SerializeToElement(result,Json).EnumerateArray().ToArray();
            Assert.Equal("平台后台",values[0].GetProperty("yzAdminTypeName").GetString());Assert.Equal("第二批后台",values[200].GetProperty("yzAdminTypeName").GetString());
            Assert.Equal(JsonValueKind.Null,values[2].GetProperty("yzAdminTypeName").ValueKind);
            Assert.Equal("平台后台",values[205].GetProperty("yzAdminTypeName").GetString());Assert.Equal(JsonValueKind.Null,values[206].GetProperty("yzAdminTypeName").ValueKind);Assert.Equal(JsonValueKind.Null,values[207].GetProperty("yzAdminTypeName").ValueKind);
            Assert.All(db.Commands,sql=>{Assert.Contains("yz_admin_type",sql);Assert.Contains("delete_no",sql);Assert.Contains("yz_project_id",sql);Assert.Contains("100",sql);var select=sql[..sql.IndexOf("FROM",StringComparison.OrdinalIgnoreCase)];Assert.DoesNotContain("yz_project_id",select);Assert.DoesNotContain("delete_no",select);});
            Assert.All(auth.Requests,item=>Assert.Equal(FixedRoleAdminNameCompilation.RolePage+":list",item));
            db.Commands.Clear();auth.Requests.Clear();auth.Allowed="create";
            var empty=await Result(project.Invoke(service,[Rows(entity,[null]),auth.Actor,"create",CancellationToken.None])!);Assert.Empty(db.Commands);Assert.Single((Array)empty!);Assert.All(auth.Requests,item=>Assert.EndsWith(":create",item));
            var created=await Result(project.Invoke(service,[Rows(entity,[1]),auth.Actor,"create",CancellationToken.None])!);Assert.Equal("平台后台",JsonSerializer.SerializeToElement(created,Json)[0].GetProperty("yzAdminTypeName").GetString());Assert.All(auth.Requests,item=>Assert.EndsWith(":create",item));db.Commands.Clear();
            auth.Allowed="none";await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Result(project.Invoke(service,[Rows(entity,[1]),auth.Actor,"list",CancellationToken.None])!));Assert.Empty(db.Commands);
            auth.Allowed="list";await Assert.ThrowsAsync<InvalidOperationException>(()=>Result(project.Invoke(service,[Rows(entity,Enumerable.Range(1,5001).Select(i=>(long?)i).ToArray()),auth.Actor,"list",CancellationToken.None])!));Assert.Empty(db.Commands);
            // 公共分页方法也实际走补名，两个源记录只增加一次固定名称查询。
            db.Data[entity]=Rows(entity,[1,999]).Cast<object>().ToArray();foreach(var row in db.Data[entity])entity.GetProperty("YzProjectId")!.SetValue(row,100L);
            var pageResult=await Result(serviceType.GetMethod("Page"+FixedRoleAdminNameCompilation.RolePage+"Async")!.Invoke(service,[new PageRequest(),auth.Actor,CancellationToken.None])!);
            Assert.Equal(3,db.Commands.Count);var records=JsonSerializer.SerializeToElement(pageResult,Json).GetProperty("records");Assert.Equal("平台后台",records[0].GetProperty("yzAdminTypeName").GetString());Assert.Equal(JsonValueKind.Null,records[1].GetProperty("yzAdminTypeName").ValueKind);
            db.Commands.Clear();auth.Requests.Clear();auth.Allowed="detail";var detailResult=await Result(serviceType.GetMethod("Detail"+FixedRoleAdminNameCompilation.RolePage+"Async")!.Invoke(service,[1L,null,auth.Actor,CancellationToken.None])!);
            Assert.Equal(2,db.Commands.Count);Assert.Equal("平台后台",JsonSerializer.SerializeToElement(detailResult,Json).GetProperty("yzAdminTypeName").GetString());Assert.All(auth.Requests,item=>Assert.Equal(FixedRoleAdminNameCompilation.RolePage+":detail",item));
            // 模拟遗留手工构造漏传新依赖：真实生成写生命周期和DatabaseSession必须回滚，不能写后报错却提交。
            var transactions=new TransactionProvider();using(var writing=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline"},orm,transactions))
            {
                db.AllowWrites=true;var writingMapper=Activator.CreateInstance(assembly.GetType(Ns+".Generated.Mappers.YzRoleMapper",true)!,writing)!;
                var writingService=Activator.CreateInstance(serviceType,[writing,writingMapper,new SnowflakeIdGenerator(871),Array.Empty<IRecordMutationObserver>(),Array.Empty<IRecordMutationGuard>(),null])!;
                var requestType=assembly.GetType(Ns+".Generated.DTOs.YzRolePage"+FixedRoleAdminNameCompilation.RolePage+"CreateRequest",true)!;
                var request=JsonSerializer.Deserialize("{\"name\":\"离线回滚\",\"yzAdminTypeId\":1}",requestType,Json)!;
                await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Result(serviceType.GetMethod("Create"+FixedRoleAdminNameCompilation.RolePage+"Async")!.Invoke(writingService,[request,null,auth.Actor,CancellationToken.None])!));
                Assert.Contains("Insert",db.Writes);Assert.Equal(new[]{"begin","rollback"},transactions.Trace);Assert.Null(writing.CurrentTransaction);db.AllowWrites=false;
            }
            await RunVue(Path.Combine(generated,"frontend/pages/yzp/yz_role-detail-"+FixedRoleAdminNameCompilation.RolePage+".vue"));
        }
        finally {await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>隐藏源字段时实际生成仍能编译普通保存，VO/DTO和echo不开放计算别名。</summary>
    [Fact]
    public async Task Hidden_role_backend_id_does_not_leak_or_reference_the_alias()
    {
        var catalog=Catalog();catalog.ConfigPageColumns.Remove(Field(catalog));var boundary=Path.Combine(Path.GetTempPath(),"metarill-hidden-admin-name");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(directory,"generated");new CSharpGenerator().Generate(catalog,generated,Ns);
            foreach(var path in new[]{"VOs/YzRolePage"+FixedRoleAdminNameCompilation.RolePage+"Vo.g.cs","Controllers/YzRolePage"+FixedRoleAdminNameCompilation.RolePage+"Controller.g.cs"})Assert.DoesNotContain("yzAdminTypeName",File.ReadAllText(Path.Combine(generated,path)));
            Assert.DoesNotContain("ProjectRoleAdminNames",File.ReadAllText(Path.Combine(generated,"Services/YzRoleService.g.cs")));await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
        }
        finally {await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>只保留真实角色/后台稳定图，不为正例伪造缺失的业务列或权限。</summary>
    internal static MetadataCatalog Catalog([CallerFilePath]string source="")
    {
        var catalog=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../metadata/platform.json"))),Json)!;
        var tables=new[]{FixedRoleAdminNameCompilation.RoleTable,FixedRoleAdminNameCompilation.AdminTable};
        catalog.ConfigTables=catalog.ConfigTables.Where(item=>tables.Contains(item.Id)).ToList();
        catalog.ConfigColumns=catalog.ConfigColumns.Where(item=>tables.Contains(item.ConfigTableId)&&item.ColumnName is "id" or "name" or "yz_admin_type_id" or "yz_project_id" or "delete_no").ToList();
        catalog.ConfigPages=catalog.ConfigPages.Where(item=>item.Id is FixedRoleAdminNameCompilation.RolePage or FixedRoleAdminNameCompilation.AdminPage).ToList();
        var columns=catalog.ConfigColumns.Select(item=>item.Id).ToHashSet();var pages=catalog.ConfigPages.Select(item=>item.Id).ToHashSet();catalog.ConfigPageColumns=catalog.ConfigPageColumns.Where(item=>pages.Contains(item.ConfigPageId)&&columns.Contains(item.ConfigColumnId)).ToList();
        catalog.ConfigMenus=[];catalog.ConfigButtons=[];catalog.ConfigChildren=[];catalog.ConfigCustomPages=[];catalog.ConfigIndexes=[];catalog.ConfigIndexColumns=[];return catalog;
    }
    /// <summary>从同一真实生成模型定位固定源表页。</summary>
    private static (GenerationModel,TableModel,PageModel) Model(MetadataCatalog catalog){var model=GenerationModel.Create(catalog,Ns);var table=model.Tables.Single(item=>item.Source.Id==FixedRoleAdminNameCompilation.RoleTable);return(model,table,table.Pages.Single());}
    /// <summary>定位原页字段，避免测试修改其它页覆盖。</summary>
    private static PageColumnMetadata Field(MetadataCatalog catalog)=>catalog.ConfigPageColumns.Single(item=>item.ConfigPageId==FixedRoleAdminNameCompilation.RolePage&&item.ConfigColumnId==FixedRoleAdminNameCompilation.AdminColumn);
    /// <summary>JSON离开文档生命周期后仍保留完整类型。</summary>
    private static JsonElement Parse(string text){using var doc=JsonDocument.Parse(text);return doc.RootElement.Clone();}
    /// <summary>源码位置由编译期调用点确定，不依赖测试运行目录。</summary>
    private static string SourceRoot([CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
    /// <summary>等待真实生成方法，保留其原始异步异常。</summary>
    private static async Task<object?> Result(object value){var task=(Task)value;await task;return task.GetType().GetProperty("Result")!.GetValue(task);}
    /// <summary>构造已确定后台ID的内存源行，不伪称数据库记录。</summary>
    private static Array Rows(Type type,long?[] ids){var rows=Array.CreateInstance(type,ids.Length);for(var i=0;i<ids.Length;i++){var row=Activator.CreateInstance(type)!;type.GetProperty("Id")!.SetValue(row,(long)i+1);type.GetProperty("YzAdminTypeId")!.SetValue(row,ids[i]);rows.SetValue(row,i);}return rows;}
    /// <summary>用于同项目、跨项目及软删除固定谓词的内存目标行。</summary>
    private static object Admin(Type type,long id,string name,long project,bool deleted=false){var row=Activator.CreateInstance(type)!;type.GetProperty("Id")!.SetValue(row,id);type.GetProperty("Name")!.SetValue(row,name);type.GetProperty("YzProjectId")!.SetValue(row,project);if(deleted)type.GetProperty("DeleteNo")!.SetValue(row,DateTime.UtcNow);return row;}
    /// <summary>运行真实生成原详情与Vue，外部进程期限固定有界。</summary>
    private static async Task RunVue(string detail,[CallerFilePath]string file="")
    {
        var info=new ProcessStartInfo(Environment.GetEnvironmentVariable("NODE_HOST_PATH")??"node"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};info.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(file)!,"FixedRoleAdminNameVueProbe.mjs"));info.ArgumentList.Add(detail);
        using var process=Process.Start(info)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));await process.WaitForExitAsync(timeout.Token);Assert.True(process.ExitCode==0,await output+await error);
    }
    private sealed class SourceAuthorization:IRuntimeAuthorization
    {
        internal RuntimeActor Actor{get;}=new(1,"离线角色读取",false,"100","1");internal string Allowed="list";internal List<string> Requests{get;}=[];
        public RuntimeActor RequireActor()=>Actor;
        public void RequirePagePermission(string pageId,string operation){Requests.Add(pageId+":"+operation);if(pageId!=FixedRoleAdminNameCompilation.RolePage||operation!=Allowed)throw new UnauthorizedAccessException("仅允许原来源动作");}
    }
    /// <summary>严格区分翻译与执行：表达式交真实FreeSql翻译，行仅在内存执行已捕获的固定谓词。</summary>
    public sealed class OfflineRows
    {
        internal Dictionary<Type,object[]> Data{get;}=new();internal List<string> Commands{get;}=[];internal List<string> Writes{get;}=[];internal IFreeSql TranslationOrm=null!;internal bool AllowWrites;internal int Active,MaxConcurrent,PredicateDepth;
        internal async Task<T> Complete<T>(object? value){var active=Interlocked.Increment(ref Active);MaxConcurrent=Math.Max(MaxConcurrent,active);try{await Task.Yield();return (T)value!;}finally{Interlocked.Decrement(ref Active);}}
    }
    /// <summary>仅Select交真实FreeSql规划，写替身必须由回滚案例显式开启。</summary>
    public class ReadOrmProxy:DispatchProxy
    {
        private IFreeSql real=null!;private OfflineRows rows=null!;
        internal void Initialize(IFreeSql value,OfflineRows data){real=value;rows=data;rows.TranslationOrm=value;}
        protected override object? Invoke(MethodInfo? method,object?[]? args)
        {
            if(method!.Name is "Insert" or "Update" or "Delete")
            {
                if(!rows.AllowWrites)throw new InvalidOperationException("离线测试未授权写替身");
                var command=DispatchProxy.Create(method.ReturnType,typeof(WriteCommandProxy));((WriteCommandProxy)command).Initialize(rows,method.Name,command);return command;
            }
            if(method!.Name!="Select")throw new InvalidOperationException("离线替身只允许Select");
            var query=method.Invoke(real,args)!;var proxy=DispatchProxy.Create(method.ReturnType,typeof(ReadQueryProxy));
            ((ReadQueryProxy)proxy).Initialize(query,method.GetGenericArguments()[0],rows,proxy);return proxy;
        }
    }
    /// <summary>保留真实固定表达式和SQL翻译，仅终端行读取改为已声明的内存替身。</summary>
    public class ReadQueryProxy:DispatchProxy
    {
        private object real=null!,self=null!;private Type entity=null!;private OfflineRows rows=null!;private readonly List<System.Linq.Expressions.LambdaExpression> filters=[];
        internal void Initialize(object value,Type type,OfflineRows data,object proxy){real=value;entity=type;rows=data;self=proxy;}
        protected override object? Invoke(MethodInfo? method,object?[]? args)
        {
            args??=[];var lambda=args.OfType<System.Linq.Expressions.LambdaExpression>().FirstOrDefault();
            if(method!.Name == "Any")
            {
                if (rows.PredicateDepth == 0) throw new InvalidOperationException("同步Any只用于内存谓词中模拟同一SQL的固定EXISTS。");
                IEnumerable<object> data=rows.Data.GetValueOrDefault(entity)??[];
                foreach(var filter in filters) {var predicate=filter.Compile();data=data.Where(item=>(bool)predicate.DynamicInvoke(item)!);}
                return data.Any();
            }
            if(method.Name is "ToListAsync" or "CountAsync" or "FirstAsync" or "ToOneAsync")
            {
                rows.Commands.Add((string)typeof(ReadQueryProxy).GetMethod(nameof(Sql),BindingFlags.Static|BindingFlags.NonPublic)!.MakeGenericMethod(entity,lambda?.ReturnType??entity).Invoke(null,[real,lambda,method.Name=="CountAsync"])!);
                IEnumerable<object> data=rows.Data.GetValueOrDefault(entity)??[];foreach(var filter in filters){var predicate=filter.Compile();data=data.Where(item=>(bool)predicate.DynamicInvoke(item)!);}
                object? result;rows.PredicateDepth++;
                try {if(method.Name=="CountAsync")result=data.LongCount();else if(method.Name is "FirstAsync" or "ToOneAsync")
                {
                    // 显式单行投影执行同一表达式；无匹配行仍返回null，不能回传完整实体掩盖缺列。
                    var first=data.FirstOrDefault();result=first is null||lambda is null?first:lambda.Compile().DynamicInvoke(first);
                }
                else{var type=method.ReturnType.GetGenericArguments()[0];var list=(System.Collections.IList)Activator.CreateInstance(type)!;var project=lambda?.Compile();foreach(var item in data)list.Add(project is null?item:project.DynamicInvoke(item));result=list;}}
                finally {rows.PredicateDepth--;}
                return typeof(OfflineRows).GetMethod(nameof(OfflineRows.Complete),BindingFlags.Instance|BindingFlags.NonPublic)!.MakeGenericMethod(method.ReturnType.GetGenericArguments()[0]).Invoke(rows,[result]);
            }
            if(method.Name=="Where"&&lambda is not null)filters.Add(lambda);
            // 翻译使用真实provider，内存谓词仍用原proxy模拟同一SQL中的相关EXISTS；不让ORM反射测试代理私有状态。
            if(lambda is not null){args=(object?[])args.Clone();for(var i=0;i<args.Length;i++)if(ReferenceEquals(args[i],lambda))args[i]=new SqlProviderVisitor(rows.TranslationOrm).Visit(lambda);}
            var value=method.Invoke(real,args);return method.ReturnType.IsInstanceOfType(self)?self:value;
        }
        /// <summary>只在SQL翻译副本替换固定ORM实例，业务过滤表达式和测试数据不改变。</summary>
        private sealed class SqlProviderVisitor(IFreeSql orm):System.Linq.Expressions.ExpressionVisitor
        {
            protected override System.Linq.Expressions.Expression VisitMember(System.Linq.Expressions.MemberExpression node)
                => node.Type==typeof(IFreeSql)?System.Linq.Expressions.Expression.Constant(orm,typeof(IFreeSql)):base.VisitMember(node);
        }
        /// <summary>只调用真实ToSql，连接工厂始终拒绝打开数据库。</summary>
        private static string Sql<T,TResult>(object query,System.Linq.Expressions.LambdaExpression? select,bool count)where T:class=>count?((FreeSql.ISelect<T>)query).ToSql("count(1)"):select is null?((FreeSql.ISelect<T>)query).ToSql():((FreeSql.ISelect<T>)query).ToSql((System.Linq.Expressions.Expression<Func<T,TResult>>)select);
    }
    /// <summary>只记录生成Mapper的调用，不生成或执行写SQL；提交/回滚由真实DatabaseSession驱动。</summary>
    public class WriteCommandProxy:DispatchProxy
    {
        private OfflineRows rows=null!;private string operation="";private object self=null!;
        internal void Initialize(OfflineRows data,string action,object value){rows=data;operation=action;self=value;}
        protected override object? Invoke(MethodInfo? method,object?[]? args){if(method!.Name=="ExecuteAffrowsAsync"){rows.Writes.Add(operation);return Task.FromResult(1);}return self;}
    }
    /// <summary>只为真实DatabaseSession提供本机事务对象，绝不创建数据库连接或ORM。</summary>
    private sealed class TransactionProvider:IDatabaseProvider
    {
        internal List<string> Trace{get;}=[];public string Name=>"mysql";public DatabaseCapabilities Capabilities=>new("mysql",true,false);public void Validate(DatabaseOptions options){}
        public string QuoteIdentifier(string value)=>new MySqlDatabaseProvider().QuoteIdentifier(value);public string ParameterPlaceholder(string value)=>"@"+value;public IFreeSql CreateOrm(DatabaseOptions options)=>throw new NotSupportedException();public DbConnection CreateConnection(DatabaseOptions options)=>new TransactionConnection(Trace);
    }
    private sealed class TransactionConnection(List<string> trace):DbConnection
    {
        private ConnectionState state;[System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="";
        public override string Database=>"offline";public override string DataSource=>"offline";public override string ServerVersion=>"offline";public override ConnectionState State=>state;
        public override void Open()=>state=ConnectionState.Open;public override void Close()=>state=ConnectionState.Closed;public override void ChangeDatabase(string value)=>throw new NotSupportedException();protected override DbCommand CreateDbCommand()=>throw new InvalidOperationException("离线禁止真实命令");protected override DbTransaction BeginDbTransaction(IsolationLevel level){trace.Add("begin");return new Transaction(this,trace);}
    }
    private sealed class Transaction(DbConnection connection,List<string> trace):DbTransaction
    {
        protected override DbConnection DbConnection=>connection;public override IsolationLevel IsolationLevel=>IsolationLevel.RepeatableRead;public override void Commit()=>trace.Add("commit");public override void Rollback()=>trace.Add("rollback");
    }
}
