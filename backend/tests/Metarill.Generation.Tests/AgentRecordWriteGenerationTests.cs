using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>实际生成/编译记录写预检入口；截获真实ORM参数化命令，不连接数据库。</summary>
public sealed class AgentRecordWriteGenerationTests
{
    /// <summary>三种范围对每个实际写操作读取原记录，不索取query权限，也不在预检阶段获取写锁。</summary>
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(true,true)]
    public async Task Generated_record_precheck_uses_operation_scope_and_parameterized_original_id(bool nonredundant,bool derived)
    {
        var catalog=ExactDataManageGenerationTests.Catalog(nonredundant);var page=catalog.ConfigPages.Single(page=>page.Id=="101");
        // 三个操作明确开启；不依赖其它夹具将来调整的默认按钮设置。
        page.Extra["isUpdate"]=JsonSerializer.SerializeToElement(1);page.IsDelete=1;page.IsCopy=1;
        if(derived)
        {
            page.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("customer_notes");
            foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="16"))
            {field.IsShow=0;field.IsEdit=0;field.Extra["isInGrid"]=JsonSerializer.SerializeToElement(0);field.Extra["isInEdit"]=JsonSerializer.SerializeToElement(0);}
        }
        page.WhereSql+=(string.IsNullOrWhiteSpace(page.WhereSql)?"":" AND ")+(nonredundant?"private_payload":"name")+" IS NOT NULL";
        var model=GenerationModel.Create(catalog,"Example.AgentRecordScope");var table=model.Tables.Single(table=>table.Source.Id=="1");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-agent-record-scope");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);
            await File.WriteAllTextAsync(Path.Combine(directory,"Directory.Build.props"),"<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
            var actor=new RuntimeActor(8,"记录写预检",false,catalog.YzProjectId,page.YzAdminTypeId);var authorization=new Authorization(actor);
            var serviceType=assembly.GetType(model.Namespace+".Generated.Services."+table.Name+"Service",true)!;
            var entity=assembly.GetType(model.Namespace+".Generated.Entities."+table.Name,true)!;
            var marker=Assert.Single(assembly.GetTypes().Select(type=>type.GetCustomAttribute<GeneratedAgentWriteAccessAttribute>()).OfType<GeneratedAgentWriteAccessAttribute>(),marker=>marker.PageId==page.Id);
            Assert.Equal(catalog.YzProjectId,marker.ProjectId);Assert.Equal(table.Source.Id,marker.TableId);Assert.Equal(serviceType,marker.ServiceType);Assert.Equal(entity,marker.EntityType);Assert.Equal("ReadAgentRecord101Async",marker.ReadMethod);
            var mapper=Activator.CreateInstance(assembly.GetType(model.Namespace+".Generated.Mappers."+table.Name+"Mapper",true)!,session)!;
            var service=Activator.CreateInstance(serviceType,[session,mapper,new SnowflakeIdGenerator(946),Array.Empty<IRecordMutationObserver>(),Array.Empty<IRecordMutationGuard>(),authorization])!;
            var reader=serviceType.GetMethod(marker.ReadMethod)!;var parent=nonredundant?"71":null;const long id=9007199254740993;
            string? sql=null;object?[] parameters=[];var commands=0;
            // 在命令真正准备完成后、连接打开前终止；不能用模板文本断言替代SQL实证。
            session.Orm.Aop.CurdBefore+=(_,args)=>{commands++;sql=args.Sql;parameters=args.DbParms.Select(parameter=>parameter.Value).ToArray();throw new CapturedSql();};
            Task Read(string operation,long key=id,RuntimeActor? requested=null)=>(Task)reader.Invoke(service,[key,parent,operation,requested??actor,authorization,CancellationToken.None])!;
            foreach(var operation in new[]{"update","delete","copy"})
            {
                authorization.Operation=operation;authorization.LastScope="";
                await Assert.ThrowsAsync<CapturedSql>(()=>Read(operation));Assert.Equal(operation,authorization.LastScope);Assert.NotNull(sql);
                Assert.Contains(parameters,value=>value is long key&&key==id);Assert.Contains("只允许甲",parameters);Assert.Contains("只允许乙",parameters);
                Assert.DoesNotContain("9007199254740993",sql);Assert.DoesNotContain("只允许甲",sql);Assert.Contains("IS NOT NULL",sql,StringComparison.OrdinalIgnoreCase);Assert.Contains("`delete_no`",sql);Assert.DoesNotContain("FOR UPDATE",sql,StringComparison.OrdinalIgnoreCase);
                foreach(var(column,_)in FixedPageQueryCompilation.For(table,table.Pages.Single(value=>value.Source.Id=="101")).IdentityScopes)Assert.Contains("`"+column.Source.ColumnName+"`",sql);
                if(derived)Assert.Contains("EXISTS",sql);if(nonredundant)Assert.Contains("71",parameters);
            }
            var count=commands;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Read("query"));await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Read("update",requested:actor with{AccountId=9}));await Assert.ThrowsAsync<ArgumentException>(()=>Read("update",key:0));
            authorization.Allow=false;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Read("copy"));Assert.Equal(count,commands);
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>只拦截准备好的ORM命令，保证本类没有任何实库访问。</summary>
    private sealed class CapturedSql:Exception;
    /// <summary>这里只提供已计算的当前操作权限与账号范围，真实角色认证由安全模块验收。</summary>
    private sealed class Authorization(RuntimeActor actor):IRuntimeAuthorization
    {
        internal bool Allow=true;internal string Operation="update",LastScope="";
        public RuntimeActor RequireActor()=>actor;
        /// <summary>额外query读取或其它操作都必须明确失败。</summary>
        public void RequirePagePermission(string pageId,string operation)
        {Assert.Equal("101",pageId);if(!Allow||operation!=Operation)throw new UnauthorizedAccessException("仅授当前写操作。");}
        /// <summary>记录生成入口传入的真实操作，返回原账号JSON数组形状。</summary>
        public ExactDataManageValues ResolveExactDataManage(string pageId,string operation,string managerField)
        {RequirePagePermission(pageId,operation);Assert.Equal("real_name",managerField);LastScope=operation;return new(true,JsonSerializer.SerializeToElement("[\"只允许甲\",\"只允许乙\"]"));}
    }
}
