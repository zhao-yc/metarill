using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>项目内派生层名在候选、列表与缓存使用该层页面实际绑定的下拉。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CrossTableProjectNameScopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scoped_dictionary_is_used_by_project_cross_table_names(bool redundant)
    {
        var catalog=Catalog(redundant);
        const string package="Example.LocalCrossDictionary";
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-local-cross-names");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),package);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(package+".Generated.Services.NrCrossFullSourceService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            const BindingFlags plans=BindingFlags.Static|BindingFlags.NonPublic;
            var actor=new RuntimeActor(33,"实际门店",false,catalog.YzProjectId,"5"){StoreId=9007199254740993L};
            var candidate=Assert.IsType<NonRedundantReadPlan>(service.GetField("CrossTargetPlan201_25_0",plans)!.GetValue(null));
            Verify(candidate,actor);
            var code=File.ReadAllText(Path.Combine(directory,"generated/Services/NrCrossFullSourceService.g.cs"));
            Assert.Contains("CrossTargetPlan201_25_0.BindAccount(actor)",code);
            Assert.Contains("return await CrossTargetRows201_25_0Async(actor,filters,2,currentRead,ct)",code);
            Assert.NotNull(service.GetMethod("Export201Async"));
            if(redundant)
            {
                VerifyMaintenance(assembly,package);
            }
            else
            {
                var list=Assert.IsType<NonRedundantReadPlan>(service.GetField("ReadPlan201",plans)!.GetValue(null));
                Verify(list,actor);
                Assert.Contains("ReadPlan201.BindAccount(actor)",code);
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>实际 SQL 使用账号与控件附加谓词。</summary>
    private static void Verify(NonRedundantReadPlan plan,RuntimeActor actor)
    {
        Assert.True(plan.RequiresAccount);
        Assert.Throws<UnauthorizedAccessException>(()=>plan.BindAccount(actor with{StoreId=null}));
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var mysql=typeof(NonRedundantReadPlan).GetProperty("MySql",flags)!.GetValue(plan.BindAccount(actor))!;
        var sql=Assert.IsType<string>(mysql.GetType().GetProperty("From",flags)!.GetValue(mysql));
        Assert.Contains("nr_cross_dictionary",sql);
        Assert.Contains(".`store_id`",sql);
        Assert.Contains(".`segment`",sql);
        var constants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(mysql.GetType().GetProperty("Constants",flags)!.GetValue(mysql));
        Assert.Contains(actor.StoreId,constants.Values);
        Assert.Contains(17,constants.Values);
    }

    /// <summary>维护和反查使用来源行门店，字典页覆盖不能只在实时查询中生效。</summary>
    private static void VerifyMaintenance(Assembly assembly,string package)
    {
        var type=assembly.GetType(package+".Generated.GeneratedRelationMaintenance",true)!;
        var catalog=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(type.GetProperty("Catalog")!.GetValue(null));
        Assert.Contains("nr_cross_dictionary",catalog.MixedTargets);
        Assert.Contains("country_name",catalog.MixedSourceColumns["nr_cross_full_source"]);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(catalog.GetType().GetProperty("Plans",flags)!.GetValue(catalog));
        var plan=plans["2"]!;
        Assert.Null(plan.GetType().GetProperty("Error")!.GetValue(plan));
        var read=Assert.IsType<MySqlNonRedundantReadPlan>(plan.GetType().GetProperty("Read")!.GetValue(plan));
        Assert.False(Assert.IsType<bool>(read.GetType().GetProperty("RequiresAccount",flags)!.GetValue(read)));
        var expression=Assert.IsType<string>(read.GetType().GetMethod("Expression",flags)!.Invoke(read,["country_name"]));
        Assert.Contains("=BINARY CAST(b.`store_id` AS CHAR)",expression);
        var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(read.GetType().GetProperty("Constants",flags)!.GetValue(read));
        Assert.Contains(17,parameters.Values);
        Assert.DoesNotContain(7,parameters.Values);
    }

    private static MetadataCatalog Catalog(bool redundant)
    {
        var catalog=NonRedundantCrossFullTests.Catalog("string","int64",true);
        var sourcePage=catalog.ConfigPages.Single(page=>page.Id=="201");
        sourcePage.IsImport=1;
        sourcePage.IsExport=1;
        if(redundant)
        {
            catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=1;
            catalog.ConfigColumns.Add(new(){Id="9908",ConfigTableId="2",ColumnName="store_id",LogicalType="int64",YzProjectId=catalog.YzProjectId});
            foreach(var recordPage in catalog.ConfigPages.Where(page=>page.ConfigTableId=="2"))
                recordPage.WhereSql+=" AND store_id=@current_store_id";
        }
        var target=catalog.ConfigTables.Single(table=>table.Id=="3");
        target.IsRongyu=0;
        var page=catalog.ConfigPages.Single(item=>item.Id=="301");
        var label=catalog.ConfigColumns.Single(column=>column.ConfigTableId==target.Id&&column.ColumnName=="name");
        label.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        label.Extra["columnFullName"]=JsonSerializer.SerializeToElement("dictionary.name");
        catalog.ConfigTables.Add(new(){Id="99",TableName="nr_cross_dictionary",IsRongyu=0,YzProjectId=catalog.YzProjectId});
        catalog.ConfigColumns.AddRange([
            new(){Id="9901",ConfigTableId="99",ColumnName="id",LogicalType="int64",IsDbNotNull=1,YzProjectId=catalog.YzProjectId},
            new(){Id="9902",ConfigTableId="99",ColumnName="yz_project_id",LogicalType="int64",YzProjectId=catalog.YzProjectId},
            new(){Id="9903",ConfigTableId="99",ColumnName="name",LogicalType="string",YzProjectId=catalog.YzProjectId},
            new(){Id="9904",ConfigTableId="99",ColumnName="store_id",LogicalType="int64",YzProjectId=catalog.YzProjectId},
            new(){Id="9905",ConfigTableId="99",ColumnName="segment",LogicalType="int32",YzProjectId=catalog.YzProjectId},
            new(){Id="9906",ConfigTableId=target.Id,ColumnName="dictionary_id",LogicalType="int64",ControlKind="List",LinkConfigTableId="99",
                ControlDetail="""{"pageId":"990","whereSql":"segment=7"}""",YzProjectId=catalog.YzProjectId}
        ]);
        catalog.ConfigPages.Add(new(){Id="990",ConfigTableId="99",WhereSql="yz_project_id=@current_yz_project_id AND store_id=@current_store_id",
            YzProjectId=catalog.YzProjectId,YzAdminTypeId=page.YzAdminTypeId});
        catalog.ConfigPageColumns.AddRange([
            new(){Id="9909903",ConfigPageId="990",ConfigColumnId="9903",IsShow=1,IsEdit=1,YzProjectId=catalog.YzProjectId},
            new(){Id="3019906",ConfigPageId="301",ConfigColumnId="9906",IsEdit=1,YzProjectId=catalog.YzProjectId,
                Extra=new(){["controlDetail"]=JsonSerializer.SerializeToElement("""{"whereSql":"segment=17"}""")}}
        ]);
        return catalog;
    }
}
