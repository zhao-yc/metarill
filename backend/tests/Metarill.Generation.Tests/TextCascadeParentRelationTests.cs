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
        AssertTextParentSql(plan);
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

    /// <summary>非冗余来源和冗余字典使用同一文本业务键，原选档和工作簿共用范围。</summary>
    [Theory]
    [InlineData("OnlyNew","list")]
    [InlineData("OverWriteByFirstColumn","edit")]
    public async Task Sparse_source_stored_target_share_text_parent(string import,string export)
    {
        var catalog=Catalog(0,import,export);
        ScopeTarget(catalog,"81");
        ScopeTarget(catalog,"82");
        var model=GenerationModel.Create(catalog,"Example.SparseStoredTextParent");
        var table=model.Tables.Single(item=>item.Source.Id=="2");
        var page=Assert.Single(table.Pages);
        var relations=RelationCompilation.ForPage(model,page,true,forExcel:true);
        Assert.Equal(2,relations.Count);
        Assert.All(relations,relation=>
        {
            Assert.Equal("country_id",relation.SharedTextParent?.Source.ColumnName);
            Assert.True(relation.MixedStoredTextParent);
            Assert.True(ControlRenderer.RelationContext(page,relation,false).SameParent);
        });
        var targets=NonRedundantReadCompilation.ProjectSingleTargets(model,table,page,["listitem_name","singleitem_name"]);
        Assert.All(targets.Where(target=>target.ColumnId is "700" or "702"),target=>
        {
            Assert.Equal("282",target.SourceParentId);
            Assert.Equal("201",target.SourcePageId);
            Assert.True(target.ApplyPageCondition);
        });
        Assert.Equal(2,targets.Count(target=>target.ColumnId is "700" or "702"));
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var plan=NonRedundantReadCompilation.CompilePage(model,table,["listitem_name","singleitem_name"],[],
            NonRedundantCrossTableCompilation.Bindings(model,table,page),targets,[]);
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var mysql=plan.GetType().GetProperty("MySql",flags)!.GetValue(plan)!;
        var from=Assert.IsType<string>(mysql.GetType().GetProperty("From",flags)!.GetValue(mysql));
        Assert.Contains("`business_key`",from);
        Assert.DoesNotContain("single_parent_",from);
        Assert.Equal(2,System.Text.RegularExpressions.Regex.Matches(from,
            @"BINARY CAST\(\w+\.`country_id` AS CHAR\)=BINARY CAST\(CASE WHEN").Count);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-sparse-stored-text-parent");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new ApplicationPackager().Create(catalog,directory,model.Namespace,NonRedundantApplicationFixture.Root());
            var published=await PublishedReadCatalog(directory);
            var frozen=Metarill.Data.Queries.NonRedundantReads.CompileProjectPage(published,"2",["listitem_name","singleitem_name"],[],
                NonRedundantCrossTableCompilation.Bindings(model,table,page),targets);
            var frozenSql=frozen.GetType().GetProperty("MySql",flags)!.GetValue(frozen)!;
            Assert.Equal(from,Assert.IsType<string>(frozenSql.GetType().GetProperty("From",flags)!.GetValue(frozenSql)));
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/Services/TextrelSourceService.g.cs"));
            Assert.Contains("ApplyTextParent(query,session,\"country_id\",targetParentId!,businessKey:true)",service);
            Assert.Contains("OptionsScope201_700Async(logicalRelationParentId, null, actor, cancellationToken, true, false",service);
            Assert.Contains("OptionsScope201_702Async(logicalRelationParentId, null, actor, cancellationToken, true, false",service);
            Assert.Contains("ImportRelation201_700Async",service);
            Assert.Contains("ImportRelation201_702Async",service);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>目标页使用实体主键时，发布名称计划也拒绝把来源文本业务键当成同一身份。</summary>
    [Fact]
    public void Sparse_source_text_parent_rejects_stored_target_primary_key()
    {
        var catalog=Catalog(0,"OnlyNew","list");
        ScopeTarget(catalog,"81");
        var column=catalog.ConfigColumns.Single(item=>item.Id=="8106");
        var definition=System.Text.Json.Nodes.JsonNode.Parse(column.ControlDetail)!;
        definition["levels"]![0]!["idField"]="id";
        column.ControlDetail=definition.ToJsonString();
        var model=GenerationModel.Create(catalog,"Example.SparseStoredDifferentKey");
        var source=model.Tables.Single(item=>item.Source.Id=="2");
        var target=model.Tables.Single(item=>item.Source.Id=="81");
        Assert.Null(NonRedundantCrossParentCompilation.CommonTextParent(model,source,Assert.Single(source.Pages),
            target,Assert.Single(target.Pages),allowStoredTarget:true));
        var binding=new Metarill.Data.Queries.NonRedundantSingleTargetScope("700","810","282"){SourcePageId="201"};
        var error=Assert.Throws<InvalidOperationException>(()=>Metarill.Data.Queries.NonRedundantReads.CompileProjectPage(
            catalog,"2",["listitem_name"],[],[],[binding],[]));
        Assert.Contains("相同发布文本父路径",error.Message);
    }

    /// <summary>冗余来源的物理父键与非冗余目标的逻辑祖先必须用于候选、写入和缓存重算。</summary>
    [Theory]
    [InlineData("OnlyNew","list")]
    [InlineData("OverWriteByFirstColumn","edit")]
    public async Task Stored_source_sparse_target_share_text_parent(string import,string export)
    {
        var catalog=Catalog(1,import,export);
        foreach(var id in new[]{"81","82"})
        {
            ScopeTarget(catalog,id);
            catalog.ConfigTables.Single(item=>item.Id==id).IsRongyu=0;
        }
        var model=GenerationModel.Create(catalog,"Example.StoredSparseTextParent");
        var table=model.Tables.Single(item=>item.Source.Id=="2");
        var page=Assert.Single(table.Pages);
        var relations=RelationCompilation.ForPage(model,page,true,forExcel:true);
        Assert.Equal(2,relations.Count);
        Assert.All(relations,relation=>
        {
            Assert.Equal("country_id",relation.SharedTextParent?.Source.ColumnName);
            Assert.Equal("282",relation.FixedSourceParent?.Source.Id);
            Assert.True(ControlRenderer.RelationContext(page,relation,false).SameParent);
            Assert.True(relation.FixedParent?.Logical);
        });
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var caches=RelationPropagationCompilation.DerivedBindings(model)
            .Where(item=>item.Foreign.Source.Id is "700" or "702").ToArray();
        Assert.Equal(new[]{"810","820"},caches.Select(item=>item.ScopedSinglePageId));
        var maintenance=MixedRelationPropagationCompilation.ForModel(model);
        Assert.Contains("textrel_listitem",maintenance.MixedTargets);
        Assert.Contains("textrel_singleitem",maintenance.MixedTargets);
        Assert.Contains("listitem_name",maintenance.MixedSourceColumns["textrel_source"]);
        Assert.Contains("singleitem_name",maintenance.MixedSourceColumns["textrel_source"]);
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(maintenance.GetType().GetProperty("Plans",flags)!.GetValue(maintenance));
        var source=plans["2"]!;
        Assert.Null(source.GetType().GetProperty("Error")!.GetValue(source));
        var read=source.GetType().GetProperty("Read")!.GetValue(source)!;
        foreach(var column in new[]{"listitem_name","singleitem_name"})
        {
            var label=Assert.IsType<string>(read.GetType().GetMethod("Expression",flags)!.Invoke(read,[column]));
            Assert.Contains("BINARY CAST(b.`country_id` AS CHAR)",label);
            Assert.Contains("business_key",label);
        }

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-stored-sparse-text-parent");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new ApplicationPackager().Create(catalog,directory,model.Namespace,NonRedundantApplicationFixture.Root());
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/Services/TextrelSourceService.g.cs"));
            Assert.Contains("NonRedundantCrossTableScopeSql.ApplyCandidate(query,session,OptionsMembership201_700.BindAccount(actor)",service);
            Assert.Contains("NonRedundantCrossTableScopeSql.ApplyCandidate(query,session,OptionsMembership201_702.BindAccount(actor)",service);
            Assert.Contains("OptionsScope201_700Async(entity.CountryId, null, actor, cancellationToken, true, false",service);
            Assert.Contains("OptionsScope201_702Async(entity.CountryId, null, actor, cancellationToken, true, false",service);
            Assert.Contains("ImportRelation201_700Async",service);
            Assert.Contains("ImportRelation201_702Async",service);
            Assert.DoesNotContain("out var targetParentKey",service);
            var detail=await File.ReadAllTextAsync(Path.Combine(directory,"frontend/pages/yzp/textrel_source-detail-201.vue"));
            Assert.Contains(":parent_id=\"parent_id\"",detail);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>缓存重算入口独立拒绝通过字符串父列借用整数主键路径。</summary>
    [Fact]
    public void Stored_source_text_parent_rejects_sparse_target_primary_key()
    {
        var catalog=Catalog(1,"OnlyNew","list");
        ScopeTarget(catalog,"81");
        catalog.ConfigTables.Single(item=>item.Id=="81").IsRongyu=0;
        var column=catalog.ConfigColumns.Single(item=>item.Id=="8106");
        var definition=System.Text.Json.Nodes.JsonNode.Parse(column.ControlDetail)!;
        definition["levels"]![0]!["idField"]="id";
        column.ControlDetail=definition.ToJsonString();
        var cache=new Metarill.Data.Relations.RelationCacheDefinition("2","700","81","8102","701",false){ScopedSinglePageId="810"};
        var error=Assert.Throws<InvalidOperationException>(()=>Metarill.Data.Relations.DerivedCrossTableCaches.Validate(catalog,[cache],[]));
        Assert.Contains("相同发布文本父路径",error.Message);
    }

    private sealed class ScopeRow
    {
        public long Id { get; set; }
    }

    /// <summary>两端稀疏多值关系在列表、弹层、写入和名称工作簿使用相同真实父键。</summary>
    [Theory]
    [InlineData("OnlyNew","list")]
    [InlineData("OverWriteByFirstColumn","edit")]
    public async Task Sparse_same_text_parent_scopes_multi_relations(string import,string export)
    {
        var catalog=Catalog(0,import,export);
        foreach(var id in new[]{"83","84"})
        {
            ScopeTarget(catalog,id);
            catalog.ConfigTables.Single(item=>item.Id==id).IsRongyu=0;
        }
        foreach(var columnId in new[]{"704","706"})
        {
            catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId==columnId)
                .Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
            catalog.ConfigColumns.Single(column=>column.Id==columnId).Extra["filterControlKind"]=JsonSerializer.SerializeToElement("CheckBox");
        }
        var model=GenerationModel.Create(catalog,"Example.SparseMultiSameTextParent");
        var table=model.Tables.Single(item=>item.Source.Id=="2");
        var page=Assert.Single(table.Pages);
        var multi=MultiSelectCompilation.Resolve(model,table,page,table.Columns.Single(column=>column.Source.Id=="704"));
        var checkbox=DatabaseCheckboxCompilation.Resolve(model,table,page,table.Columns.Single(column=>column.Source.Id=="706"));
        Assert.True(multi.Definition is {LogicalSourceParent:true,SharedTextParent:not null},multi.Reason);
        Assert.True(checkbox.Definition is {UsesPageTextParent:true,DerivedName:true},checkbox.Reason);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        AssertTextParentSql(Metarill.Data.Queries.NonRedundantReads.CompileScopedMultiLabel(catalog,"83","830","name",["id","name"],[]));
        AssertTextParentSql(Metarill.Data.Queries.NonRedundantReads.CompileScopedCheckboxLabel(catalog,"84","840","name",["id","name"],[],false));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-sparse-multi-same-text-parent");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new ApplicationPackager().Create(catalog,directory,model.Namespace,NonRedundantApplicationFixture.Root());
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/Services/TextrelSourceService.g.cs"));
            Assert.Contains("targetParentId=LogicalParentId201(logicalRelationParentId)",service);
            Assert.Contains("targetParentId=LogicalParentId201(parentId)",service);
            Assert.Contains("\"country_id\",FixedReadComparison.Equal,targetParentId!",service);
            Assert.Contains("\"country_id\",FixedReadComparison.Equal,parentId!",service);
            Assert.Contains("DerivedCheckboxRows201_706Async(logicalRelationParentId",service);
            Assert.Contains("ExportCheckbox201_706Async(entities,actor,request.ParentId",service);
            Assert.DoesNotContain("out var targetParentKey",service);
            var probe=NonRedundantApplicationFixture.Start("node",directory);
            probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"GeneratedMultiTextParentProbe.mjs"));
            probe.ArgumentList.Add(Path.Combine(directory,"frontend"));
            probe.ArgumentList.Add("sparse");
            await NonRedundantApplicationFixture.RunAsync(probe);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>生成的多选与复选沿同一发布文本父，候选键和来源业务键的原始字符一致。</summary>
    [Theory]
    [InlineData("OnlyNew","list")]
    [InlineData("OverWriteByFirstColumn","edit")]
    public async Task Sparse_source_stored_multi_targets_share_text_parent(string import,string export)
    {
        var catalog=Catalog(0,import,export);
        ScopeTarget(catalog,"83");
        ScopeTarget(catalog,"84");
        foreach(var id in new[]{"704","706"})
        {
            catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId==id)
                .Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
            catalog.ConfigColumns.Single(column=>column.Id==id).Extra["filterControlKind"]=JsonSerializer.SerializeToElement("CheckBox");
        }
        var model=GenerationModel.Create(catalog,"Example.SparseStoredMultiTextParent");
        var table=model.Tables.Single(item=>item.Source.Id=="2");
        var page=Assert.Single(table.Pages);
        var multi=MultiSelectCompilation.Resolve(model,table,page,table.Columns.Single(column=>column.Source.Id=="704"));
        var checkbox=DatabaseCheckboxCompilation.Resolve(model,table,page,table.Columns.Single(column=>column.Source.Id=="706"));
        Assert.True(multi.Definition is {LogicalSourceParent:true,SharedTextParent:not null,UsesTargetPageScope:true},multi.Reason);
        Assert.True(checkbox.Definition is {UsesPageTextParent:true,PhysicalTargetPage:not null,UsesPageRead:true},checkbox.Reason);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var bindings=NonRedundantCrossTableCompilation.Bindings(model,table,page);
        var targets=NonRedundantReadCompilation.ProjectMultiTargets(model,table,page);
        Assert.Equal(2,targets.Count);
        var filters=Filters(catalog);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-sparse-stored-multi-text");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new ApplicationPackager().Create(catalog,directory,model.Namespace,NonRedundantApplicationFixture.Root());
            Assert.Equal(filters,Filters(await PublishedReadCatalog(directory)));
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/Services/TextrelSourceService.g.cs"));
            Assert.Contains("targetParentId=LogicalParentId201(logicalRelationParentId)",service);
            Assert.Contains("targetParentId=LogicalParentId201(parentId)",service);
            Assert.Contains("ApplyTextParent(query,session,\"country_id\",targetParentId!,businessKey:true)",service);
            Assert.Contains("ApplyTextParent(query,session,\"country_id\",parentId!,businessKey:true)",service);
            Assert.Contains("CheckboxScope201_706Async(logicalRelationParentId",service);
            Assert.Contains("ExportCheckbox201_706Async(entities,actor,request.ParentId",service);
            Assert.DoesNotContain("out var targetParentKey",service);
            var probe=NonRedundantApplicationFixture.Start("node",directory);
            probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"GeneratedMultiTextParentProbe.mjs"));
            probe.ArgumentList.Add(Path.Combine(directory,"frontend"));
            probe.ArgumentList.Add("sparse");
            await NonRedundantApplicationFixture.RunAsync(probe);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }

        string[] Filters(MetadataCatalog metadata)
        {
            var plan=Metarill.Data.Queries.NonRedundantReads.CompileProjectPage(metadata,"2",
                ["multiitem_names","checkitem_names"],[],bindings,[],targets);
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var mysql=plan.GetType().GetProperty("MySql",flags)!.GetValue(plan)!;
            var filters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,string>>(
                mysql.GetType().GetProperty("ArrayNamePredicates",flags)!.GetValue(mysql));
            Assert.Equal(2,filters.Count);
            Assert.All(filters.Values,sql=>
            {
                Assert.Contains("BINARY CAST(multi_value.`country_id` AS CHAR)=BINARY CAST((CASE WHEN",sql);
                Assert.DoesNotContain("multi_parent.",sql);
            });
            return filters.OrderBy(filter=>filter.Key,StringComparer.Ordinal).Select(filter=>filter.Value).ToArray();
        }
    }

    /// <summary>名称数组只接受同一业务键父路径，即使目标列本身也是字符串。</summary>
    [Theory]
    [InlineData("83","704","multiitem_names")]
    [InlineData("84","706","checkitem_names")]
    public void Sparse_source_text_parent_rejects_stored_multi_target_primary_key(string tableId,string relationId,string names)
    {
        var catalog=Catalog(0,"OnlyNew","list");
        ScopeTarget(catalog,tableId);
        var targetParent=catalog.ConfigColumns.Single(item=>item.Id==tableId+"06");
        var definition=System.Text.Json.Nodes.JsonNode.Parse(targetParent.ControlDetail)!;
        definition["levels"]![0]!["idField"]="id";
        targetParent.ControlDetail=definition.ToJsonString();
        var model=GenerationModel.Create(catalog,"Example.SparseStoredMultiDifferentKey");
        var table=model.Tables.Single(item=>item.Source.Id=="2");
        var page=Assert.Single(table.Pages);
        var relation=table.Columns.Single(column=>column.Source.Id==relationId);
        if(relationId=="704")
            Assert.Null(MultiSelectCompilation.Resolve(model,table,page,relation).Definition);
        else
            Assert.Null(DatabaseCheckboxCompilation.Resolve(model,table,page,relation).Definition);
        var binding=new Metarill.Data.Queries.NonRedundantMultiTargetScope(relationId,"201",tableId+"0",true);
        var error=Assert.Throws<InvalidOperationException>(()=>Metarill.Data.Queries.NonRedundantReads.CompileProjectPage(
            catalog,"2",[names],[],[],[],[binding]));
        Assert.Contains("相同发布文本父路径",error.Message);
    }

    /// <summary>冗余多选与复选的保存、回填及筛选都沿非冗余目标的实际文本父路径。</summary>
    [Theory]
    [InlineData("OnlyNew","list")]
    [InlineData("OverWriteByFirstColumn","edit")]
    public async Task Stored_source_sparse_multi_targets_share_text_parent(string import,string export)
    {
        var catalog=Catalog(1,import,export);
        foreach(var id in new[]{"83","84"})
        {
            ScopeTarget(catalog,id);
            catalog.ConfigTables.Single(item=>item.Id==id).IsRongyu=0;
        }
        foreach(var id in new[]{"704","706"})
        {
            catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId==id)
                .Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
            catalog.ConfigColumns.Single(column=>column.Id==id).Extra["filterControlKind"]=JsonSerializer.SerializeToElement("CheckBox");
        }
        var model=GenerationModel.Create(catalog,"Example.StoredSparseMultiTextParent");
        var table=model.Tables.Single(item=>item.Source.Id=="2");
        var page=Assert.Single(table.Pages);
        var multi=MultiSelectCompilation.Resolve(model,table,page,table.Columns.Single(column=>column.Source.Id=="704"));
        var checkbox=DatabaseCheckboxCompilation.Resolve(model,table,page,table.Columns.Single(column=>column.Source.Id=="706"));
        Assert.True(multi.Definition is {SameParent:true,LogicalSourceParent:false,SharedTextParent:not null},multi.Reason);
        Assert.True(checkbox.Definition is {LogicalSourceParent:false,LogicalTargetPage:not null,SharedTextParent:not null},checkbox.Reason);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var maintenance=MixedRelationPropagationCompilation.ForModel(model);
        Assert.Contains("textrel_multiitem",maintenance.MixedTargets);
        Assert.Contains("textrel_checkitem",maintenance.MixedTargets);
        Assert.Contains("multiitem_names",maintenance.MixedSourceColumns["textrel_source"]);
        Assert.Contains("checkitem_names",maintenance.MixedSourceColumns["textrel_source"]);
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(maintenance.GetType().GetProperty("Plans",flags)!.GetValue(maintenance));
        var source=plans["2"]!;
        Assert.Null(source.GetType().GetProperty("Error")!.GetValue(source));
        var read=source.GetType().GetProperty("Read")!.GetValue(source)!;
        var reverse=Assert.IsAssignableFrom<System.Collections.IEnumerable>(read.GetType().GetProperty("Reverse",flags)!.GetValue(read));
        var country=catalog.ConfigTables.Single(item=>item.Id=="3").TableName;
        foreach(var column in new[]{"multiitem_names","checkitem_names"})
        {
            var names=Assert.IsType<string>(read.GetType().GetMethod("Expression",flags)!.Invoke(read,[column]));
            Assert.Contains("BINARY CAST(b.`country_id` AS CHAR)",names);
            Assert.Contains("business_key",names);
            Assert.DoesNotContain("multi_parent.",names);
            Assert.Contains(reverse.Cast<object>(),entry=>
            {
                var type=entry.GetType();
                var outputs=Assert.IsAssignableFrom<IReadOnlyDictionary<string,IReadOnlySet<string>>>(type.GetProperty("Outputs")!.GetValue(entry));
                var key=type.GetProperty("NodeKey",flags)!.GetValue(entry);
                return type.GetProperty("TargetTable")!.GetValue(entry) as string==country&&outputs.ContainsKey(column)&&
                    key?.GetType().GetProperty("Column")?.GetValue(key) as string=="business_key";
            });
        }

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-stored-sparse-multi-text");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new ApplicationPackager().Create(catalog,directory,model.Namespace,NonRedundantApplicationFixture.Root());
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/Services/TextrelSourceService.g.cs"));
            Assert.Contains("targetParentId=SharedR1ParentKey201(entity.CountryId)",service);
            Assert.Contains("targetParentId=SharedR1ParentKey201(parentId)",service);
            Assert.Contains("DerivedCheckboxRows201_706Async(entity.CountryId",service);
            Assert.Contains("ImportCheckbox201_706Async",service);
            Assert.Contains("ApplyJsonArrayHasAnyId(session,query,entity=>entity.MultiitemIds",service);
            Assert.Contains("ApplyJsonArrayHasAnyId(session,query,entity=>entity.CheckitemIds",service);
            Assert.Contains("entity.MultiitemNames,",service);
            Assert.Contains("entity.CheckitemNames,",service);
            Assert.DoesNotContain("out var targetParentKey",service);
            var probe=NonRedundantApplicationFixture.Start("node",directory);
            probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"GeneratedMultiTextParentProbe.mjs"));
            probe.ArgumentList.Add(Path.Combine(directory,"frontend"));
            probe.ArgumentList.Add("sparse");
            await NonRedundantApplicationFixture.RunAsync(probe);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>直接编译多值缓存时也必须拒绝不同类型的实际父身份。</summary>
    [Theory]
    [InlineData("83","704","705")]
    [InlineData("84","706","707")]
    public void Stored_source_text_parent_rejects_sparse_multi_target_primary_key(string targetId,string relationId,string cacheId)
    {
        var catalog=Catalog(1,"OnlyNew","list");
        ScopeTarget(catalog,targetId);
        catalog.ConfigTables.Single(item=>item.Id==targetId).IsRongyu=0;
        var column=catalog.ConfigColumns.Single(item=>item.Id==targetId+"06");
        var definition=System.Text.Json.Nodes.JsonNode.Parse(column.ControlDetail)!;
        definition["levels"]![0]!["idField"]="id";
        column.ControlDetail=definition.ToJsonString();
        var cache=new Metarill.Data.Relations.RelationCacheDefinition("2",relationId,targetId,targetId+"02",cacheId,true);
        var error=Assert.Throws<InvalidOperationException>(()=>Metarill.Data.Relations.DerivedCrossTableCaches.Validate(catalog,[cache],[]));
        Assert.Contains("相同发布文本父路径",error.Message);
    }

    /// <summary>两端冗余时，改名传播和维护重算仍须跟原下拉及单选的文本父范围一致。</summary>
    [Fact]
    public void Stored_single_names_refresh_from_published_text_parent()
    {
        var catalog=Catalog(1,"OnlyNew","list");
        ScopeTarget(catalog,"81");
        ScopeTarget(catalog,"82");
        catalog.ConfigColumns.Single(column=>column.Id=="700").Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("810");
        var model=GenerationModel.Create(catalog,"Example.StoredSingleTextCache");
        var bindings=RelationPropagationCompilation.DerivedBindings(model);
        Assert.Equal(2,bindings.Count(binding=>binding.Foreign.Source.Id is "700" or "702"));
        var maintenance=MixedRelationPropagationCompilation.ForModel(model);
        Assert.Contains("textrel_listitem",maintenance.MixedTargets);
        Assert.Contains("textrel_singleitem",maintenance.MixedTargets);
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(maintenance.GetType().GetProperty("Plans",flags)!.GetValue(maintenance));
        var source=plans["2"]!;
        Assert.Null(source.GetType().GetProperty("Error")!.GetValue(source));
        var read=source.GetType().GetProperty("Read")!.GetValue(source)!;
        var from=Assert.IsType<string>(read.GetType().GetProperty("From",flags)!.GetValue(read));
        Assert.Contains("`business_key`",from);
        var reverse=Assert.IsAssignableFrom<System.Collections.IEnumerable>(read.GetType().GetProperty("Reverse",flags)!.GetValue(read));
        var country=catalog.ConfigTables.Single(item=>item.Id=="3").TableName;
        foreach(var column in new[]{"listitem_name","singleitem_name"})
        {
            var label=Assert.IsType<string>(read.GetType().GetMethod("Expression",flags)!.Invoke(read,[column]));
            Assert.Matches(@"BINARY CAST\(\w+\.`country_id` AS CHAR\)=BINARY CAST\(b\.`country_id` AS CHAR\)",label);
            Assert.DoesNotContain("single_parent_",label);
            Assert.Contains(reverse.Cast<object>(),entry=>
            {
                var type=entry.GetType();
                var outputs=Assert.IsAssignableFrom<IReadOnlyDictionary<string,IReadOnlySet<string>>>(type.GetProperty("Outputs")!.GetValue(entry));
                var key=type.GetProperty("NodeKey",flags)!.GetValue(entry);
                return type.GetProperty("TargetTable")!.GetValue(entry) as string==country&&outputs.ContainsKey(column)&&
                    key?.GetType().GetProperty("Column")?.GetValue(key) as string=="business_key";
            });
            Assert.Contains(reverse.Cast<object>(),entry=>
            {
                var type=entry.GetType();
                var outputs=Assert.IsAssignableFrom<IReadOnlyDictionary<string,IReadOnlySet<string>>>(type.GetProperty("Outputs")!.GetValue(entry));
                return type.GetProperty("TargetTable")!.GetValue(entry) as string=="textrel_"+column[..^5]&&
                    outputs.TryGetValue(column,out var fields)&&fields.Contains("country_id");
            });
        }
    }

    /// <summary>旧缓存配置中把文本父列改到主键层位时，维护入口也明确拒绝。</summary>
    [Fact]
    public void Stored_single_refresh_rejects_different_parent_keys()
    {
        var catalog=Catalog(1,"OnlyNew","list");
        ScopeTarget(catalog,"82");
        var column=catalog.ConfigColumns.Single(item=>item.Id=="8206");
        var definition=System.Text.Json.Nodes.JsonNode.Parse(column.ControlDetail)!;
        definition["levels"]![0]!["idField"]="id";
        column.ControlDetail=definition.ToJsonString();
        var cache=new Metarill.Data.Relations.RelationCacheDefinition("2","702","82","8202","703",false){ScopedSinglePageId="820"};
        var error=Assert.Throws<InvalidOperationException>(()=>Metarill.Data.Relations.DerivedCrossTableCaches.Validate(catalog,[cache],[]));
        Assert.Contains("相同发布文本父路径",error.Message);
    }

    /// <summary>旧下拉只有实际标记为生成器管理的名称列才增加缓存登记。</summary>
    [Fact]
    public void Stored_legacy_list_keeps_handwritten_text_column()
    {
        var catalog=Catalog(1,"OnlyNew","list");
        ScopeTarget(catalog,"81");
        var foreign=catalog.ConfigColumns.Single(column=>column.Id=="700");
        foreign.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("810");
        catalog.ConfigColumns.Single(column=>column.Id=="701").Extra.Remove("isJoinColumn");
        var source=catalog.ConfigTables.Single(table=>table.Id=="2");
        var target=catalog.ConfigTables.Single(table=>table.Id=="81");
        Assert.Null(ProjectScopedSingleRelationTarget.Resolve(catalog,source,foreign,target));
    }

    /// <summary>两端冗余的数组名称在回填时沿同一原选档文本父键和真实祖先。</summary>
    [Fact]
    public void Stored_multi_names_refresh_from_published_text_parent()
    {
        var catalog=Catalog(1,"OnlyNew","list");
        ScopeTarget(catalog,"83");
        ScopeTarget(catalog,"84");
        var model=GenerationModel.Create(catalog,"Example.StoredMultiTextCache");
        var bindings=RelationPropagationCompilation.DerivedBindings(model);
        Assert.Equal(2,bindings.Count(binding=>binding.Foreign.Source.Id is "704" or "706"));
        var maintenance=MixedRelationPropagationCompilation.ForModel(model);
        Assert.Contains("textrel_multiitem",maintenance.MixedTargets);
        Assert.Contains("textrel_checkitem",maintenance.MixedTargets);
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(maintenance.GetType().GetProperty("Plans",flags)!.GetValue(maintenance));
        var source=plans["2"]!;
        Assert.Null(source.GetType().GetProperty("Error")!.GetValue(source));
        var read=source.GetType().GetProperty("Read")!.GetValue(source)!;
        var reverse=Assert.IsAssignableFrom<System.Collections.IEnumerable>(read.GetType().GetProperty("Reverse",flags)!.GetValue(read));
        var country=catalog.ConfigTables.Single(item=>item.Id=="3").TableName;
        foreach(var column in new[]{"multiitem_names","checkitem_names"})
        {
            var label=Assert.IsType<string>(read.GetType().GetMethod("Expression",flags)!.Invoke(read,[column]));
            Assert.Contains("BINARY CAST(multi_value.`country_id` AS CHAR)=BINARY CAST(b.`country_id` AS CHAR)",label);
            Assert.Contains("`business_key`",label);
            Assert.DoesNotContain("multi_parent.",label);
            Assert.Contains(reverse.Cast<object>(),entry=>
            {
                var type=entry.GetType();
                var outputs=Assert.IsAssignableFrom<IReadOnlyDictionary<string,IReadOnlySet<string>>>(type.GetProperty("Outputs")!.GetValue(entry));
                var key=type.GetProperty("NodeKey",flags)!.GetValue(entry);
                return type.GetProperty("TargetTable")!.GetValue(entry) as string==country&&outputs.ContainsKey(column)&&
                    key?.GetType().GetProperty("Column")?.GetValue(key) as string=="business_key";
            });
            Assert.Contains(reverse.Cast<object>(),entry=>
            {
                var type=entry.GetType();
                var outputs=Assert.IsAssignableFrom<IReadOnlyDictionary<string,IReadOnlySet<string>>>(type.GetProperty("Outputs")!.GetValue(entry));
                return type.GetProperty("TargetTable")!.GetValue(entry) as string=="textrel_"+column[..^6]&&
                    outputs.TryGetValue(column,out var fields)&&fields.Contains("country_id");
            });
        }
    }

    /// <summary>两种原多值控件都拒绝以同表的整数主键替代发布的文本祖先。</summary>
    [Theory]
    [InlineData("83","704","705")]
    [InlineData("84","706","707")]
    public void Stored_multi_refresh_rejects_different_parent_keys(string targetId,string relationId,string cacheId)
    {
        var catalog=Catalog(1,"OnlyNew","list");
        ScopeTarget(catalog,targetId);
        var column=catalog.ConfigColumns.Single(item=>item.Id=="25");
        var definition=System.Text.Json.Nodes.JsonNode.Parse(column.ControlDetail)!;
        definition["levels"]![0]!["idField"]="id";
        column.ControlDetail=definition.ToJsonString();
        var cache=new Metarill.Data.Relations.RelationCacheDefinition("2",relationId,targetId,targetId+"02",cacheId,true);
        var error=Assert.Throws<InvalidOperationException>(()=>Metarill.Data.Relations.DerivedCrossTableCaches.Validate(catalog,[cache],[]));
        Assert.Contains("相同发布文本父路径",error.Message);
    }

    /// <summary>使用生成应用实际装载的精简目录重放查询，包含页级范围与原控件绑定。</summary>
    private static async Task<MetadataCatalog> PublishedReadCatalog(string directory)
    {
        var registration=await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/GeneratedRegistration.g.cs"));
        var snapshot=System.Text.RegularExpressions.Regex.Match(registration,
            """MetadataCatalog>\(("(?:\\.|[^"\\])*"),new System\.Text\.Json""");
        Assert.True(snapshot.Success,"生成应用须内置其实际发布的读取目录。");
        return JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Deserialize<string>(snapshot.Groups[1].Value)!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    /// <summary>固定候选以真实业务键关联父路径，同时避免将业务键与整数主键再比较。</summary>
    private static void AssertTextParentSql(Metarill.Data.Queries.NonRedundantReadPlan plan)
    {
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var mysql=plan.GetType().GetProperty("MySql",flags)!.GetValue(plan)!;
        var scope=Assert.IsType<string>(mysql.GetType().GetProperty("Scope",flags)!.GetValue(mysql));
        var from=Assert.IsType<string>(mysql.GetType().GetProperty("From",flags)!.GetValue(mysql));
        Assert.Contains("`business_key`",from);
        Assert.DoesNotContain("multi_parent.`id`",scope);
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
