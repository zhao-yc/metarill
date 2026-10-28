using System.Reflection;
using System.Text.Json;
using FreeSql;
using Metarill.Core.Metadata;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>实际生成/编译按钮记录入口并检查ORM真实SQL；不连接数据库，不冒充锁并发验收。</summary>
public sealed class BusinessRecordScopeGenerationTests
{
    /// <summary>物理/非冗余/派生范围均直接进入记录SQL，普通query权限不参与，所有反例在连接前拒绝。</summary>
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(true,true)]
    public async Task Actual_button_record_query_keeps_fixed_scope_and_parameterized_own_values(bool nonredundant,bool derived)
    {
        var catalog=ExactDataManageGenerationTests.Catalog(nonredundant);var page=catalog.ConfigPages.Single(page=>page.Id=="101");
        if(derived)
        {
            page.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("customer_notes");
            foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="16")){field.IsShow=0;field.IsEdit=0;field.Extra["isInGrid"]=JsonSerializer.SerializeToElement(0);field.Extra["isInEdit"]=JsonSerializer.SerializeToElement(0);}
        }
        page.WhereSql+=(string.IsNullOrWhiteSpace(page.WhereSql)?"":" AND ")+(nonredundant?"private_payload":"name")+" IS NOT NULL";
        catalog.ConfigButtons.Add(JsonSerializer.SerializeToElement(new{id="86000",configPageId=page.Id,yzProjectId=catalog.YzProjectId,actionType=1,name="固定业务",sql="/admin/custom/scope/probe"}));
        var model=GenerationModel.Create(catalog,"Example.ButtonRecordScope");var table=model.Tables.Single(table=>table.Source.Id=="1");var boundary=Path.Combine(Path.GetTempPath(),"metarill-button-record-scope");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);
            // 三种真实生成模型都采用零警告门禁，不只依赖外层测试项目的编译参数。
            await File.WriteAllTextAsync(Path.Combine(directory,"Directory.Build.props"),"<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
            var actor=new RuntimeActor(8,"按钮记录范围",false,catalog.YzProjectId,page.YzAdminTypeId);var authorization=new Authorization(actor);
            var type=assembly.GetType(model.Namespace+".Generated.Services."+table.Name+"Service",true)!;var entity=assembly.GetType(model.Namespace+".Generated.Entities."+table.Name,true)!;
            var access=Assert.Single(assembly.GetTypes().Select(controller=>controller.GetCustomAttribute<GeneratedPageAccessAttribute>()).OfType<GeneratedPageAccessAttribute>(),value=>value.PageId==page.Id);
            Assert.Equal(table.Source.Id,access.TableId);Assert.Equal(type,access.ServiceType);Assert.Equal(entity,access.EntityType);Assert.Equal("ReadButtonRecord"+page.Id+"Async",access.ReadMethod);
            var mapper=Activator.CreateInstance(assembly.GetType(model.Namespace+".Generated.Mappers."+table.Name+"Mapper",true)!,session)!;
            var service=Activator.CreateInstance(type,[session,mapper,new SnowflakeIdGenerator(945),Array.Empty<IRecordMutationObserver>(),Array.Empty<IRecordMutationGuard>(),authorization])!;
            var scope=type.GetMethod("ButtonRecordQuery101",BindingFlags.Instance|BindingFlags.NonPublic)!;var parent=nonredundant?"71":null;const long id=9007199254740993;
            object Query(long key=id,string button="86000",RuntimeActor? requested=null,bool locking=false)=>scope.Invoke(service,[key,parent,button,requested??actor,locking])!;
            string Sql()=>SqlOf(entity,Query());
            var sql=Sql();Assert.Equal("86000",authorization.LastButton);Assert.Equal(0,authorization.PageChecks);Assert.DoesNotContain("9007199254740993",sql);Assert.Contains("IS NOT NULL",sql,StringComparison.OrdinalIgnoreCase);Assert.Contains("IN",sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("只允许甲",sql);Assert.DoesNotContain("只允许乙",sql);
            foreach(var (column,_) in FixedPageQueryCompilation.For(table,table.Pages.Single(page=>page.Source.Id=="101")).IdentityScopes)Assert.Contains("`"+column.Source.ColumnName+"`",sql);
            Assert.Contains("`delete_no`",sql);if(derived){Assert.Contains("EXISTS",sql);Assert.DoesNotContain("FOR UPDATE",sql);}
            // 截获ORM真正准备执行的命令，在建立连接前中止；大ID必须是long参数，范围值也不能被拼成SQL。
            string? actualSql=null;object?[] actualParameters=[];
            session.Orm.Aop.CurdBefore+=(_,args)=>{actualSql=args.Sql;actualParameters=args.DbParms.Select(parameter=>parameter.Value).ToArray();throw new CapturedSql();};
            await Assert.ThrowsAsync<CapturedSql>(async()=>await(Task)type.GetMethod("ReadButtonRecord101Async")!.Invoke(service,[id,parent,"86000",actor,CancellationToken.None])!);
            Assert.NotNull(actualSql);Assert.Contains(actualParameters,value=>value is long key&&key==id);Assert.Contains("只允许甲",actualParameters);Assert.Contains("只允许乙",actualParameters);Assert.DoesNotContain("只允许甲",actualSql);Assert.DoesNotContain("9007199254740993",actualSql);
            authorization.Values="[]";Assert.Contains("1=0",Sql());authorization.Values="[{}]";Assert.IsType<ArgumentException>(Failure(()=>Query()));
            authorization.Restricted=false;var unrestricted=Sql();Assert.DoesNotContain(" IN (",unrestricted);Assert.Contains("IS NOT NULL",unrestricted,StringComparison.OrdinalIgnoreCase);
            var checks=authorization.ButtonChecks;Assert.IsType<UnauthorizedAccessException>(Failure(()=>Query(button:"86001")));Assert.Equal(checks,authorization.ButtonChecks);
            Assert.IsType<UnauthorizedAccessException>(Failure(()=>Query(requested:actor with{AccountId=9})));Assert.IsType<ArgumentException>(Failure(()=>Query(key:0)));
            authorization.Allow=false;Assert.IsType<UnauthorizedAccessException>(Failure(()=>Query()));authorization.Allow=true;
            var lockError=Assert.IsType<InvalidOperationException>(Failure(()=>Query(locking:true)));Assert.Contains("真实写事务",lockError.Message);
            Assert.NotNull(type.GetMethod("ReadButtonRecord101Async"));Assert.NotNull(type.GetMethod("LockButtonRecord101Async"));
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>反射入口只解包实际生成方法抛出的异常，不用替身断言代替生成执行。</summary>
    private static Exception Failure(Func<object> action)=>Assert.Throws<TargetInvocationException>(()=>action()).InnerException!;
    /// <summary>真实ORM按生成实体解析SQL，连接保持未打开。</summary>
    private static string SqlOf(Type entity,object query)=>(string)typeof(BusinessRecordScopeGenerationTests).GetMethod(nameof(TypedSql),BindingFlags.NonPublic|BindingFlags.Static)!.MakeGenericMethod(entity).Invoke(null,[query])!;
    /// <summary>类型只在生成后已知，运行时仍执行FreeSql原ToSql。</summary>
    private static string TypedSql<T>(object query)where T:class=>((ISelect<T>)query).ToSql();
    /// <summary>仅用于命令准备阶段中止，不允许测试继续建立真实连接。</summary>
    private sealed class CapturedSql:Exception;
    private sealed class Authorization(RuntimeActor actor):IRuntimeAuthorization
    {
        internal bool Restricted=true,Allow=true;internal string Values="[\"只允许甲\",\"只允许乙\"]",LastButton="";internal int ButtonChecks,PageChecks;
        /// <summary>固定当前真实身份，不能由生成方法换主体。</summary>
        public RuntimeActor RequireActor()=>actor;
        /// <summary>业务记录入口不能索取普通查询权；发生调用就明确失败。</summary>
        public void RequirePagePermission(string pageId,string operation){PageChecks++;throw new UnauthorizedAccessException("未授普通查询权。");}
        /// <summary>这里只提供已计算权限；实际角色决策由共享安全模块独立验证。</summary>
        public void RequireButtonPermission(string pageId,string buttonId){ButtonChecks++;Assert.Equal("101",pageId);Assert.Equal("86000",buttonId);if(!Allow)throw new UnauthorizedAccessException();}
        /// <summary>传入的固定字段必须与编译声明一致，数据按账号原JSON形状提供。</summary>
        public ExactDataManageValues ResolveButtonExactDataManage(string pageId,string buttonId,string managerField)
        {RequireButtonPermission(pageId,buttonId);Assert.Equal("real_name",managerField);LastButton=buttonId;return Restricted?new(true,JsonSerializer.SerializeToElement(Values)):ExactDataManageValues.Unrestricted;}
    }
}
