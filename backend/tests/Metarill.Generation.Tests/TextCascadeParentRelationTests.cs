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

    /// <summary>原下拉和单选把同一文本父键加入候选、保存及原工作簿范围。</summary>
    [Theory]
    [InlineData("OnlyNew","list")]
    [InlineData("OverWriteByFirstColumn","edit")]
    public async Task Matching_published_text_parent_scopes_single_relations(string import,string export)
    {
        var catalog=Catalog(1,import,export);
        ScopeTarget(catalog,"81");
        ScopeTarget(catalog,"82");
        catalog.ConfigColumns.Single(column=>column.Id=="700").Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("810");
        var model=GenerationModel.Create(catalog,"Example.SameTextParent");
        var source=model.Tables.Single(table=>table.Source.Id=="2");
        var page=Assert.Single(source.Pages);
        var relations=RelationCompilation.ForPage(model,page,true,forExcel:true);
        Assert.Equal(2,relations.Count);
        Assert.All(relations,relation=>
        {
            Assert.Equal("country_id",relation.SharedTextParent?.Source.ColumnName);
            Assert.True(ControlRenderer.RelationContext(page,relation,false).SameParent);
        });
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-same-text-parent");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new ApplicationPackager().Create(catalog,directory,model.Namespace,NonRedundantApplicationFixture.Root());
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/Services/TextrelSourceService.g.cs"));
            Assert.Contains("string.Equals(targetParentId,parentId,StringComparison.Ordinal)",service);
            Assert.Contains("\"country_id\",targetParentId!,businessKey:true)",service);
            Assert.DoesNotContain("out var targetParentKey",service);
            Assert.Contains("OptionsScope201_700Async(entity.CountryId, null, actor, cancellationToken, true, false",service);
            Assert.Contains("OptionsScope201_702Async(entity.CountryId, null, actor, cancellationToken, true, false",service);
            Assert.Contains("ImportRelation201_700Async",service);
            Assert.Contains("ImportRelation201_702Async",service);
            Assert.Contains("OptionsScope201_700Async(parentId, null, actor, cancellationToken, true, false",service);
            Assert.Contains("OptionsScope201_702Async(parentId, null, actor, cancellationToken, true, false",service);
            var detail=await File.ReadAllTextAsync(Path.Combine(directory,"frontend/pages/yzp/textrel_source-detail-201.vue"));
            Assert.Contains(":parent_id=\"parent_id\"",detail);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>同表字符串字段可能存有旧主键，只有发布的键和真实路径相同才复用。</summary>
    [Fact]
    public void Matching_text_storage_does_not_prove_matching_key()
    {
        var catalog=Catalog(1,"OnlyNew","list");
        ScopeTarget(catalog,"81");
        var column=catalog.ConfigColumns.Single(item=>item.Id=="8106");
        var control=System.Text.Json.Nodes.JsonNode.Parse(column.ControlDetail)!;
        control["levels"]![0]!["idField"]="id";
        column.ControlDetail=control.ToJsonString();
        var model=GenerationModel.Create(catalog,"Example.TextParentDifferentKey");
        var source=model.Tables.Single(item=>item.Source.Id=="2");
        var target=model.Tables.Single(item=>item.Source.Id=="81");
        Assert.Null(SharedRedundantParentCompilation.CommonTextParent(model,source,Assert.Single(source.Pages),target,Assert.Single(target.Pages)));
    }

    /// <summary>共用文本父键的两个多值控件沿原候选、写入及工作簿合同生成。</summary>
    [Theory]
    [InlineData("OnlyNew","list")]
    [InlineData("OverWriteByFirstColumn","edit")]
    public async Task Matching_published_text_parent_scopes_multi_relations(string import,string export)
    {
        var catalog=Catalog(1,import,export);
        ScopeTarget(catalog,"83");
        ScopeTarget(catalog,"84");
        catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="706")
            .Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        var model=GenerationModel.Create(catalog,"Example.MultiSameTextParent");
        var table=model.Tables.Single(item=>item.Source.Id=="2");
        var page=Assert.Single(table.Pages);
        var multi=Assert.Single(MultiSelectCompilation.ForPage(model,table,page));
        var checkbox=Assert.Single(DatabaseCheckboxCompilation.ForPage(model,table,page));
        Assert.True(multi.SameParent);
        Assert.Equal("country_id",multi.SharedTextParent?.Source.ColumnName);
        Assert.Equal("country_id",checkbox.SharedTextParent?.Source.ColumnName);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-multi-same-text-parent");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new ApplicationPackager().Create(catalog,directory,model.Namespace,NonRedundantApplicationFixture.Root());
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/Services/TextrelSourceService.g.cs"));
            Assert.Contains("targetParentId=SharedR1ParentKey201(entity.CountryId)",service);
            Assert.Contains("targetParentId=SharedR1ParentKey201(parentId)",service);
            Assert.Contains("\"country_id\",targetParentId!,businessKey:true)",service);
            Assert.Contains("\"country_id\",parentId!,businessKey:true)",service);
            Assert.Contains("CheckboxScope201_706Async(entity.CountryId",service);
            Assert.Contains("ImportMulti201_704Async",service);
            Assert.Contains("ImportCheckbox201_706Async",service);
            Assert.Contains("Export201Async",service);
            Assert.DoesNotContain("out var targetParentKey",service);
            var detail=await File.ReadAllTextAsync(Path.Combine(directory,"frontend/pages/yzp/textrel_source-detail-201.vue"));
            Assert.Contains(",[\"multiitemIds\"])",detail);
            var list=await File.ReadAllTextAsync(Path.Combine(directory,"frontend/pages/yzp/textrel_source-list-201.vue"));
            Assert.Contains("loadOriginalArrayCheckboxOptions(",list);
            Assert.Contains("\"textParent\":true",list);
            var probe=NonRedundantApplicationFixture.Start("node",directory);
            probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"GeneratedMultiTextParentProbe.mjs"));
            probe.ArgumentList.Add(Path.Combine(directory,"frontend"));
            await NonRedundantApplicationFixture.RunAsync(probe);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>非冗余单值关系从两端稀疏路径还原文本父键，名称和工作簿复用实际目标范围。</summary>
    [Theory]
    [InlineData("OnlyNew","list")]
    [InlineData("OverWriteByFirstColumn","edit")]
    public async Task Sparse_same_text_parent_scopes_single_relations(string import,string export)
    {
        var catalog=Catalog(0,import,export);
        foreach(var id in new[]{"81","82"})
        {
            ScopeTarget(catalog,id);
            catalog.ConfigTables.Single(item=>item.Id==id).IsRongyu=0;
        }
        var model=GenerationModel.Create(catalog,"Example.SparseSameTextParent");
        var table=model.Tables.Single(item=>item.Source.Id=="2");
        var page=Assert.Single(table.Pages);
        var relations=RelationCompilation.ForPage(model,page,true,forExcel:true);
        Assert.Equal(2,relations.Count);
        Assert.All(relations,relation=>
        {
            Assert.Equal("country_id",relation.SharedTextParent?.Source.ColumnName);
            Assert.True(relation.LogicalSourceParent);
            Assert.True(ControlRenderer.RelationContext(page,relation,false).SameParent);
        });
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var plan=Metarill.Data.Queries.NonRedundantReads.CompileCrossPageCandidate(catalog,"81","810",["id"],[]);
        using var session=new Metarill.Data.DatabaseSession(new(){ExpectedDatabase="offline",
            ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused"});
        foreach(var key in new[]{"001","1","Country-A"})
        {
            var sql=Metarill.Data.Queries.NonRedundantCrossTableScopeSql.ApplyCandidate(session.Select<ScopeRow>(),session,plan,
                "same_text_parent",catalog.YzProjectId,false,key).ToSql();
            Assert.Contains("=BINARY @same_text_parent_parent",sql);
        }
        Assert.Throws<ArgumentException>(()=>Metarill.Data.Queries.NonRedundantCrossTableScopeSql.ApplyCandidate(
            session.Select<ScopeRow>(),session,plan,"same_text_parent",catalog.YzProjectId,false," "));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-sparse-same-text-parent");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new ApplicationPackager().Create(catalog,directory,model.Namespace,NonRedundantApplicationFixture.Root());
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/Services/TextrelSourceService.g.cs"));
            Assert.Contains("NonRedundantCrossTableScopeSql.ApplyCandidate(query,session,OptionsMembership201_700.BindAccount(actor)",service);
            Assert.Contains("NonRedundantCrossTableScopeSql.ApplyCandidate(query,session,OptionsMembership201_702.BindAccount(actor)",service);
            Assert.Contains("OptionsScope201_700Async(logicalRelationParentId, null, actor, cancellationToken, true, false",service);
            Assert.Contains("OptionsScope201_702Async(logicalRelationParentId, null, actor, cancellationToken, true, false",service);
            Assert.Contains("ImportRelation201_700Async",service);
            Assert.Contains("ImportRelation201_702Async",service);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    private sealed class ScopeRow
    {
        public long Id { get; set; }
    }

    /// <summary>两张业务表发布同一真实国家业务键，候选数据仍来自各自的表和页面。</summary>
    private static void ScopeTarget(MetadataCatalog catalog,string id)
    {
        var page=catalog.ConfigPages.Single(item=>item.ConfigTableId==id);
        page.WhereSql+=" AND country_id=@parent_id";
        var cascade=catalog.ConfigColumns.Single(column=>column.Id=="25");
        catalog.ConfigColumns.AddRange([
            new(){Id=id+"04",ConfigTableId=id,ColumnName="country_id",LogicalType="string",LinkConfigTableId="3",YzProjectId=catalog.YzProjectId},
            new(){Id=id+"05",ConfigTableId=id,ColumnName="area_id",LogicalType="int64",LinkConfigTableId="4",YzProjectId=catalog.YzProjectId},
            new(){Id=id+"06",ConfigTableId=id,ColumnName="place_id",LogicalType="string",LinkConfigTableId="5",ControlKind="CascadeTree",ControlDetail=cascade.ControlDetail,YzProjectId=catalog.YzProjectId}
        ]);
        catalog.ConfigPageColumns.Add(new(){Id="f"+id+"06",ConfigPageId=page.Id,ConfigColumnId=id+"06",YzProjectId=catalog.YzProjectId});
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
