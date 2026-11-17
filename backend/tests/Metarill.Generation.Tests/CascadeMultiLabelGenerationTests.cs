using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CascadeMultiLabelGenerationTests
{
    /// <summary>保留原多选和复选入口，发布目录可独立编译候选、导入、导出与维护计划。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,false)]
    [InlineData("MultiSelectPage",1,false)]
    [InlineData("MultiCheckBoxDatabase",0,false)]
    [InlineData("MultiCheckBoxDatabase",1,false)]
    [InlineData("MultiSelectPage",0,true)]
    [InlineData("MultiSelectPage",1,true)]
    [InlineData("MultiCheckBoxDatabase",0,true)]
    [InlineData("MultiCheckBoxDatabase",1,true)]
    public Task Cascade_names_compile_in_original_endpoints(string kind,int mode,bool selfTree)=>
        CompileCascadeLabel(kind,mode,selfTree,indirect:false);

    /// <summary>通过普通关联取跨表祖先或自树叶名，发布与维护必须包含中间记录的依赖。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,false)]
    [InlineData("MultiCheckBoxDatabase",1,false)]
    [InlineData("MultiSelectPage",1,true)]
    [InlineData("MultiCheckBoxDatabase",0,true)]
    public Task Joined_cascade_names_compile_in_original_endpoints(string kind,int mode,bool selfTree)=>
        CompileCascadeLabel(kind,mode,selfTree,indirect:true);

    private static async Task CompileCascadeLabel(string kind,int mode,bool selfTree,bool indirect)
    {
        const string ns="Example.CascadeLabels";
        var catalog=selfTree?NonRedundantSelfTreeTests.Catalog("label_cross"):
            NonRedundantCrossTableGenerationTests.Catalog("label_cross","string","int64");
        var display=selfTree?"tree_name":"country_name";
        if(selfTree)
        {
            var target=catalog.ConfigPages.Single(page=>page.Id=="201");
            target.Extra["type"]=JsonSerializer.SerializeToElement(2);
        }
        else
        {
            catalog.ConfigColumns.Add(new(){Id="29",ConfigTableId="2",ColumnName=display,LogicalType="string"});
            catalog.ConfigPageColumns.Add(new(){Id="20129",ConfigPageId="201",ConfigColumnId="29",IsEdit=0});
        }
        var targetTable="2";
        var targetPage="201";
        if(indirect)
        {
            targetTable="8";
            targetPage="801";
            catalog.ConfigTables.Add(new(){Id=targetTable,TableName="label_join",IsRongyu=0});
            catalog.ConfigColumns.AddRange([
                new(){Id="81",ConfigTableId=targetTable,ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id="82",ConfigTableId=targetTable,ColumnName="yz_project_id",LogicalType="int64"},
                new(){Id="83",ConfigTableId=targetTable,ColumnName="carrier_id",LogicalType="string",ControlKind="List",LinkConfigTableId="2",
                    ControlDetail="{\"tableId\":\"2\",\"nameField\":\"id\"}"},
                new(){Id="84",ConfigTableId=targetTable,ColumnName="name",LogicalType="string",Extra=new()
                    {["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("carrier."+display)}}
            ]);
            catalog.ConfigPages.Add(new(){Id=targetPage,ConfigTableId=targetTable,WhereSql="yz_project_id=@current_yz_project_id",
                Extra=new(){["type"]=JsonSerializer.SerializeToElement(2)}});
            catalog.ConfigPageColumns.Add(new(){Id="80184",ConfigPageId=targetPage,ConfigColumnId="84",IsEdit=0});
            display="name";
        }
        catalog.ConfigTables.Add(new(){Id="9",TableName="label_consumer",IsRongyu=mode});
        catalog.ConfigPages.Add(new(){Id="901",ConfigTableId="9",IsImport=1,IsExport=1,WhereSql="yz_project_id=@current_yz_project_id"});
        catalog.ConfigColumns.AddRange([
            new(){Id="91",ConfigTableId="9",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="92",ConfigTableId="9",ColumnName="name",Name="记录",LogicalType="string"},
            new(){Id="93",ConfigTableId="9",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="94",ConfigTableId="9",ColumnName="delete_no",LogicalType="datetime"},
            new(){Id="95",ConfigTableId="9",ColumnName="selected_ids",Name="关联",LogicalType="string",ControlKind=kind,
                LinkConfigTableId=targetTable,ControlDetail=JsonSerializer.Serialize(new{tableId=targetTable,pageId=targetPage,nameField=display})}
        ]);
        catalog.ConfigPageColumns.Add(new(){Id="90192",ConfigPageId="901",ConfigColumnId="92"});
        catalog.ConfigPageColumns.Add(new(){Id="90195",ConfigPageId="901",ConfigColumnId="95",Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}});
        RedundantMultiNameCacheTests.Published(catalog);

        var model=GenerationModel.Create(catalog,ns);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cascade-multi-labels");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var consumer=assembly.GetType(ns+".Generated.Services.LabelConsumerService",true)!;
            RuntimeHelpers.RunClassConstructor(consumer.TypeHandle);
            RuntimeHelpers.RunClassConstructor(assembly.GetType(ns+".Generated.Services.LabelCrossSourceService",true)!.TypeHandle);
            if(indirect)RuntimeHelpers.RunClassConstructor(assembly.GetType(ns+".Generated.Services.LabelJoinService",true)!.TypeHandle);
            Assert.NotNull(consumer.GetMethod("Import901Async"));
            Assert.NotNull(consumer.GetMethod("Export901Async"));
            if(mode==1)
            {
                var targets=MixedRelationPropagationCompilation.ForModel(model).MixedTargets;
                Assert.Contains(selfTree?"label_cross_tree":"label_cross_country",targets);
                if(indirect)Assert.Contains("label_cross_source",targets);
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
