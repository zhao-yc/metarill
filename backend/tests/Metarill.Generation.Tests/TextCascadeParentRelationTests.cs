using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>文本级联父页与使用独立目标范围的原关系控件共同生成。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class TextCascadeParentRelationTests
{
    [Theory]
    [InlineData(0, "OnlyNew", "list")]
    [InlineData(1, "OverWriteByFirstColumn", "edit")]
    public async Task Text_parent_with_independent_relations_compiles_original_pages(int redundant, string import, string export)
    {
        var catalog=Catalog(redundant,import,export);
        var model=GenerationModel.Create(catalog,"Example.TextParentRelations");
        var table=model.Tables.Single(item=>item.Source.Id=="2");
        var page=Assert.Single(table.Pages);
        Assert.NotNull(CrossTableReadCompilation.TextSourceParent(model,table,page));
        Assert.Equal(2,RelationCompilation.ForPage(model,page,forExcel:true).Count);
        Assert.False(Assert.Single(MultiSelectCompilation.ForPage(model,table,page)).SameParent);
        Assert.True(Assert.Single(DatabaseCheckboxCompilation.ForPage(model,table,page)).IndependentTextSource);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-text-parent-relations");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var root=NonRedundantApplicationFixture.Root();
            new ApplicationPackager().Create(catalog,directory,model.Namespace,root);
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/Services/TextrelSourceService.g.cs"));
            var parentReader=redundant==0?"LogicalParentId201(parentId)":"SharedR1ParentKey201(parentId)";
            Assert.Contains($"PathByKeyAsync(0,{parentReader}",service);
            Assert.DoesNotContain("out var sourceParentKey",service);
            Assert.Contains("CrossExcelResolve201_25Async",service);
            Assert.Contains("ImportMulti201_704",service);
            Assert.Contains("ImportCheckbox201_706",service);
            Assert.Contains("Export201Async",service);
            if(redundant==0)Assert.Contains("ExportCheckbox201_706Async(entities,actor,request.ParentId",service);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>同表父主键与文本业务键有不同语义，目标加入该父范围时不能隐式沿用。</summary>
    [Fact]
    public void Relation_does_not_reuse_text_parent_as_primary_key()
    {
        var catalog=Catalog(0,"OnlyNew","list");
        var target=catalog.ConfigPages.Single(item=>item.Id=="810");
        target.WhereSql+=" AND country_id=@parent_id";
        catalog.ConfigColumns.Add(new(){Id="8109",ConfigTableId=target.ConfigTableId,ColumnName="country_id",LogicalType="int64",LinkConfigTableId="3"});
        var model=GenerationModel.Create(catalog,"Example.TextParentMismatch");
        var page=Assert.Single(model.Tables.Single(item=>item.Source.Id=="2").Pages);
        Assert.Contains("同一文本业务键",Assert.Throws<NotSupportedException>(()=>RelationCompilation.ForPage(model,page)).Message);
    }

    /// <summary>四种原控件都使用独立字典，父树和字典行可分别授权。</summary>
    private static MetadataCatalog Catalog(int redundant,string import,string export)
    {
        var catalog=CrossTableExcelImportColumnsTests.Catalog("textrel","string");
        var cascade=catalog.ConfigColumns.Single(column=>column.Id=="25");
        if(import=="OverWriteByFirstColumn")cascade.SortNo=-1;
        CrossTableExcelLosslessGenerationTests.AddBusinessKeys(catalog,cascade,primaryLevel:1);
        foreach(var column in catalog.ConfigColumns.Where(column=>column.ColumnName=="business_key"||column.Id=="45"))
        {
            column.LogicalType="string";
            column.ColumnLength="50";
        }
        catalog.ConfigTables.Single(item=>item.Id=="2").IsRongyu=redundant;
        var page=catalog.ConfigPages.Single(item=>item.Id=="201");
        page.WhereSql+=" AND country_id=@parent_id";
        page.Extra.Remove("type");
        page.Extra["importMethod"]=JsonSerializer.SerializeToElement(import);
        page.Extra["exportMethod"]=JsonSerializer.SerializeToElement(export);
        page.IsNew=page.IsSave=1;
        catalog.ConfigColumns.AddRange([
            new(){Id="282",ConfigTableId="2",ColumnName="country_id",LogicalType="string",LinkConfigTableId="3"},
            new(){Id="281",ConfigTableId="2",ColumnName="area_id",LogicalType="int64",LinkConfigTableId="4"}
        ]);
        var controls=new[]{("List","listitem",0), ("SelectPage","singleitem",1),
            ("MultiSelectPage","multiitem",2), ("MultiCheckBoxDatabase","checkitem",0)};
        for(var i=0;i<controls.Length;i++)
        {
            var (control,name,type)=controls[i];
            var targetId=(81+i).ToString();
            var targetPage=targetId+"0";
            var sourceId=(700+i*2).ToString();
            var displayId=(701+i*2).ToString();
            var multiple=i>=2;
            catalog.ConfigTables.Add(new(){Id=targetId,TableName="textrel_"+name,IsRongyu=1});
            catalog.ConfigPages.Add(new(){Id=targetPage,ConfigTableId=targetId,Name=name,WhereSql="yz_project_id=@current_yz_project_id",OrderBy="id asc",
                Extra=new(){["type"]=JsonSerializer.SerializeToElement(type)}});
            catalog.ConfigColumns.AddRange([
                new(){Id=targetId+"01",ConfigTableId=targetId,ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id=targetId+"02",ConfigTableId=targetId,ColumnName="name",LogicalType="string"},
                new(){Id=targetId+"03",ConfigTableId=targetId,ColumnName="yz_project_id",LogicalType="int64"},
                new(){Id=sourceId,ConfigTableId="2",ColumnName=name+(multiple?"_ids":"_id"),LogicalType=multiple?"string":"int64",ColumnLength=multiple?"max":"",
                    ControlKind=control,LinkConfigTableId=targetId,ControlDetail=JsonSerializer.Serialize(new{tableId=targetId,pageId=targetPage,nameField="name"})},
                new(){Id=displayId,ConfigTableId="2",ColumnName=name+(multiple?"_names":"_name"),LogicalType="string",ColumnLength=multiple?"max":"100",
                    Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement(name+".name")}}
            ]);
            catalog.ConfigPageColumns.AddRange([
                new(){Id="f"+targetId,ConfigPageId=targetPage,ConfigColumnId=targetId+"02",IsShow=1},
                new(){Id="f"+sourceId,ConfigPageId=page.Id,ConfigColumnId=sourceId,IsShow=1,IsEdit=1},
                new(){Id="f"+displayId,ConfigPageId=page.Id,ConfigColumnId=displayId,IsShow=1}
            ]);
        }
        RedundantMultiNameCacheTests.Published(catalog);
        catalog.ConfigMenus.Add(new(){Id="2200",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",Name="来源",Kind="GENERATED_PAGE",ConfigPageId=page.Id});
        return catalog;
    }
}
