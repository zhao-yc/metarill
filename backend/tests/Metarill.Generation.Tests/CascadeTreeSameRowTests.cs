using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>同表非父关联的正式生成编译，实际数据库语义另走MySQL验收。</summary>
public sealed class CascadeTreeSameRowTests
{
    /// <summary>四种ID/父键组合实际编译创建、更新、复制、导入和不同来源页的事务后验入口。</summary>
    [Theory]
    [InlineData("string","string",false)][InlineData("string","string",true)]
    [InlineData("string","int64",false)][InlineData("string","int64",true)]
    [InlineData("int64","string",false)][InlineData("int64","string",true)]
    [InlineData("int64","int64",false)][InlineData("int64","int64",true)]
    public async Task Final_storage_validation_compiles_for_every_write_entry(string sourceType,string parentType,bool nonRedundant)
    {
        var catalog=Catalog("same",sourceType,parentType);if(nonRedundant)catalog.ConfigTables.Single().IsRongyu=0;var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.SameRow");var table=Assert.Single(model.Tables);var page=table.Pages.Single(page=>page.Source.Id=="201");var plan=Assert.Single(CascadeTreeWriteCompilation.ForService(model,table,page));Assert.True(CascadeTreeMergedWriteRenderer.Applies(table,plan));
        Assert.Contains("submitted.Contains(\"amount\")",CascadeTreeMergedWriteRenderer.Trigger(table,plan));Assert.Contains("submitted.Contains(\"parent_id\")",CascadeTreeMergedWriteRenderer.Trigger(table,plan));Assert.Contains("ValidateStoredCascade201_6Async",CascadeTreeMergedWriteRenderer.After(model,table,page,false,true));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cascade-same-row");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.SameRow");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType("Example.SameRow.Generated.Controllers.SameNodePage203Controller",true));
            var hand=Path.Combine(directory,"generated/Services/SameNodeService.cs");var bytes=File.ReadAllBytes(hand);new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.SameRow");Assert.Equal(bytes,File.ReadAllBytes(hand));Assert.Equal(before,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>实际编译同表业务键的直接父和书签，核对锁内图、写后存储及 Excel。</summary>
    [Theory]
    [InlineData(0,"string","int64",false,false)]
    [InlineData(0,"int64","string",false,false)]
    [InlineData(1,"string","int64",false,false)]
    [InlineData(1,"int64","string",false,false)]
    [InlineData(0,"string","string",true,false)]
    [InlineData(1,"string","string",true,false)]
    [InlineData(1,"string","string",true,true)]
    public async Task Same_table_business_key_drives_topology_bookmarks_and_excel(int redundant,string sourceType,string parentType,bool textKey,bool typedLabel)
    {
        const string ns="Example.SameBusinessKey";
        var catalog=RedundantMultiNameCacheTests.Published(Catalog("same_key",sourceType,parentType));
        catalog.ConfigTables.Single().IsRongyu=redundant;
        catalog.ConfigColumns.Single(column=>column.Id=="1").Id="10001";
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigColumnId=="1"))
            field.ConfigColumnId="10001";
        var details=JsonSerializer.Serialize(new
        {
            sourceMode="selfTree",selfTree=new{table="same_key_node",parentField="parent_id",nameField=typedLabel?"node_label":"name",idField="node_key"}
        });
        var parent=catalog.ConfigColumns.Single(column=>column.Id=="5");
        parent.ControlKind="CascadeTree";
        parent.ControlDetail=details;
        catalog.ConfigColumns.Single(column=>column.Id=="6").ControlDetail=details;
        catalog.ConfigColumns.Add(new(){Id="10",ConfigTableId="1",YzProjectId=catalog.YzProjectId,Name="节点键",
            ColumnName="node_key",LogicalType=textKey?"string":"int64",ColumnLength="48",IsDbNotNull=1,SortNo=9});
        if(typedLabel)
        {
            catalog.ConfigColumns.Add(new(){Id="11",ConfigTableId="1",YzProjectId=catalog.YzProjectId,
                Name="节点号",ColumnName="node_label",LogicalType="int32",SortNo=10});
            catalog.ConfigPageColumns.Add(new(){Id="202_label",ConfigPageId="202",ConfigColumnId="11",
                YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=1,SortNo=10});
        }
        foreach(var page in catalog.ConfigPages)
        {
            page.IsSave=1;
            if(page.Id=="202")
            {
                page.IsImport=1;
                if(parentType=="string")page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
            }
            page.IsExport=1;
            catalog.ConfigPageColumns.Add(new(){Id=page.Id+"_key",ConfigPageId=page.Id,ConfigColumnId="10",
                YzProjectId=catalog.YzProjectId,IsEdit=1,SortNo=9});
        }
        catalog.ConfigIndexes.Add(JsonSerializer.SerializeToElement(new{id="701",configTableId="1",indexName="uq_node_key",
            isUnique=1,isEnabled=1,yzProjectId=catalog.YzProjectId}));
        catalog.ConfigIndexColumns.Add(JsonSerializer.SerializeToElement(new{id="702",configIndexId="701",
            configColumnId="2",sortNo=10,yzProjectId=catalog.YzProjectId}));
        catalog.ConfigIndexColumns.Add(JsonSerializer.SerializeToElement(new{id="703",configIndexId="701",
            configColumnId="10",sortNo=20,yzProjectId=catalog.YzProjectId}));

        var model=GenerationModel.Create(catalog,ns);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-same-key");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,generated,ns);
            var service=await File.ReadAllTextAsync(Path.Combine(generated,"Services/SameKeyNodeService.g.cs"));
            if(textKey)
            {
                Assert.Contains("row.Id>after",service);
                Assert.Contains("TextCascadeTreeSql.Match",service);
                Assert.Contains("FilterIdsAsync(storedKey,false,cancellationToken)",service);
                Assert.Contains("FilterIdsAsync(stored.NodeKey!,false,cancellationToken)",service);
                Assert.Contains("node.Id==entity.NodeKey!",service);
                Assert.Contains("if(id==entity.NodeKey!)",service);
            }
            else
            {
                Assert.Contains("row.NodeKey>after",service);
                Assert.Contains("row=>row.NodeKey==id).ForUpdate()",service);
                Assert.Contains("FilterIdsAsync(storedKey.Value,false,cancellationToken)",service);
                Assert.Contains("FilterIdsAsync(stored.NodeKey.GetValueOrDefault(),false,cancellationToken)",service);
                Assert.Contains("node.Id==entity.NodeKey.GetValueOrDefault().ToString",service);
                Assert.Contains("if(id==entity.NodeKey.GetValueOrDefault())",service);
            }
            Assert.Contains("storedKey!=entity.NodeKey",service);
            Assert.Contains("stored.NodeKey!=entity.NodeKey",service);
            Assert.Contains("ImportCascade201_6Async",service);
            Assert.Contains("ImportCascade203_6Async",service);
            Assert.Contains("ImportCascade202_5Async",service);
            if(redundant==1)Assert.Contains("[\"node_key\"] = entity.NodeKey",service);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            Assert.NotNull(assembly.GetType(ns+".Generated.Controllers.SameKeyNodePage202Controller",true));
            if(redundant==1)
            {
                var declaration=assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!;
                var maintenance=declaration.GetProperty("Catalog")!.GetValue(null)!;
                const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(maintenance.GetType().GetProperty("Plans",hidden)!.GetValue(maintenance));
                var plan=plans["1"] ?? throw new InvalidOperationException("同表树缺少维护计划。");
                Assert.Null(plan.GetType().GetProperty("Error")!.GetValue(plan));
            }
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>书签引用本表时真实生成的写后校验会比较已提交的字典分组。</summary>
    [Fact]
    public async Task Same_table_bookmark_checks_submitted_dictionary_dependencies_after_write()
    {
        const string ns="Example.SameDictionary";
        var catalog=RedundantMultiNameCacheTests.Published(Catalog("same_dictionary","string","int64"));
        catalog.ConfigTables.Single().IsRongyu=0;
        catalog.ConfigColumns.Single(column=>column.Id=="1").Id="10001";
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigColumnId=="1"))
            field.ConfigColumnId="10001";
        var bookmark=catalog.ConfigColumns.Single(column=>column.Id=="6");
        bookmark.ControlDetail="""{"sourceMode":"selfTree","selfTree":{"table":"same_dictionary_node","parentField":"parent_id","nameField":"display_label"}}""";
        catalog.ConfigTables.Add(new(){Id="90",TableName="same_dictionary_company",IsRongyu=1,
            YzProjectId=catalog.YzProjectId,Extra=new(){["scopeKind"]=JsonSerializer.SerializeToElement("global")}});
        catalog.ConfigColumns.AddRange([
            new(){Id="901",ConfigTableId="90",ColumnName="id",LogicalType="int64",IsDbNotNull=1,YzProjectId=catalog.YzProjectId},
            new(){Id="902",ConfigTableId="90",ColumnName="name",LogicalType="string",YzProjectId=catalog.YzProjectId},
            new(){Id="903",ConfigTableId="90",ColumnName="segment",LogicalType="int32",YzProjectId=catalog.YzProjectId},
            new(){Id="904",ConfigTableId="90",ColumnName="store_id",LogicalType="int64",YzProjectId=catalog.YzProjectId},
            new(){Id="905",ConfigTableId="90",ColumnName="status",LogicalType="int32",YzProjectId=catalog.YzProjectId},
            new(){Id="11",ConfigTableId="1",ColumnName="company_id",LogicalType="int64",ControlKind="List",
                LinkConfigTableId="90",YzProjectId=catalog.YzProjectId,
                ControlDetail="""{"tableId":"90","pageId":"9010","nameField":"name"}"""},
            new(){Id="12",ConfigTableId="1",ColumnName="company_group",LogicalType="int32",YzProjectId=catalog.YzProjectId},
            new(){Id="13",ConfigTableId="1",ColumnName="display_label",LogicalType="string",YzProjectId=catalog.YzProjectId,
                Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
                    ["columnFullName"]=JsonSerializer.SerializeToElement("company.name")}}
        ]);
        var admin=catalog.ConfigPages.Single(page=>page.Id=="202").YzAdminTypeId;
        foreach(var (id,condition) in new[]{("9010","status=17"),("9020","status=3 AND store_id=@current_store_id")})
        {
            catalog.ConfigPages.Add(new(){Id=id,ConfigTableId="90",YzProjectId=catalog.YzProjectId,
                YzAdminTypeId=admin,WhereSql=condition});
            catalog.ConfigPageColumns.Add(new(){Id=id+"_902",ConfigPageId=id,ConfigColumnId="902",
                YzProjectId=catalog.YzProjectId,IsShow=1});
        }
        foreach(var pageId in new[]{"201","202"})
        foreach(var columnId in new[]{"11","12"})
            catalog.ConfigPageColumns.Add(new(){Id=pageId+"_"+columnId,ConfigPageId=pageId,ConfigColumnId=columnId,
                YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=1});
        catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="202"&&field.ConfigColumnId=="11")
            .Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"pageId":"9020","whereSql":"segment=@formParameters.companyGroup"}""");
        catalog.ConfigPageColumns.Add(new(){Id="202_13",ConfigPageId="202",ConfigColumnId="13",
            YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=0});

        var model=GenerationModel.Create(catalog,ns);
        var source=model.Tables.Single(table=>table.Source.Id=="1");
        _=NonRedundantReadCompilation.ForPage(model,source,source.Pages.Single(page=>page.Source.Id=="201"));
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-same-dictionary");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var actor=new Metarill.Core.Security.RuntimeActor(8,"字典维护",false,catalog.YzProjectId,admin)
                {StoreId=9007199254741000L};
            const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.NonPublic;
            var service=assembly.GetType(ns+".Generated.Services.SameDictionaryNodeService",true)!;
            var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                service.GetField("DerivedCascadePlan201_6",hidden|System.Reflection.BindingFlags.Static)!.GetValue(null));
            var bound=plan.BindAccount(actor);
            const System.Reflection.BindingFlags instance=hidden|System.Reflection.BindingFlags.Instance;
            var sql=bound.GetType().GetProperty("MySql",instance)!.GetValue(bound)!;
            var constants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                sql.GetType().GetProperty("Constants",instance)!.GetValue(sql));
            Assert.DoesNotContain(17,constants.Values);
            Assert.Contains(3,constants.Values);
            Assert.Contains(actor.StoreId!.Value,constants.Values);
            using var runtime=new SharedExcelReadTransport(assembly,ns,"SameDictionaryNode",actor:actor);
            runtime.Add("SameDictionaryNode",new Dictionary<string,object?>
                {["Id"]=11L,["YzProjectId"]=long.Parse(actor.ProjectId,CultureInfo.InvariantCulture),["CompanyGroup"]=7});
            const string method="ValidateStoredCascade201_6Async";
            var matching=new Dictionary<string,object?>{["CompanyGroup"]=7};
            var different=new Dictionary<string,object?>{["CompanyGroup"]=3};
            await runtime.VerifyStoredRelation(method,11,matching,["company_group"]);
            var mismatch=await Assert.ThrowsAsync<InvalidOperationException>(()=>
                runtime.VerifyStoredRelation(method,11,different,["company_group"]));
            Assert.Contains("派生名称依赖字段",mismatch.Message);
            await runtime.VerifyStoredRelation(method,11,different,["company_id"]);
            Assert.Equal(3,runtime.Reads.Count(item=>item=="lock:SameDictionaryNode"));
            Assert.Equal(1,runtime.Transport.Rollbacks);
            Assert.Equal(2,runtime.Transport.Commits);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>写后拓扑必须复核本次父键，另一个有效父节点不能替代本次选择。</summary>
    [Theory]
    [InlineData(false,1)]
    [InlineData(true,0)]
    public async Task Direct_parent_readback_rejects_rewritten_key_or_cache(bool textKey,int redundant)
    {
        const string ns="Example.DirectParentReadback";
        const string entity="ParentReadbackNode", method="VerifyDirectParentForTestAsync";
        var catalog=RedundantMultiNameCacheTests.Published(Catalog("parent_readback",textKey?"string":"int64",textKey?"string":"int64"));
        catalog.ConfigTables.Single().IsRongyu=redundant;
        catalog.ConfigColumns.Single(column=>column.Id=="1").Id="10001";
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigColumnId=="1"))
            field.ConfigColumnId="10001";
        var parent=catalog.ConfigColumns.Single(column=>column.Id=="5");
        parent.ControlKind="CascadeTree";
        parent.ControlDetail=JsonSerializer.Serialize(new{sourceMode="selfTree",selfTree=new
            {table="parent_readback_node",parentField="parent_id",nameField="name",idField=textKey?"node_key":"id"}});
        catalog.ConfigColumns.Single(column=>column.Id=="6").ControlDetail=parent.ControlDetail;
        if(textKey)
        {
            catalog.ConfigColumns.Add(new(){Id="10",ConfigTableId="1",ColumnName="node_key",LogicalType="string",
                YzProjectId=catalog.YzProjectId,ColumnLength="48",IsDbNotNull=1});
            catalog.ConfigIndexes.Add(JsonSerializer.SerializeToElement(new{id="701",configTableId="1",
                indexName="uq_tree_node_key",isUnique=1,isEnabled=1}));
            catalog.ConfigIndexColumns.Add(JsonSerializer.SerializeToElement(new{id="702",configIndexId="701",configColumnId="2",sortNo=1}));
            catalog.ConfigIndexColumns.Add(JsonSerializer.SerializeToElement(new{id="703",configIndexId="701",configColumnId="10",sortNo=2}));
        }
        var model=GenerationModel.Create(catalog,ns);
        var table=Assert.Single(model.Tables);
        var page=table.Pages.Single(candidate=>candidate.Source.Id=="202");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-parent-readback");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,generated,ns);
            // 在受控存储上直接运行正式生成的写后调用，替身不复制校验逻辑。
            await File.WriteAllTextAsync(Path.Combine(generated,"Services/ParentReadbackProbe.cs"),$$"""
                using Metarill.Core.Security;
                using {{ns}}.Generated.Entities;
                namespace {{ns}}.Generated.Services;
                public partial class {{entity}}Service
                {
                    private async Task {{method}}({{entity}} entity,RuntimeActor actor,ISet<string> submitted,
                        bool creating,CancellationToken cancellationToken,bool requireTargetList)
                    {
                        if(creating)
                        {
                {{CascadeTreeTopologyRenderer.After(model,table,page,true)}}
                        }
                        else
                        {
                {{CascadeTreeTopologyRenderer.After(model,table,page,false)}}
                        }
                    }
                }
                """);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var actor=new Metarill.Core.Security.RuntimeActor(8,"父节点校验",false,catalog.YzProjectId,page.Source.YzAdminTypeId);
            using var runtime=new SharedExcelReadTransport(assembly,ns,entity,actor:actor,pageId:"202",mysqlDialect:true);
            var project=long.Parse(actor.ProjectId,CultureInfo.InvariantCulture);
            object first=textKey?"Root-A":1L;
            object second=textKey?"Root-B":2L;
            Seed(1,"可根甲","Root-A");
            Seed(2,"可根乙","Root-B");
            Seed(11,"可当前","Child-11",second);
            var mismatch=await Assert.ThrowsAsync<InvalidOperationException>(()=>
                runtime.VerifyStoredRelation(method,11,Expected(first,"可根甲"),["parent_id"]));
            Assert.Contains("自树父节点在存储后发生变化",mismatch.Message);
            var creation=await Assert.ThrowsAsync<InvalidOperationException>(()=>
                runtime.VerifyStoredRelation(method,11,Expected(first,"可根甲"),[],creating:true));
            Assert.Contains("自树父节点在存储后发生变化",creation.Message);
            await runtime.VerifyStoredRelation(method,11,Expected(second,"可根乙"),["parent_id"]);
            if(redundant==1)
            {
                var cache=await Assert.ThrowsAsync<InvalidOperationException>(()=>
                    runtime.VerifyStoredRelation(method,11,Expected(second,"被改写"),["parent_id"]));
                Assert.Contains("自树父节点名称在存储后发生变化",cache.Message);
            }
            else
                await runtime.VerifyStoredRelation(method,11,Expected(first,"可根甲"),["node_key"]);
            Assert.Equal(redundant==1?3:2,runtime.Transport.Rollbacks);
            Assert.Equal(redundant==1?1:2,runtime.Transport.Commits);

            Dictionary<string,object?> Expected(object parentId,string parentName)
            {
                var fields=new Dictionary<string,object?>{["ParentId"]=parentId,["ParentName"]=parentName};
                if(textKey)fields["NodeKey"]="Child-11";
                return fields;
            }
            void Seed(long id,string name,string key,object? parentId=null)
            {
                var fields=new Dictionary<string,object?>{["Id"]=id,["YzProjectId"]=project,["Name"]=name,
                    ["Status"]=1,["Amount"]=1m,["Code"]="é",["ParentId"]=parentId,
                    ["ParentName"]=parentId is null?null:"可根乙"};
                if(textKey)fields["NodeKey"]=key;
                runtime.Add(entity,fields);
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>来源201/203不含父字段，202为唯一树读取页；作用域使用真实数据库字符比较及金额舍入。</summary>
    internal static MetadataCatalog Catalog(string prefix="same",string sourceType="string",string parentType="int64")
    {
        const string project="9007199254740993";var catalog=new MetadataCatalog{YzProjectId=project,ConfigTables=[new(){Id="1",TableName=prefix+"_node"}]};
        catalog.ConfigColumns.AddRange([
            new(){Id="1",ConfigTableId="1",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="2",ConfigTableId="1",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="3",ConfigTableId="1",ColumnName="delete_no",LogicalType="datetime"},
            new(){Id="4",ConfigTableId="1",ColumnName="name",Name="节点名",LogicalType="string",ColumnLength="100",SortNo=1},new(){Id="5",ConfigTableId="1",ColumnName="parent_id",Name="父节点",LogicalType=parentType,ColumnLength="64",LinkConfigTableId="1",SortNo=5},
            new(){Id="6",ConfigTableId="1",ColumnName="bookmark_id",Name="书签",LogicalType=sourceType,ColumnLength="64",LinkConfigTableId="1",ControlKind="CascadeTree",ControlDetail=JsonSerializer.Serialize(new{sourceMode="selfTree",selfTree=new{table=prefix+"_node",parentField="parent_id",nameField="name"},displayJoiner="/"}),SortNo=2},
            new(){Id="7",ConfigTableId="1",ColumnName="status",LogicalType="int32",DefaultValue="1",SortNo=3},new(){Id="8",ConfigTableId="1",ColumnName="amount",LogicalType="decimal",Precision=8,Scale=2,DefaultValue="1",SortNo=4},new(){Id="9",ConfigTableId="1",ColumnName="code",LogicalType="string",ColumnLength="20",DefaultValue="é",SortNo=6}]);
        foreach(var pageId in new[]{"201","202","203"})
        {
            var page=new PageMetadata{Id=pageId,ConfigTableId="1",YzAdminTypeId="1",IsCopy=1,IsImport=pageId=="202"?0:1,WhereSql="yz_project_id=@current_yz_project_id"+(pageId=="202"?" AND status=1 AND code='é' AND amount<>0.01 AND name LIKE '可%'":"")};if(pageId=="203")page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");catalog.ConfigPages.Add(page);
            foreach(var column in catalog.ConfigColumns.Where(column=>column.Id is "1" or "4" or "6" or "7" or "8" or "9"||pageId=="202"&&column.Id=="5"))catalog.ConfigPageColumns.Add(new(){Id=pageId+column.Id,ConfigPageId=pageId,ConfigColumnId=column.Id,SortNo=column.SortNo});
        }
        return catalog;
    }
}
