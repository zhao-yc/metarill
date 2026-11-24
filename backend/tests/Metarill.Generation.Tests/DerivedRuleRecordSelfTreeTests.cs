using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class DerivedRuleRecordGenerationTests
{
    /// <summary>仅被规则引用的自树叶名也需按发布页读取。真实生成入口和事务内规则读使用同一固定树路径。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Self_tree_leaf_used_only_in_record_rules_respects_the_published_tree(bool derived)
    {
        const string application="Example.RuleTree";
        const string prefix="rule_tree";
        const long storeId=9007199254741993L;
        var catalog=derived?DerivedSelfTreeTests.Catalog(prefix,"int64","string",false):
            RedundantMultiNameCacheTests.Published(NonRedundantSelfTreeTests.Catalog(prefix,"string","int64"));
        var source=catalog.ConfigPages.Single(page=>page.Id=="201");
        source.Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement("treeName == '受限叶名'");
        source.Extra["isUpdate"]=JsonSerializer.SerializeToElement(1);
        catalog.ConfigButtons.Add(JsonSerializer.SerializeToElement(new{id="86000",configPageId=source.Id,
            yzProjectId=catalog.YzProjectId,actionType=1,name="按树叶规则执行",sql="/admin/custom/derived-rule/probe",visibleScheme="VisibleWhenWritable"}));
        var target=catalog.ConfigPages.Single(page=>page.Id=="301");
        target.WhereSql+=" AND store_id=@current_store_id";
        catalog.ConfigColumns.Add(new(){Id="93",ConfigTableId="3",YzProjectId=catalog.YzProjectId,ColumnName="store_id",LogicalType="int64"});

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-rule-tree");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var model=GenerationModel.Create(catalog,application);
            var table=model.Tables.Single(item=>item.Source.Id=="2");
            var page=Assert.Single(table.Pages);
            Assert.DoesNotContain(page.Searchable.Concat(page.Sortable),column=>column.Source.ColumnName=="tree_name");
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),application);
            await File.WriteAllTextAsync(Path.Combine(directory,"Directory.Build.props"),
                "<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var serviceType=assembly.GetType(application+".Generated.Services."+table.Name+"Service",true)!;
            var plan=Assert.IsType<NonRedundantReadPlan>(serviceType.GetField("RuleRecordPlan201",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null));
            Assert.True(plan.RequiresAccount);

            string? leaf="受限叶名";
            var transport=new Transport(plan.OutputTypes){ProjectId=catalog.YzProjectId,
                RowValue=(name,_)=>name switch
                {
                    "id"=>Transport.Id, "yz_project_id"=>catalog.YzProjectId, "name"=>"来源行",
                    "tree_id"=>derived?(object)101L:"101", "tree_name"=>leaf, _=>null
                }};
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",
                ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"},null,transport);
            var actor=new RuntimeActor(8,"自树规则",false,catalog.YzProjectId,source.YzAdminTypeId){StoreId=storeId};
            var authorization=new Authorization(actor,"201");
            var mapper=Activator.CreateInstance(assembly.GetType(application+".Generated.Mappers."+table.Name+"Mapper",true)!,session)!;
            var service=Activator.CreateInstance(serviceType,[session,mapper,new SnowflakeIdGenerator(949),
                Array.Empty<IRecordMutationObserver>(),Array.Empty<IRecordMutationGuard>(),authorization])!;
            var rule=ReadOnlyConditionRule.Compile("treeName == '受限叶名'");
            async Task VerifyRead(string method, BindingFlags binding, object?[] args, bool locking)
            {
                transport.Commands.Clear();
                var execution=Assert.IsAssignableFrom<Task>(serviceType.GetMethod(method,binding)!.Invoke(service,args));
                await execution;
                var record=Assert.IsAssignableFrom<IGeneratedRuleRecord>(execution.GetType().GetProperty("Result")!.GetValue(execution));
                Assert.True(record.TryGetLoadedRuleValue("tree_name",out var value));
                Assert.Equal(leaf,value);
                Assert.Equal(leaf is not null,rule.IsReadOnly(new Dictionary<string,object?>{{"treeName",value}}));
                var command=Assert.Single(transport.Commands);
                Assert.Contains("WITH RECURSIVE",command.Sql);
                Assert.Contains("JSON_CONTAINS",command.Sql);
                Assert.Contains(prefix+"_tree",command.Sql);
                Assert.Contains(".`delete_no`",command.Sql);
                Assert.Contains(".`store_id`",command.Sql);
                Assert.Contains(".`status`",command.Sql);
                Assert.Contains(command.Values,value=>value is long accountStore&&accountStore==storeId);
                Assert.Contains(command.Values,value=>value is int integer&&integer==1||value is long number&&number==1L);
                Assert.DoesNotContain(storeId.ToString(),command.Sql);
                Assert.Equal(locking,command.Sql.Contains("FOR SHARE",StringComparison.Ordinal));
                if(locking)
                {
                    Assert.Same(session.CurrentTransaction,command.Transaction);
                    Assert.True(System.Text.RegularExpressions.Regex.Matches(command.Sql,"FOR SHARE").Count>=3);
                }
                if(derived)Assert.Contains(prefix+"_company",command.Sql);
            }
            await VerifyRead("ReadAgentRecord201Async",BindingFlags.Public|BindingFlags.Instance,
                [Transport.Id,null,"update",actor,authorization,CancellationToken.None],false);
            await VerifyRead("ReadButtonRecord201Async",BindingFlags.Public|BindingFlags.Instance,
                [Transport.Id,null,"86000",actor,CancellationToken.None],false);
            leaf=null;
            await session.InTransactionAsync(async()=>
            {
                await VerifyRead("ReadRuleRecord201CoreAsync",BindingFlags.NonPublic|BindingFlags.Instance,
                    [Transport.Id,null,"86000",actor,true,true,CancellationToken.None],true);
                return true;
            });
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
