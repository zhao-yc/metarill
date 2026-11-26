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
        Assert.True(Assert.Single(DatabaseCheckboxCompilation.ForPage(model,table,page)).IndependentSourceParent);
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

    /// <summary>整数级联父校验来源页，独立复选字典继续按自己的项目和发布范围读取。</summary>
    [Theory]
    [InlineData(false,"OnlyNew","list")]
    [InlineData(true,"OverWriteByFirstColumn","edit")]
    [InlineData(false,"OnlyNew","list",1,false)]
    [InlineData(true,"OverWriteByFirstColumn","edit",1,false)]
    [InlineData(false,"OnlyNew","list",1,true)]
    [InlineData(true,"OverWriteByFirstColumn","edit",1,true)]
    [InlineData(false,"OnlyNew","list",1,true,true)]
    [InlineData(true,"OverWriteByFirstColumn","edit",1,false,true)]
    [InlineData(false,"OnlyNew","list",1,false,false,true)]
    [InlineData(true,"OverWriteByFirstColumn","edit",1,true,true,true)]
    [InlineData(true,"OverWriteByFirstColumn","edit",1,false,false,true,true)]
    public async Task Integer_parent_with_independent_checkbox_compiles_original_pages(bool businessKey,string import,string export,
        int redundant=0,bool globalCountry=false,bool storedAsText=false,bool compound=false,bool numericCascade=false)
    {
        var catalog=Catalog(redundant,import,export);
        foreach(var column in catalog.ConfigColumns.Where(column=>column.Id=="business_3"||
            column.Id=="282"&&!storedAsText||numericCascade&&column.Id=="business_5"))
        {
            column.LogicalType="int64";
            column.ColumnLength="";
        }
        if(!businessKey)
        {
            var source=catalog.ConfigColumns.Single(column=>column.Id=="25");
            var control=System.Text.Json.Nodes.JsonNode.Parse(source.ControlDetail)!;
            control["levels"]![0]!["idField"]="id";
            source.ControlDetail=control.ToJsonString();
        }
        if(compound)
        {
            var source=catalog.ConfigColumns.Single(column=>column.Id=="25");
            var control=System.Text.Json.Nodes.JsonNode.Parse(source.ControlDetail)!;
            var levels=control["levels"]!.AsArray();
            levels.RemoveAt(2);
            levels[0]!["columnPrefix"]="other_country";
            levels[1]!["columnPrefix"]="other_area";
            catalog.ConfigColumns.AddRange([
                new(){Id="290",ConfigTableId="2",ColumnName="other_country_id",LogicalType="int64",
                    LinkConfigTableId="3",YzProjectId=catalog.YzProjectId},
                new(){Id="291",ConfigTableId="2",ColumnName="other_area_id",LogicalType="int64",ControlKind="CascadeTree",
                    LinkConfigTableId="4",ControlDetail=control.ToJsonString(),YzProjectId=catalog.YzProjectId}
            ]);
            catalog.ConfigPageColumns.Add(new(){Id="201291",ConfigPageId="201",ConfigColumnId="291",IsShow=1,IsEdit=1,
                YzProjectId=catalog.YzProjectId});
            catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql+=" AND other_country_id=@parent_id";
        }
        if(globalCountry)
        {
            catalog.ConfigTables.Single(table=>table.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            catalog.ConfigColumns.RemoveAll(column=>column.ConfigTableId=="3"&&column.ColumnName=="yz_project_id");
            catalog.ConfigPages.Single(page=>page.Id=="301").WhereSql="status=1 AND store_id=@current_store_id";
            catalog.ConfigColumns.AddRange([
                new(){Id="country_store",ConfigTableId="3",ColumnName="store_id",LogicalType="int64",YzProjectId=catalog.YzProjectId},
                new(){Id="source_store",ConfigTableId="2",ColumnName="store_id",LogicalType="int64",YzProjectId=catalog.YzProjectId}
            ]);
            // 冗余名称传播按来源行的持久门店归属重算。
            catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql+=" AND store_id=@current_store_id";
            catalog.ConfigIndexColumns.RemoveAll(column=>column.GetProperty("configIndexId").GetString()=="business_index_3"&&
                column.GetProperty("configColumnId").GetString()!="business_3");
        }
        var model=GenerationModel.Create(catalog,"Example.IntegerParentRelations");
        var table=model.Tables.Single(item=>item.Source.Id=="2");
        var page=Assert.Single(table.Pages);
        var field=table.Columns.Single(item=>item.Source.Id=="706");
        var cascade=table.Columns.Single(item=>item.Source.Id=="25");
        var path=CrossTableWriteCompilation.Resolve(model,table,page,cascade);
        Assert.True(path.Definition is not null,path.Reason);
        var resolved=DatabaseCheckboxCompilation.Resolve(model,table,page,field);
        Assert.True(resolved.Definition is not null,resolved.Reason);
        Assert.DoesNotContain(resolved.Definition.Scopes,scope=>scope.Source=="parent");
        if(compound)Assert.Equal(2,resolved.Definition.SourceScopeColumns.Count);
        if(redundant==0)Assert.True(resolved.Definition is {LogicalSourceParent:true,IndependentSourceParent:true});
        else
        {
            var binding=Assert.Single(RelationPropagationCompilation.Bindings(model),item=>item.Foreign.Source.Id=="706");
            Assert.Equal("checkitem_names",binding.Cache.Source.ColumnName);
        }
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        if(compound&&numericCascade)
        {
            // 同一张表的整数主键和业务键可能数值相同，生成时仍须拒绝混用。
            var branch=catalog.ConfigColumns.Single(column=>column.Id=="291");
            var original=branch.ControlDetail;
            try
            {
                var control=System.Text.Json.Nodes.JsonNode.Parse(original)!;
                control["levels"]![0]!["idField"]="id";
                branch.ControlDetail=control.ToJsonString();
                var wrong=GenerationModel.Create(catalog,model.Namespace);
                var owner=wrong.Tables.Single(item=>item.Source.Id=="2");
                var result=DatabaseCheckboxCompilation.Resolve(wrong,owner,Assert.Single(owner.Pages),
                    owner.Columns.Single(item=>item.Source.Id=="706"));
                Assert.Null(result.Definition);
                Assert.Contains("同一个已发布父键",result.Reason);
            }
            finally { branch.ControlDetail=original; }
        }

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-integer-parent-relations");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new ApplicationPackager().Create(catalog,directory,model.Namespace,NonRedundantApplicationFixture.Root());
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/Services/TextrelSourceService.g.cs"));
            var start=service.IndexOf("CheckboxScope201_706Async(string? parentId",StringComparison.Ordinal);
            Assert.True(start>=0);
            var stop=service.IndexOf("return locking?query.ForUpdate():query;",start,StringComparison.Ordinal);
            Assert.True(stop>start);
            var scope=service[start..stop];
            var parentReader=redundant==0?"LogicalParentId201(parentId)":"SharedR1ParentKey201(parentId)";
            Assert.Contains(parentReader,scope);
            Assert.Contains("CrossReader201_25(actor,locking,false)",scope);
            if(compound)
            {
                Assert.Contains("CrossReader201_291(actor,locking,false)",scope);
                Assert.Contains("复选的来源父字段必须属于同一父记录",service);
            }
            var key=redundant==0?"parentKey":parentReader;
            Assert.Contains(numericCascade?$"PathAsync(0,{key},cancellationToken)":
                $"PathByKeyAsync(0,{key}.ToString(CultureInfo.InvariantCulture),cancellationToken)",scope);
            Assert.DoesNotContain("parentQuery=",scope);
            if(globalCountry)Assert.Contains("store_id=@current_store_id",service);
            Assert.Contains("row.YzProjectId==long.Parse(actor.ProjectId",scope);
            Assert.DoesNotContain("row.CountryId",scope);
            Assert.Contains("ImportCheckbox201_706Async",service);
            if(redundant==0)Assert.Contains("ExportCheckbox201_706Async(entities,actor,request.ParentId",service);
            else
            {
                // 冗余工作簿保存原 ID 数组，通过同一写入服务校验候选及更新名称副本。
                Assert.Contains("DatabaseCheckboxValue.ProjectIds(DatabaseCheckboxValue.ReadWrite(cell))",service);
                Assert.Contains("ValidateCheckbox201_706Async",service);
            }
            if(redundant==0&&import=="OverWriteByFirstColumn")
            {
                Assert.Contains("ImportMatchesAsync(",service);
                Assert.Contains("if(importMatches.Count>1)",service);
            }
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

    /// <summary>原下拉及单选在同一非冗余字典父范围内叠加实际发布页的角色权限。</summary>
    [Theory]
    [InlineData(0,"OnlyNew","list")]
    [InlineData(0,"OverWriteByFirstColumn","edit")]
    [InlineData(1,"OnlyNew","list")]
    [InlineData(1,"OverWriteByFirstColumn","edit")]
    public Task Single_text_parent_keeps_exact_dictionary_permission(int sourceStorage,string import,string export)=>
        VerifySingleExactDictionary(sourceStorage,0,import,export);

    /// <summary>原下拉和单选在冗余字典上将角色权限与文本父键同时绑定到候选及写入。</summary>
    [Theory]
    [InlineData(0,"OnlyNew","list")]
    [InlineData(0,"OverWriteByFirstColumn","edit")]
    [InlineData(1,"OnlyNew","list")]
    [InlineData(1,"OverWriteByFirstColumn","edit")]
    public Task Stored_single_dictionary_text_parent_keeps_exact_permission(int sourceStorage,string import,string export)=>
        VerifySingleExactDictionary(sourceStorage,1,import,export);

    private static async Task VerifySingleExactDictionary(int sourceStorage,int targetStorage,string import,string export)
    {
        var catalog=Catalog(sourceStorage,import,export);
        if(targetStorage==1)
            catalog.ConfigColumns.Single(column=>column.Id=="700").Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("810");
        foreach(var id in new[]{"81","82"})
        {
            ScopeTarget(catalog,id);
            catalog.ConfigTables.Single(table=>table.Id==id).IsRongyu=targetStorage;
            catalog.ConfigColumns.Add(new(){Id=id+"09",ConfigTableId=id,YzProjectId=catalog.YzProjectId,
                ColumnName="scope_code",LogicalType="string"});
            var dictionary=catalog.ConfigPages.Single(item=>item.ConfigTableId==id);
            dictionary.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("scope_code");
            var conditionField=targetStorage==0?"area_name":"scope_code";
            var exclusion=id=="81"?"下拉隐藏地区":"单选隐藏地区";
            dictionary.WhereSql+=$" AND {conditionField} <> '{exclusion}'";
        }
        foreach(var admin in catalog.YzAdminTypes)
            admin.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("real_name");
        var model=GenerationModel.Create(catalog,"Example.TextParentExactSingle");
        var owner=model.Tables.Single(table=>table.Source.Id=="2");
        var page=Assert.Single(owner.Pages);
        var relations=RelationCompilation.ForPage(model,page,true,forExcel:true);
        Assert.Equal(2,relations.Count);
        Assert.All(relations,relation=>
        {
            Assert.Equal("country_id",relation.SharedTextParent?.Source.ColumnName);
            Assert.Equal("scope_code",DerivedRelationNamesCompilation.CandidateExact(model,owner,relation)?.Column.Source.ColumnName);
        });
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-text-parent-exact-single");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new ApplicationPackager().Create(catalog,directory,model.Namespace,NonRedundantApplicationFixture.Root());
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/Services/TextrelSourceService.g.cs"));
            Assert.Contains("ResolveExactDataManage(\"810\", \"list\", \"real_name\")",service);
            Assert.Contains("ResolveExactDataManage(\"820\", \"list\", \"real_name\")",service);
            Assert.Contains("\"scope_code\", FixedReadComparison.In",service);
            Assert.Contains("OptionsScope201_700Async(",service);
            Assert.Contains("OptionsScope201_702Async(",service);
            Assert.Contains("ImportRelation201_700Async",service);
            Assert.Contains("ImportRelation201_702Async",service);
            Assert.Contains("Export201Async",service);
            if(targetStorage==1)
                Assert.Equal(2,System.Text.RegularExpressions.Regex.Matches(service,
                    System.Text.RegularExpressions.Regex.Escape("ForTextParent(\"country_id\",targetParentId!,businessKey:true)")).Count);
            var published=await PublishedReadCatalog(directory);
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            foreach(var relation in relations)
            {
                if(targetStorage==0)
                {
                    var plan=Metarill.Data.Queries.NonRedundantReads.CompileExactSingleParentCandidate(published,
                        relation.Target.Source.Id,relation.TargetPage.Source.Id,["id","name"],["scope_code"],[],[]);
                    AssertTextParentSql(plan);
                    var sql=plan.GetType().GetProperty("MySql",flags)!.GetValue(plan)!;
                    var scope=Assert.IsType<string>(sql.GetType().GetProperty("Scope",flags)!.GetValue(sql));
                    AssertExclusion(sql,scope,relation.SourceColumn.Source.ControlKind=="List"?"下拉隐藏地区":"单选隐藏地区");
                }
                else
                {
                    var suffix=relation.SourceColumn.Source.Id;
                    Assert.Contains($"DerivedOptionsPlan201_{suffix}",service);
                    Assert.Contains("ApplyTextParent(query,session,\"country_id\",targetParentId!,businessKey:true)",service);
                    Assert.Contains($"OptionsPageCondition201_{suffix}",service);
                    var plan=Metarill.Data.Queries.NonRedundantReads.CompilePhysicalRelation(published,relation.Target.Source.Id,
                        ["id","name"],["country_id","scope_code"],[]);
                    var sql=plan.GetType().GetProperty("MySql",flags)!.GetValue(plan)!;
                    var filterSql=typeof(Metarill.Data.Queries.MySqlNonRedundantReader).GetMethod("Filter",
                        System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
                    foreach(var key in new[]{"001","1","Country-A","country-a"})
                    {
                        var filter=Metarill.Data.Queries.FixedReadFilter.ForTextParent("country_id",key,businessKey:true);
                        Assert.Equal(key,filter.Value);
                        Assert.Contains("BINARY (b.`country_id`)=BINARY @filter_0",
                            Assert.IsType<string>(filterSql.Invoke(null,[sql,filter,0])));
                    }
                    Assert.Throws<ArgumentException>(()=>Metarill.Data.Queries.FixedReadFilter.ForTextParent("country_id"," ",businessKey:true));
                    Assert.Throws<ArgumentException>(()=>Metarill.Data.Queries.FixedReadFilter.ForTextParent("country_id","001"));
                }
            }
            if(sourceStorage==1)
            {
                var maintenance=MixedRelationPropagationCompilation.ForModel(model);
                Assert.Contains("textrel_listitem",maintenance.MixedTargets);
                Assert.Contains("textrel_singleitem",maintenance.MixedTargets);
                var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(maintenance.GetType().GetProperty("Plans",flags)!.GetValue(maintenance));
                var source=plans["2"]!;
                Assert.Null(source.GetType().GetProperty("Error")!.GetValue(source));
                AssertNameExclusions(source.GetType().GetProperty("Read")!.GetValue(source)!,"BINARY CAST(b.`country_id` AS CHAR)");
            }
            else if(targetStorage==1)
            {
                string[] names=["listitem_name","singleitem_name"];
                var plan=Metarill.Data.Queries.NonRedundantReads.CompileProjectPage(published,"2",names,[],
                    NonRedundantCrossTableCompilation.Bindings(model,owner,page),NonRedundantReadCompilation.ProjectSingleTargets(model,owner,page,names));
                var sql=plan.GetType().GetProperty("MySql",flags)!.GetValue(plan)!;
                var joins=Assert.IsType<string>(sql.GetType().GetProperty("From",flags)!.GetValue(sql));
                foreach(var (field,exclusion) in new[]{("listitem_name","下拉隐藏地区"),("singleitem_name","单选隐藏地区")})
                {
                    var expression=Assert.IsType<string>(sql.GetType().GetMethod("Expression",flags)!.Invoke(sql,[field]));
                    var alias=System.Text.RegularExpressions.Regex.Match(expression,@"^(\w+)\.`name`$");
                    Assert.True(alias.Success,expression);
                    var dictionaryAlias=alias.Groups[1].Value;
                    Assert.Contains($"BINARY CAST({dictionaryAlias}.`country_id` AS CHAR)=BINARY CAST(CASE WHEN",joins);
                    AssertExclusion(sql,joins,exclusion,$"{dictionaryAlias}.`scope_code`<>@");
                }
            }
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }

        // 冗余来源回填的名称表达式应保留两个字典各自的固定筛选。
        static void AssertNameExclusions(object plan,string parentExpression)
        {
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            foreach(var (field,exclusion) in new[]{("listitem_name","下拉隐藏地区"),("singleitem_name","单选隐藏地区")})
            {
                var expression=Assert.IsType<string>(plan.GetType().GetMethod("Expression",flags)!.Invoke(plan,[field]));
                Assert.Contains(parentExpression,expression);
                AssertExclusion(plan,expression,exclusion);
            }
        }

        // 比较发布页真正绑定的常量，防止下拉和单选错误复用对方的过滤条件。
        static void AssertExclusion(object plan,string sql,string value,string prefix="")
        {
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(plan.GetType().GetProperty("Constants",flags)!.GetValue(plan));
            Assert.Contains(parameters,parameter=>Equals(parameter.Value,value)&&sql.Contains(prefix+parameter.Key,StringComparison.Ordinal));
        }
    }

    /// <summary>共用文本父键的原下拉、单选 Excel 沿实际级联字段执行控件附加条件。</summary>
    [Theory]
    [InlineData(0,false,false)]
    [InlineData(0,false,true)]
    [InlineData(0,true,false)]
    [InlineData(0,true,true)]
    [InlineData(1,false,false)]
    [InlineData(1,false,true)]
    [InlineData(1,true,false)]
    [InlineData(1,true,true)]
    public Task Single_text_parent_uses_control_conditions(int sourceStorage,bool exact,bool form)=>
        VerifySingleTextParentControlConditions(sourceStorage,exact,form,joined:false);

    /// <summary>下拉与单选 Excel 的联接条件须使用固定字典页，支持字面量和来源表单值。</summary>
    [Theory]
    [InlineData(0,false,false)]
    [InlineData(0,false,true)]
    [InlineData(0,true,false)]
    [InlineData(0,true,true)]
    [InlineData(1,false,false)]
    [InlineData(1,false,true)]
    [InlineData(1,true,false)]
    [InlineData(1,true,true)]
    public Task Single_text_parent_uses_joined_control_conditions(int sourceStorage,bool exact,bool form)=>
        VerifySingleTextParentControlConditions(sourceStorage,exact,form,joined:true);

    /// <summary>原下拉与单选 Excel 可以按冗余字典的已维护标量名称列筛选。</summary>
    [Theory]
    [InlineData(0,false,false)]
    [InlineData(0,false,true)]
    [InlineData(0,true,false)]
    [InlineData(0,true,true)]
    [InlineData(1,false,false)]
    [InlineData(1,false,true)]
    [InlineData(1,true,false)]
    [InlineData(1,true,true)]
    public Task Stored_text_parent_uses_cached_control_conditions(int sourceStorage,bool exact,bool form)=>
        VerifySingleTextParentControlConditions(sourceStorage,exact,form,joined:true,stored:true);

    /// <summary>原联接副本会继承标量控件，评分条件与关系显示名称使用两个实际字段。</summary>
    [Fact]
    public Task Stored_text_parent_uses_typed_cache_column()=>
        VerifySingleTextParentControlConditions(1,exact:true,form:true,joined:true,stored:true,typed:true);

    /// <summary>字典页常量使用实际维护列，独立生效并且可与来源控件附加条件叠加。</summary>
    [Theory]
    [InlineData(0,false,false)]
    [InlineData(0,false,true)]
    [InlineData(0,true,false)]
    [InlineData(0,true,true)]
    [InlineData(1,false,false)]
    [InlineData(1,false,true)]
    [InlineData(1,true,false)]
    [InlineData(1,true,true)]
    public Task Stored_text_parent_uses_cached_page_conditions(int sourceStorage,bool exact,bool form)=>
        VerifySingleTextParentControlConditions(sourceStorage,exact,form,joined:true,stored:true,pageCondition:true);

    /// <summary>非冗余字典自身的联接常量与实际页面及控件条件共用固定字典路径。</summary>
    [Theory]
    [InlineData(0,false,false)]
    [InlineData(0,false,true)]
    [InlineData(0,true,false)]
    [InlineData(0,true,true)]
    [InlineData(1,false,false)]
    [InlineData(1,false,true)]
    [InlineData(1,true,false)]
    [InlineData(1,true,true)]
    public Task Single_text_parent_uses_joined_page_conditions(int sourceStorage,bool exact,bool form)=>
        VerifySingleTextParentControlConditions(sourceStorage,exact,form,joined:true,pageCondition:true);

    /// <summary>条件使用公开别名时仍检查联接终点，避免通过候选结果推断凭据。</summary>
    [Fact]
    public void Single_text_parent_joined_control_condition_rejects_sensitive_terminal()
    {
        var catalog=Catalog(0,"OnlyNew","list");
        PublishTextParentJoinedLabels(catalog,"string",["81"]);
        catalog.ConfigColumns.Add(new(){Id="8606",ConfigTableId="86",YzProjectId=catalog.YzProjectId,
            ColumnName="password",LogicalType="string"});
        catalog.ConfigColumns.Single(column=>column.Id=="8102").Extra["columnFullName"]=
            JsonSerializer.SerializeToElement("label_item.password");
        var error=Assert.Throws<NotSupportedException>(()=>
            Metarill.Data.Queries.NonRedundantReads.CompileGlobalListCandidate(catalog,"81","810",["name"],["id"],[]));
        Assert.Contains("非凭据单值终点",error.Message);
    }

    /// <summary>原交互候选、写入、Excel 与名称维护共用同一个受限目标。</summary>
    private static async Task VerifySingleTextParentControlConditions(int sourceStorage,bool exact,bool form,bool joined,
        bool stored=false,bool typed=false,bool pageCondition=false)
    {
        var catalog=Catalog(sourceStorage,form?"OverWriteByFirstColumn":"OnlyNew",form?"edit":"list");
        var conditionField=typed?"label_item_score":stored?"label_item_name":joined?"display_region":"area_name";
        var conditionType=typed?"int32":"string";
        if(joined)
        {
            PublishTextParentJoinedLabels(catalog,"string",[]);
            if(typed)catalog.ConfigColumns.Add(new(){Id="8607",ConfigTableId="86",YzProjectId=catalog.YzProjectId,
                ColumnName="score",LogicalType="int32",ControlKind="Rate"});
            catalog.ConfigColumns.Add(new(){Id="8605",ConfigTableId="86",YzProjectId=catalog.YzProjectId,
                ColumnName="label_enabled",LogicalType="int32"});
            catalog.ConfigPages.Single(page=>page.Id=="860").WhereSql+=" AND label_enabled=1";
        }
        if(form)
        {
            catalog.ConfigColumns.Add(new(){Id="709",ConfigTableId="2",YzProjectId=catalog.YzProjectId,
                ColumnName="match_region",LogicalType=conditionType});
            catalog.ConfigPageColumns.Add(new(){Id="f709",ConfigPageId="201",ConfigColumnId="709",YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=1});
        }
        foreach(var (id,foreign) in new[]{("81","700"),("82","702")})
        {
            ScopeTarget(catalog,id);
            catalog.ConfigTables.Single(table=>table.Id==id).IsRongyu=stored?1:0;
            if(joined)
            {
                catalog.ConfigColumns.Add(new(){Id=id+"08",ConfigTableId=id,YzProjectId=catalog.YzProjectId,
                    ColumnName="label_item_id",LogicalType="int64",ControlKind="List",LinkConfigTableId="86",
                    ControlDetail="""{"tableId":"86","pageId":"860","nameField":"name"}""",
                    Extra={["linkConfigPageId"]=JsonSerializer.SerializeToElement("860")}});
                catalog.ConfigColumns.Add(new(){Id=id+"10",ConfigTableId=id,YzProjectId=catalog.YzProjectId,
                    ColumnName=conditionField,LogicalType=conditionType,ControlKind=typed?"Rate":"",Extra={
                    ["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
                    ["columnFullName"]=JsonSerializer.SerializeToElement(typed?"label_item.score":"label_item.name")}});
                catalog.ConfigPageColumns.Add(new(){Id="f"+id+"08",ConfigPageId=id+"0",ConfigColumnId=id+"08",YzProjectId=catalog.YzProjectId});
            }
            var control=catalog.ConfigColumns.Single(column=>column.Id==foreign);
            if(stored&&foreign=="700")control.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("810");
            var definition=JsonSerializer.Deserialize<Dictionary<string,string>>(control.ControlDetail)!;
            if(form||!pageCondition)
                definition["whereSql"]=form?$"{conditionField} = @formParameters.matchRegion":$"{conditionField} <> '控件排除{id}'";
            control.ControlDetail=JsonSerializer.Serialize(definition);
            var dictionary=catalog.ConfigPages.Single(item=>item.ConfigTableId==id);
            dictionary.WhereSql+=$" AND {(pageCondition?conditionField:stored?"name":"area_name")} <> '字典排除地区'";
            if(exact)
            {
                catalog.ConfigColumns.Add(new(){Id=id+"09",ConfigTableId=id,YzProjectId=catalog.YzProjectId,
                    ColumnName="scope_code",LogicalType="string"});
                dictionary.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("scope_code");
            }
        }
        if(exact)
            foreach(var admin in catalog.YzAdminTypes)
                admin.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("real_name");
        var model=GenerationModel.Create(catalog,"Example.TextParentSingleControls");
        var owner=model.Tables.Single(table=>table.Source.Id=="2");
        var page=Assert.Single(owner.Pages);
        var relations=RelationCompilation.ForPage(model,page,true,forExcel:true);
        var list=Assert.Single(relations,relation=>relation.SourceColumn.Source.Id=="700");
        var single=Assert.Single(relations,relation=>relation.SourceColumn.Source.Id=="702");
        Assert.All(relations,relation=>Assert.Equal("country_id",relation.SharedTextParent?.Source.ColumnName));
        Assert.Null(single.Conditions);
        var hasControlCondition=form||!pageCondition;
        if(hasControlCondition)
        {
            Assert.NotNull(list.Conditions);
            Assert.NotNull(single.ImportCondition);
            Assert.Contains(conditionField,stored?list.Conditions.Syntax.Fields:joined?list.Conditions.JoinedFields:list.Conditions.CrossFields);
            Assert.Contains(conditionField,stored?single.ImportCondition.Syntax.Fields:joined?single.ImportCondition.JoinedFields:single.ImportCondition.CrossFields);
            Assert.Equal(form,list.Conditions.Forms.Count==1);
            Assert.Equal(form,single.ImportCondition.Forms.Count==1);
        }
        else
        {
            Assert.Null(list.Conditions);
            Assert.Null(single.ImportCondition);
        }
        if(pageCondition)Assert.All(relations,relation=>Assert.Contains(conditionField,relation.TargetPage.Condition!.Syntax.Fields));
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        if(sourceStorage==1)
        {
            var maintenance=MixedRelationPropagationCompilation.ForModel(model);
            Assert.Contains("listitem_name",maintenance.MixedSourceColumns["textrel_source"]);
            Assert.Contains("singleitem_name",maintenance.MixedSourceColumns["textrel_source"]);
            if(joined)Assert.Contains("textrel_name_dictionary",maintenance.MixedTargets);
        }
        var compilePackage=form==(sourceStorage==1);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-single-text-parent-control-conditions");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=compilePackage?Path.Combine(directory,"backend/generated"):directory;
            if(compilePackage)new ApplicationPackager().Create(catalog,directory,model.Namespace,NonRedundantApplicationFixture.Root());
            else new CSharpGenerator().Generate(catalog,directory,model.Namespace);
            var service=await File.ReadAllTextAsync(Path.Combine(generated,"Services/TextrelSourceService.g.cs"));
            if(hasControlCondition)
            {
                Assert.Contains(stored?"OptionsCondition201_700.Apply(":"OptionsCondition201_700.BindVariables",service);
                Assert.Contains("SingleImportCondition201_702",service);
            }
            if(joined&&(!stored||exact))Assert.Contains("DerivedOptionsPlan201_700",service);
            Assert.DoesNotContain("OptionsCondition201_702",service);
            Assert.Contains("ImportRelation201_700Async",service);
            Assert.Contains("ImportRelation201_702Async",service);
            Assert.Contains("Export201Async",service);
            if(form)
            {
                Assert.Contains("MatchRegion",service);
                Assert.Contains("relationContext[\"matchRegion\"]",service);
            }
            var maintenanceOnly=stored&&sourceStorage==1&&!exact;
            var metadata=compilePackage?await PublishedReadCatalog(directory,maintenanceOnly):catalog;
            if(stored)
            {
                Assert.Contains("ApplyTextParent(query,session,\"country_id\",targetParentId!,businessKey:true)",service);
                Assert.Contains(JsonSerializer.Serialize(conditionField),service);
                if(pageCondition)
                {
                    Assert.Contains("OptionsPageCondition201_700",service);
                    Assert.Contains("OptionsPageCondition201_702",service);
                }
                AssertStoredControlConditions(model,owner,page,metadata,sourceStorage,conditionField,pageCondition);
                if(!exact&&!form&&sourceStorage==0)
                {
                    catalog.ConfigColumns.Add(new(){Id="8606",ConfigTableId="86",YzProjectId=catalog.YzProjectId,
                        ColumnName="password",LogicalType="string"});
                    catalog.ConfigColumns.Single(column=>column.Id=="8110").Extra["columnFullName"]=
                        JsonSerializer.SerializeToElement("label_item.password");
                    Assert.Throws<NotSupportedException>(()=>Metarill.Data.Queries.NonRedundantReads.CompileStoredSingleRelation(
                        catalog,"81","810",["id","name"],["country_id",conditionField],[conditionField]));
                    if(pageCondition)
                    {
                        var unsafeModel=GenerationModel.Create(catalog,"Example.UnsafeStoredPageCondition");
                        var unsafePage=Assert.Single(unsafeModel.Tables.Single(table=>table.Source.Id=="2").Pages);
                        Assert.Throws<NotSupportedException>(()=>RelationCompilation.ForPage(unsafeModel,unsafePage,true,forExcel:true));
                    }
                }
                if(compilePackage)
                    foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
                return;
            }
            var listTargets=DerivedRelationNamesCompilation.ProjectCandidateTargets(model,list);
            var selectTargets=NonRedundantReadCompilation.ProjectPathTargets(model,single.Target,single.TargetPage,[conditionField]);
            var listPlan=exact?Metarill.Data.Queries.NonRedundantReads.CompileExactSingleParentCandidate(metadata,"81","810",["id"],["scope_code"],[],listTargets,hasControlCondition?[conditionField]:null)
                :hasControlCondition?Metarill.Data.Queries.NonRedundantReads.CompileGlobalListCandidate(metadata,"81","810",[conditionField],["id"],[],listTargets)
                :Metarill.Data.Queries.NonRedundantReads.CompileCrossPageCandidate(metadata,"81","810",["id"],[],null,listTargets);
            var selectPlan=exact?Metarill.Data.Queries.NonRedundantReads.CompileExactSingleParentCandidate(metadata,"82","820",["id","name"],["scope_code"],[],selectTargets,hasControlCondition?[conditionField]:null)
                :hasControlCondition?Metarill.Data.Queries.NonRedundantReads.CompileSingleImportLabel(metadata,"82","820","name",[conditionField],singleTargets:selectTargets)
                :Metarill.Data.Queries.NonRedundantReads.CompileCrossPageCandidate(metadata,"82","820",["id","name"],[],null,selectTargets);
            AssertTextParentSql(listPlan);
            AssertTextParentSql(selectPlan);
            using var session=new Metarill.Data.DatabaseSession(new(){ExpectedDatabase="offline",
                ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused"});
            var variables=new Dictionary<string,object?>();
            if(form)variables.Add("formParameters.matchRegion","实际选择地区");
            var sql=hasControlCondition
                ?Metarill.Data.Queries.NonRedundantCrossTableScopeSql.ApplyCondition(session.Select<ScopeRow>(),session,listPlan,
                    Metarill.Data.Queries.FixedConditionSqlPlan.Compile(list.Conditions!.Syntax,list.Conditions.Types).BindVariables(variables),
                    "single_relation",catalog.YzProjectId,parentId:"001").ToSql()
                :Metarill.Data.Queries.NonRedundantCrossTableScopeSql.ApplyCandidate(session.Select<ScopeRow>(),session,
                    listPlan,"single_relation",catalog.YzProjectId,locking:false,parentId:"001").ToSql();
            Assert.Contains("BINARY @single_relation_parent",sql);
            if(hasControlCondition)Assert.Contains("@single_relation_condition_",sql);
            if(joined)
            {
                Assert.Contains("`textrel_name_dictionary`",sql);
                Assert.Contains("`label_enabled`",sql);
                const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var readPlan=selectPlan.GetType().GetProperty("MySql",flags)!.GetValue(selectPlan)!;
                var from=Assert.IsType<string>(readPlan.GetType().GetProperty("From",flags)!.GetValue(readPlan));
                var expression=Assert.IsType<string>(readPlan.GetType().GetMethod("Expression",flags)!.Invoke(readPlan,[conditionField]));
                Assert.Contains("`textrel_name_dictionary`",from);
                Assert.Contains("`label_enabled`",from+expression);
            }
            if(pageCondition)
            {
                AssertJoinedPageCondition(listPlan);
                AssertJoinedPageCondition(selectPlan);
                var condition=list.TargetPage.Condition!;
                var target=metadata.ConfigTables.Single(item=>item.Id=="81");
                var targetPage=metadata.ConfigPages.Single(item=>item.Id=="810");
                var membership=Metarill.Data.Queries.NonRedundantReads.CompilePageMembership(metadata,target,targetPage,
                    condition.Syntax.Fields,condition.CrossFields,condition.SelfTreeFields,["id"],[],listTargets);
                var current=Metarill.Data.Queries.NonRedundantCrossTableScopeSql.ApplyCondition(session.Select<ScopeRow>(),session,membership,
                    Metarill.Data.Queries.FixedConditionSqlPlan.Compile(condition.Syntax,condition.Types),
                    "dictionary_page",catalog.YzProjectId).ToSql();
                Assert.Contains("`textrel_name_dictionary`",current);
                Assert.Contains("@dictionary_page_condition_",current);
                Assert.Contains("CrossConditionPlan810",await File.ReadAllTextAsync(Path.Combine(generated,"Services/TextrelListitemService.g.cs")));
            }
            if(compilePackage)
                foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>确认字典页常量通过参数进入候选及 Excel 查询条件。</summary>
    private static void AssertJoinedPageCondition(Metarill.Data.Queries.NonRedundantReadPlan plan)
    {
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var mysql=plan.GetType().GetProperty("MySql",flags)!.GetValue(plan)!;
        var scope=Assert.IsType<string>(mysql.GetType().GetProperty("Scope",flags)!.GetValue(mysql));
        var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
            mysql.GetType().GetProperty("Constants",flags)!.GetValue(mysql));
        Assert.Contains(parameters,pair=>Equals(pair.Value,"字典排除地区")&&scope.Contains("@"+pair.Key,StringComparison.Ordinal));
    }

    /// <summary>候选和实时名称读取实际物理副本，冗余来源维护保留筛选列的反向依赖。</summary>
    private static void AssertStoredControlConditions(GenerationModel model,TableModel owner,PageModel page,
        MetadataCatalog metadata,int sourceStorage,string field,bool pageCondition)
    {
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        foreach(var id in new[]{"81","82"})
        {
            var plan=Metarill.Data.Queries.NonRedundantReads.CompileStoredSingleRelation(metadata,id,id+"0",
                ["id","name"],["country_id",field],[field]);
            var sql=plan.GetType().GetProperty("MySql",flags)!.GetValue(plan)!;
            Assert.Equal($"b.`{field}`",sql.GetType().GetMethod("Expression",flags)!.Invoke(sql,[field]));
            Assert.DoesNotContain("`textrel_name_dictionary`",Assert.IsType<string>(sql.GetType().GetProperty("From",flags)!.GetValue(sql)));
        }
        if(sourceStorage==0)
        {
            string[] names=["listitem_name","singleitem_name"];
            var plan=Metarill.Data.Queries.NonRedundantReads.CompileProjectPage(metadata,"2",names,[],
                NonRedundantCrossTableCompilation.Bindings(model,owner,page),NonRedundantReadCompilation.ProjectSingleTargets(model,owner,page,names));
            var sql=plan.GetType().GetProperty("MySql",flags)!.GetValue(plan)!;
            var from=Assert.IsType<string>(sql.GetType().GetProperty("From",flags)!.GetValue(sql));
            Assert.Contains($".`{field}`",from);
            Assert.Contains("BINARY CAST",from);
            if(pageCondition)AssertPageConstants(sql,from);
            return;
        }
        var maintenance=MixedRelationPropagationCompilation.ForModel(model);
        var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(maintenance.GetType().GetProperty("Plans",flags)!.GetValue(maintenance));
        var source=plans["2"]!;
        Assert.Null(source.GetType().GetProperty("Error")!.GetValue(source));
        var read=source.GetType().GetProperty("Read")!.GetValue(source)!;
        var expression=read.GetType().GetMethod("Expression",flags)!;
        Assert.Contains($".`{field}`",Assert.IsType<string>(expression.Invoke(read,["listitem_name"])));
        var single=Assert.IsType<string>(expression.Invoke(read,["singleitem_name"]));
        if(pageCondition)
        {
            Assert.Contains($".`{field}`",single);
            AssertPageConstants(read,single);
        }
        else Assert.DoesNotContain($".`{field}`",single);
        var reverse=Assert.IsAssignableFrom<System.Collections.IEnumerable>(read.GetType().GetProperty("Reverse",flags)!.GetValue(read));
        var dependency=Assert.Single(reverse.Cast<object>(),item=>
            Equals(item.GetType().GetProperty("TargetTable")!.GetValue(item),"textrel_listitem"));
        var outputs=Assert.IsAssignableFrom<IReadOnlyDictionary<string,IReadOnlySet<string>>>(
            dependency.GetType().GetProperty("Outputs")!.GetValue(dependency));
        Assert.Contains(field,outputs["listitem_name"]);
        if(pageCondition)
        {
            var selection=Assert.Single(reverse.Cast<object>(),item=>
                Equals(item.GetType().GetProperty("TargetTable")!.GetValue(item),"textrel_singleitem"));
            var selectionOutputs=Assert.IsAssignableFrom<IReadOnlyDictionary<string,IReadOnlySet<string>>>(
                selection.GetType().GetProperty("Outputs")!.GetValue(selection));
            Assert.Contains(field,selectionOutputs["singleitem_name"]);
        }

        // 使用实际参数位置证明页面常量进入了联接谓词，避免只检查字段被投影。
        static void AssertPageConstants(object plan,string sql)
        {
            var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                plan.GetType().GetProperty("Constants",flags)!.GetValue(plan));
            Assert.Contains(parameters,pair=>Equals(pair.Value,"字典排除地区")&&sql.Contains("@"+pair.Key,StringComparison.Ordinal));
        }
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

    /// <summary>原复选的页面简称使用同一已发布文本父级，全称和简称都登记可回填副本。</summary>
    [Theory]
    [InlineData("OnlyNew","list",1)]
    [InlineData("OverWriteByFirstColumn","edit",1)]
    [InlineData("OnlyNew","list",0)]
    [InlineData("OverWriteByFirstColumn","edit",0)]
    public async Task Stored_checkbox_text_parent_keeps_both_published_names(string import,string export,int targetStorage)
    {
        var catalog=Catalog(1,import,export);
        ScopeTarget(catalog,"84");
        catalog.ConfigTables.Single(item=>item.Id=="84").IsRongyu=targetStorage;
        PublishCheckboxShortName(catalog,storedCache:true);
        var model=GenerationModel.Create(catalog,"Example.StoredCheckboxTextParentLabels");
        var table=model.Tables.Single(item=>item.Source.Id=="2");
        var page=Assert.Single(table.Pages);
        var resolved=DatabaseCheckboxCompilation.Resolve(model,table,page,table.Columns.Single(field=>field.Source.Id=="706"));
        Assert.True(resolved.Definition is {SharedTextParent:not null,UsesNameOverride:true},resolved.Reason);
        Assert.Equal("checkitem_short_names",resolved.Definition!.Names!.Source.ColumnName);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var definitions=RelationPropagationCompilation.DerivedBindings(model)
            .Where(binding=>binding.Foreign.Source.Id=="706").ToArray();
        Assert.Equal(new[]{"name","short_name"},definitions.Select(binding=>binding.Value.Source.ColumnName).Order(StringComparer.Ordinal));
        Assert.All(definitions,binding=>
        {
            Assert.Equal("840",binding.CheckboxTargetPageId);
            Assert.Equal(targetStorage==1?"282":null,binding.CheckboxSourceParentId);
        });
        var maintained=MixedRelationPropagationCompilation.ForModel(model);
        Assert.Contains("textrel_checkitem",maintained.MixedTargets);
        Assert.Contains("checkitem_names",maintained.MixedSourceColumns["textrel_source"]);
        Assert.Contains("checkitem_short_names",maintained.MixedSourceColumns["textrel_source"]);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-checkbox-text-parent-labels");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new ApplicationPackager().Create(catalog,directory,model.Namespace,NonRedundantApplicationFixture.Root());
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/Services/TextrelSourceService.g.cs"));
            Assert.Contains("CheckboxScope201_706Async",service);
            if(targetStorage==1)
            {
                Assert.Contains("\"country_id\",parentId!,businessKey:true)",service);
                Assert.Contains("Name=row.ShortName",service);
                Assert.Contains("new {row.Id,row.Name,row.ShortName}",service);
            }
            else
            {
                Assert.Contains("DerivedCheckboxRows201_706Async(entity.CountryId",service);
                Assert.Contains("names[id].CacheName",service);
                Assert.Contains("names[id].CacheShortName",service);
            }
            Assert.Contains("ImportCheckbox201_706Async",service);
            Assert.Contains("Export201Async",service);
            Assert.Contains("entity.CheckitemNames",service);
            Assert.Contains("entity.CheckitemShortNames",service);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>非冗余业务表的原名称字段按页面实时读取简称，保留文本同父校验。</summary>
    [Theory]
    [InlineData(0,"OnlyNew","list")]
    [InlineData(0,"OverWriteByFirstColumn","edit")]
    [InlineData(1,"OnlyNew","list")]
    [InlineData(1,"OverWriteByFirstColumn","edit")]
    public async Task Sparse_checkbox_text_parent_reads_the_published_short_name(int targetStorage,string import,string export)
    {
        var catalog=Catalog(0,import,export);
        ScopeTarget(catalog,"84");
        catalog.ConfigTables.Single(item=>item.Id=="84").IsRongyu=targetStorage;
        PublishCheckboxShortName(catalog,storedCache:false);
        var setting=catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="706");
        setting.Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="707").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        var model=GenerationModel.Create(catalog,"Example.SparseCheckboxShortName");
        var owner=model.Tables.Single(item=>item.Source.Id=="2");
        var page=Assert.Single(owner.Pages);
        var resolved=DatabaseCheckboxCompilation.Resolve(model,owner,page,owner.Columns.Single(field=>field.Source.Id=="706"));
        Assert.True(resolved.Definition is {UsesPageTextParent:true,UsesNameOverride:true},resolved.Reason);
        Assert.Equal("checkitem_names",resolved.Definition!.Names!.Source.ColumnName);
        Assert.Equal("short_name",resolved.Definition.Name.Source.ColumnName);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var bindings=NonRedundantCrossTableCompilation.Bindings(model,owner,page);
        var targets=NonRedundantReadCompilation.ProjectMultiTargets(model,owner,page);
        Assert.Contains(targets,target=>target.ColumnId=="706"&&target.TargetPageId=="840"&&target.ApplyPageScope);
        var full=NameFilter(catalog);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-sparse-checkbox-text-short-name");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new ApplicationPackager().Create(catalog,directory,model.Namespace,NonRedundantApplicationFixture.Root());
            Assert.Equal(full,NameFilter(await PublishedReadCatalog(directory)));
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/Services/TextrelSourceService.g.cs"));
            Assert.Contains("ImportCheckbox201_706Async",service);
            Assert.Contains("ExportCheckbox201_706Async(entities,actor,request.ParentId",service);
            Assert.Contains("\"checkitem_names\",FixedReadComparison.JsonArrayAnyContains",service);
            Assert.Contains(targetStorage==0?"DerivedCheckboxRows201_706Async(logicalRelationParentId":
                "CheckboxScope201_706Async(logicalRelationParentId",service);
            Assert.Contains(targetStorage==0?"\"short_name\"":".ShortName",service);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }

        string NameFilter(MetadataCatalog metadata)
        {
            var plan=Metarill.Data.Queries.NonRedundantReads.CompileProjectPage(metadata,"2",["checkitem_names"],[],bindings,[],targets);
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var mysql=plan.GetType().GetProperty("MySql",flags)!.GetValue(plan)!;
            var filters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,string>>(mysql.GetType().GetProperty("ArrayNamePredicates",flags)!.GetValue(mysql));
            var sql=Assert.Single(filters).Value;
            Assert.Contains("`short_name`",sql);
            Assert.Contains("BINARY CAST",sql);
            Assert.Contains("`business_key`",sql);
            Assert.DoesNotContain("multi_parent.",sql);
            return sql;
        }
    }

    /// <summary>原多选和复选在同一文本父级下分别执行各自字典页的级联名称条件。</summary>
    [Theory]
    [InlineData(0,"OnlyNew","list")]
    [InlineData(0,"OverWriteByFirstColumn","edit")]
    [InlineData(1,"OnlyNew","list")]
    [InlineData(1,"OverWriteByFirstColumn","edit")]
    public async Task Multi_controls_text_parent_keeps_dictionary_cascade_conditions(int sourceStorage,string import,string export)
    {
        var catalog=Catalog(sourceStorage,import,export);
        foreach(var id in new[]{"83","84"})
        {
            ScopeTarget(catalog,id);
            catalog.ConfigTables.Single(item=>item.Id==id).IsRongyu=0;
        }
        catalog.ConfigPages.Single(item=>item.Id=="830").WhereSql+=" AND area_name <> '排除多选地区'";
        catalog.ConfigPages.Single(item=>item.Id=="840").WhereSql+=" AND area_name <> '排除地区'";
        PublishCheckboxShortName(catalog,storedCache:sourceStorage==1);
        var model=GenerationModel.Create(catalog,"Example.MultiTextParentConditions");
        var owner=model.Tables.Single(item=>item.Source.Id=="2");
        var page=Assert.Single(owner.Pages);
        var relation=owner.Columns.Single(column=>column.Source.Id=="706");
        var result=DatabaseCheckboxCompilation.Resolve(model,owner,page,relation);
        Assert.True(result.Definition is {UsesNameOverride:true,SharedTextParent:not null,LogicalTargetPage:not null},result.Reason);
        var multi=MultiSelectCompilation.Resolve(model,owner,page,owner.Columns.Single(column=>column.Source.Id=="704"));
        Assert.True(multi.Definition is {SameParent:true,SharedTextParent:not null},multi.Reason);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        if(sourceStorage==1)
        {
            var maintenance=MixedRelationPropagationCompilation.ForModel(model);
            Assert.Contains("checkitem_names",maintenance.MixedSourceColumns["textrel_source"]);
            Assert.Contains("checkitem_short_names",maintenance.MixedSourceColumns["textrel_source"]);
            Assert.Contains("multiitem_names",maintenance.MixedSourceColumns["textrel_source"]);
            var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(maintenance.GetType().GetProperty("Plans",flags)!.GetValue(maintenance));
            var source=plans["2"]!;
            var read=source.GetType().GetProperty("Read")!.GetValue(source)!;
            foreach(var output in new[]{"multiitem_names","checkitem_names","checkitem_short_names"})
            {
                var expression=Assert.IsType<string>(read.GetType().GetMethod("Expression",flags)!.Invoke(read,[output]));
                AssertCondition(read,expression,output=="multiitem_names"?"排除多选地区":"排除地区");
            }
        }
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-multi-text-parent-conditions");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var expected=new[]{DictionaryScope(catalog,checkbox:false),DictionaryScope(catalog,checkbox:true)};
            new ApplicationPackager().Create(catalog,directory,model.Namespace,NonRedundantApplicationFixture.Root());
            var published=await PublishedReadCatalog(directory);
            Assert.Equal(expected,new[]{DictionaryScope(published,checkbox:false),DictionaryScope(published,checkbox:true)});
            if(sourceStorage==0)
            {
                var listPlan=Metarill.Data.Queries.NonRedundantReads.CompileProjectPage(published,"2",["multiitem_names","checkitem_names"],[],
                    NonRedundantCrossTableCompilation.Bindings(model,owner,page),[],NonRedundantReadCompilation.ProjectMultiTargets(model,owner,page));
                var sql=listPlan.GetType().GetProperty("MySql",flags)!.GetValue(listPlan)!;
                var filters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,string>>(sql.GetType().GetProperty("ArrayNamePredicates",flags)!.GetValue(sql));
                Assert.Equal(2,filters.Count);
                AssertCondition(sql,filters["multiitem_names"],"排除多选地区");
                AssertCondition(sql,filters["checkitem_names"],"排除地区");
                Assert.Contains("`short_name`",filters["checkitem_names"]);
            }
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/Services/TextrelSourceService.g.cs"));
            Assert.Contains("CompileScopedCheckboxLabel(",service);
            Assert.Contains("DerivedCheckboxRows201_706Async",service);
            Assert.Contains("ImportCheckbox201_706Async",service);
            Assert.Contains("CompileScopedMultiLabel(\"83\",\"830\",\"name\"",service);
            Assert.Contains("ValidateMulti201_704Async",service);
            Assert.Contains("ImportMulti201_704Async",service);
            Assert.Contains("Export201Async",service);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }

        string DictionaryScope(MetadataCatalog metadata,bool checkbox)
        {
            var plan=checkbox
                ?Metarill.Data.Queries.NonRedundantReads.CompileScopedCheckboxLabel(metadata,"84","840","short_name",["id","short_name"],[],false)
                :Metarill.Data.Queries.NonRedundantReads.CompileScopedMultiLabel(metadata,"83","830","name",["id","name"],[]);
            AssertTextParentSql(plan);
            var mysql=plan.GetType().GetProperty("MySql",flags)!.GetValue(plan)!;
            var scope=Assert.IsType<string>(mysql.GetType().GetProperty("Scope",flags)!.GetValue(mysql));
            AssertCondition(mysql,scope,checkbox?"排除地区":"排除多选地区");
            Assert.DoesNotContain("b.`area_name`",scope);
            return scope;
        }

        void AssertCondition(object mysql,string sql,string expected)
        {
            var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(mysql.GetType().GetProperty("Constants",flags)!.GetValue(mysql));
            Assert.Contains("<>",sql);
            Assert.Contains(parameters,parameter=>Equals(parameter.Value,expected)&&sql.Contains(parameter.Key,StringComparison.Ordinal));
        }
    }

    /// <summary>按显示类型覆盖冗余及非冗余来源和字典，轮流启用原页精确数据管理。</summary>
    public static IEnumerable<object[]> SingleScalarTextParentCases()
    {
        string[] types=["int32","int64","decimal","double","bool","date","datetime","time"];
        foreach(var (type,index) in types.Select((type,index)=>(type,index)))
            foreach(var source in new[]{0,1})
                foreach(var target in new[]{0,1})
                    yield return [type,source,target,(index+source+target)%2==0];
    }

    /// <summary>下拉和单选保留各标量显示类型，并按原文本父键限定候选、保存、Excel 及名称副本。</summary>
    [Theory]
    [MemberData(nameof(SingleScalarTextParentCases))]
    public async Task Single_text_parent_preserves_physical_scalar_labels(string kind,int sourceStorage,int targetStorage,bool exact)
    {
        var catalog=Catalog(sourceStorage,exact?"OverWriteByFirstColumn":"OnlyNew",exact?"edit":"list");
        if(targetStorage==1)
            catalog.ConfigColumns.Single(column=>column.Id=="700").Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("810");
        foreach(var (id,displayId) in new[]{("81","701"),("82","703")})
        {
            ScopeTarget(catalog,id);
            catalog.ConfigTables.Single(table=>table.Id==id).IsRongyu=targetStorage;
            foreach(var column in catalog.ConfigColumns.Where(column=>column.Id==id+"02"||column.Id==displayId))
            {
                column.LogicalType=kind;
                column.ColumnLength=kind=="decimal"?"24,6":"";
            }
            var dictionary=catalog.ConfigPages.Single(page=>page.ConfigTableId==id);
            if(targetStorage==0)dictionary.WhereSql+=" AND area_name <> '排除地区'";
            if(exact)
            {
                catalog.ConfigColumns.Add(new(){Id=id+"09",ConfigTableId=id,YzProjectId=catalog.YzProjectId,
                    ColumnName="scope_code",LogicalType="string"});
                dictionary.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("scope_code");
            }
        }
        if(exact)
            foreach(var admin in catalog.YzAdminTypes)
                admin.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("real_name");
        var model=GenerationModel.Create(catalog,"Example.TextParentSingleScalars");
        var owner=model.Tables.Single(item=>item.Source.Id=="2");
        var page=Assert.Single(owner.Pages);
        var relations=RelationCompilation.ForPage(model,page,true,forExcel:true);
        Assert.Equal(2,relations.Count);
        Assert.All(relations,relation=>
        {
            Assert.Equal(kind,relation.NameColumn.Source.LogicalType);
            Assert.Equal("country_id",relation.SharedTextParent?.Source.ColumnName);
            Assert.Equal(exact,DerivedRelationNamesCompilation.CandidateExact(model,owner,relation) is not null);
        });
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        if(sourceStorage==1)
        {
            var maintenance=MixedRelationPropagationCompilation.ForModel(model);
            Assert.Contains("listitem_name",maintenance.MixedSourceColumns["textrel_source"]);
            Assert.Contains("singleitem_name",maintenance.MixedSourceColumns["textrel_source"]);
        }
        var compilePackage=(kind,sourceStorage,targetStorage) is ("time",0,0) or ("double",0,1) or ("decimal",1,0) or ("int64",1,1);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-single-text-parent-scalars");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=compilePackage?Path.Combine(directory,"backend/generated"):directory;
            if(compilePackage)new ApplicationPackager().Create(catalog,directory,model.Namespace,NonRedundantApplicationFixture.Root());
            else new CSharpGenerator().Generate(catalog,directory,model.Namespace);
            var service=await File.ReadAllTextAsync(Path.Combine(generated,"Services/TextrelSourceService.g.cs"));
            Assert.Contains("DerivedOptionsPlan201_700",service);
            Assert.Contains("DerivedOptionsPlan201_702",service);
            Assert.Contains("ImportRelation201_700Async",service);
            Assert.Contains("ImportRelation201_702Async",service);
            Assert.Contains("Export201Async",service);
            Assert.Contains(kind switch
            {
                "int32"=>"int.TryParse(label",
                "int64"=>"long.TryParse(label",
                _=>"MultiValueLabel.Parse(\""+kind+"\""
            },service);
            if(compilePackage)
                foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>字典非字符串显示名沿用已有文本格式，选档与名称读取仍匹配真实祖先。</summary>
    [Theory]
    [InlineData("int32",0,0,false)]
    [InlineData("int64",1,1,true)]
    [InlineData("decimal",1,0,true)]
    [InlineData("double",0,1,true)]
    [InlineData("bool",1,1,false)]
    [InlineData("date",0,1,false)]
    [InlineData("datetime",1,0,false)]
    [InlineData("time",0,0,true)]
    public async Task Multi_text_parent_preserves_physical_scalar_labels(string kind,int sourceStorage,int targetStorage,bool buildPackage)
    {
        var catalog=Catalog(sourceStorage,"OverWriteByFirstColumn","edit");
        foreach(var id in new[]{"83","84"})
        {
            ScopeTarget(catalog,id);
            catalog.ConfigTables.Single(table=>table.Id==id).IsRongyu=targetStorage;
            var label=catalog.ConfigColumns.Single(column=>column.Id==id+"02");
            label.LogicalType=kind;
            if(kind=="decimal")label.ColumnLength="24,6";
            if(targetStorage==0)
                catalog.ConfigPages.Single(page=>page.ConfigTableId==id).WhereSql+=" AND area_name <> '排除地区'";
        }
        var model=GenerationModel.Create(catalog,"Example.TextParentScalarLabels");
        var owner=model.Tables.Single(table=>table.Source.Id=="2");
        var page=Assert.Single(owner.Pages);
        var multi=MultiSelectCompilation.Resolve(model,owner,page,owner.Columns.Single(column=>column.Source.Id=="704"));
        var checkbox=DatabaseCheckboxCompilation.Resolve(model,owner,page,owner.Columns.Single(column=>column.Source.Id=="706"));
        Assert.True(multi.Definition is {SameParent:true,SharedTextParent:not null},multi.Reason);
        Assert.True(checkbox.Definition is {TypedLabel:true,SharedTextParent:not null},checkbox.Reason);
        Assert.Equal(kind,multi.Definition!.Name.Source.LogicalType);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        if(sourceStorage==1)
        {
            var maintenance=MixedRelationPropagationCompilation.ForModel(model);
            Assert.Contains("multiitem_names",maintenance.MixedSourceColumns["textrel_source"]);
            Assert.Contains("checkitem_names",maintenance.MixedSourceColumns["textrel_source"]);
        }
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-text-parent-scalar-labels");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            string generated;
            if(buildPackage)
            {
                new ApplicationPackager().Create(catalog,directory,model.Namespace,NonRedundantApplicationFixture.Root());
                generated=Path.Combine(directory,"backend/generated");
            }
            else
            {
                new CSharpGenerator().Generate(catalog,directory,model.Namespace);
                generated=directory;
            }
            var service=await File.ReadAllTextAsync(Path.Combine(generated,"Services/TextrelSourceService.g.cs"));
            Assert.Contains("ImportMulti201_704Async",service);
            Assert.Contains("ImportCheckbox201_706Async",service);
            Assert.Contains("Export201Async",service);
            Assert.Contains(kind switch
            {
                "int32"=>"int.TryParse(label",
                "int64"=>"long.TryParse(label",
                _=>"MultiValueLabel.Parse(\""+kind+"\""
            },service);
            if(buildPackage)
                foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>按文本和全部原标量类型覆盖两种来源，并分别验证普通字典和精确数据字典。</summary>
    public static IEnumerable<object[]> SingleJoinedTextParentCases()
    {
        string[] types=["string","int32","int64","decimal","double","bool","date","datetime","time"];
        foreach(var (type,index) in types.Select((type,index)=>(type,index)))
            foreach(var source in new[]{0,1})
                yield return [type,source,(index+source)%2==0];
    }

    /// <summary>已发布文本父级的下拉和单选从字典固定联接取名，并保留写入及 Excel 范围。</summary>
    [Theory]
    [MemberData(nameof(SingleJoinedTextParentCases))]
    public async Task Single_text_parent_reads_names_from_fixed_join(string kind,int sourceStorage,bool exact)
    {
        var catalog=Catalog(sourceStorage,exact?"OverWriteByFirstColumn":"OnlyNew",exact?"edit":"list");
        PublishTextParentJoinedLabels(catalog,kind,["81","82"]);
        catalog.ConfigColumns.Add(new(){Id="8605",ConfigTableId="86",YzProjectId=catalog.YzProjectId,
            ColumnName="label_enabled",LogicalType="int32"});
        catalog.ConfigPages.Single(page=>page.Id=="860").WhereSql+=" AND label_enabled=1";
        foreach(var link in catalog.ConfigColumns.Where(column=>column.Id is "8108" or "8208"))
        {
            link.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("860");
            link.ControlDetail="""{"tableId":"86","pageId":"860","nameField":"name"}""";
        }
        foreach(var display in catalog.ConfigColumns.Where(column=>column.Id is "701" or "703"))
        {
            display.LogicalType=kind;
            display.ColumnLength=kind=="decimal"?"24,6":"";
        }
        if(exact)
        {
            foreach(var id in new[]{"81","82"})
            {
                catalog.ConfigColumns.Add(new(){Id=id+"09",ConfigTableId=id,YzProjectId=catalog.YzProjectId,
                    ColumnName="scope_code",LogicalType="string"});
                catalog.ConfigPages.Single(page=>page.ConfigTableId==id).Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("scope_code");
            }
            foreach(var admin in catalog.YzAdminTypes)
                admin.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("real_name");
        }
        var model=GenerationModel.Create(catalog,"Example.TextParentJoinedSingle");
        var owner=model.Tables.Single(item=>item.Source.Id=="2");
        var page=Assert.Single(owner.Pages);
        var relations=RelationCompilation.ForPage(model,page,true,forExcel:true);
        Assert.Equal(2,relations.Count);
        Assert.All(relations,relation=>
        {
            Assert.Equal("country_id",relation.SharedTextParent?.Source.ColumnName);
            Assert.True(DerivedRelationNamesCompilation.Declared(model,relation));
            Assert.Equal(kind,relation.NameColumn.Source.LogicalType);
            Assert.Equal(exact,DerivedRelationNamesCompilation.CandidateExact(model,owner,relation) is not null);
        });
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        if(sourceStorage==1)
        {
            var maintenance=MixedRelationPropagationCompilation.ForModel(model);
            Assert.Contains("textrel_name_dictionary",maintenance.MixedTargets);
            Assert.Contains("listitem_name",maintenance.MixedSourceColumns["textrel_source"]);
            Assert.Contains("singleitem_name",maintenance.MixedSourceColumns["textrel_source"]);
        }
        var compilePackage=kind is "string" or "int64";
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-single-text-parent-joined-names");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=compilePackage?Path.Combine(directory,"backend/generated"):directory;
            if(compilePackage)new ApplicationPackager().Create(catalog,directory,model.Namespace,NonRedundantApplicationFixture.Root());
            else new CSharpGenerator().Generate(catalog,directory,model.Namespace);
            var service=await File.ReadAllTextAsync(Path.Combine(generated,"Services/TextrelSourceService.g.cs"));
            Assert.Contains("DerivedOptionsPlan201_700",service);
            Assert.Contains("DerivedOptionsPlan201_702",service);
            Assert.Contains("ImportRelation201_700Async",service);
            Assert.Contains("ImportRelation201_702Async",service);
            Assert.Contains("Export201Async",service);
            if(exact)
            {
                Assert.Contains("ResolveExactDataManage(\"810\", \"list\", \"real_name\")",service);
                Assert.Contains("ResolveExactDataManage(\"820\", \"list\", \"real_name\")",service);
            }
            var metadata=compilePackage?await PublishedReadCatalog(directory):catalog;
            foreach(var relation in relations)
            {
                var targets=DerivedRelationNamesCompilation.ProjectCandidateTargets(model,relation);
                var tableId=relation.Target.Source.Id;
                var pageId=relation.TargetPage.Source.Id;
                var plan=exact?Metarill.Data.Queries.NonRedundantReads.CompileExactSingleParentCandidate(metadata,tableId,pageId,["id","name"],["scope_code"],[],targets)
                    :Metarill.Data.Queries.NonRedundantReads.CompileCrossPageCandidate(metadata,tableId,pageId,["id","name"],[],null,targets);
                AssertTextParentSql(plan);
                Assert.Equal(kind,plan.OutputTypes["name"]);
                const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var sql=plan.GetType().GetProperty("MySql",flags)!.GetValue(plan)!;
                var from=Assert.IsType<string>(sql.GetType().GetProperty("From",flags)!.GetValue(sql));
                var name=Assert.IsType<string>(sql.GetType().GetMethod("Expression",flags)!.Invoke(sql,["name"]));
                Assert.Contains("`textrel_name_dictionary`",from);
                Assert.Contains("`label_enabled`=@",from+name);
                var scope=Assert.IsType<string>(sql.GetType().GetProperty("Scope",flags)!.GetValue(sql));
                var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(sql.GetType().GetProperty("Constants",flags)!.GetValue(sql));
                Assert.Contains(parameters,parameter=>Equals(parameter.Value,"排除地区")&&scope.Contains(parameter.Key,StringComparison.Ordinal));
            }
            if(compilePackage)
                foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>显示列使用普通名称时也要检查联接终点，凭据不得被发布为字典名称。</summary>
    [Theory]
    [InlineData("8102")]
    [InlineData("8202")]
    public void Single_text_parent_join_rejects_sensitive_terminal(string labelId)
    {
        var catalog=Catalog(0,"OnlyNew","list");
        PublishTextParentJoinedLabels(catalog,"string",["81","82"]);
        catalog.ConfigColumns.Add(new(){Id="8606",ConfigTableId="86",YzProjectId=catalog.YzProjectId,
            ColumnName="password",LogicalType="string"});
        catalog.ConfigColumns.Single(column=>column.Id==labelId).Extra["columnFullName"]=JsonSerializer.SerializeToElement("label_item.password");
        var model=GenerationModel.Create(catalog,"Example.TextParentProtectedLabel");
        var owner=model.Tables.Single(table=>table.Source.Id=="2");
        var page=Assert.Single(owner.Pages);
        var error=Assert.Throws<NotSupportedException>(()=>RelationCompilation.ForPage(model,page,forService:true,forExcel:true));
        Assert.Contains("非凭据单值终点",error.Message);
    }

    /// <summary>原多值名称可以沿字典固定联接读取，文本父级与终点名称各自通过实际发布证明。</summary>
    [Theory]
    [InlineData(0,"string","OnlyNew","list",true)]
    [InlineData(1,"string","OverWriteByFirstColumn","edit",true)]
    [InlineData(0,"int64","OverWriteByFirstColumn","edit",false)]
    [InlineData(1,"int64","OnlyNew","list",true)]
    [InlineData(0,"date","OverWriteByFirstColumn","edit",true)]
    [InlineData(1,"date","OnlyNew","list",false)]
    [InlineData(0,"string","OnlyNew","list",true,true)]
    [InlineData(1,"string","OverWriteByFirstColumn","edit",true,true)]
    [InlineData(1,"string","OnlyNew","list",true,true,false)]
    [InlineData(0,"string","OverWriteByFirstColumn","edit",true,true,true,true)]
    [InlineData(1,"string","OnlyNew","list",true,true,true,true)]
    public Task Multi_text_parent_reads_names_from_fixed_join(int sourceStorage,string kind,string import,string export,bool buildPackage,
        bool joinedPageCondition=false,bool separateConditionPath=true,bool physicalLabel=false) =>
        VerifyMultiJoinedPage(sourceStorage,kind,import,export,buildPackage,joinedPageCondition,separateConditionPath,physicalLabel);

    /// <summary>同项目多值字典的固定联接与物理父范围分别证明，也支持无父字典。</summary>
    [Theory]
    [InlineData(0,"int64","OnlyNew","list")]
    [InlineData(1,"string","OverWriteByFirstColumn","edit")]
    [InlineData(0,"none","OverWriteByFirstColumn","edit")]
    [InlineData(1,"none","OnlyNew","list")]
    public Task Multi_physical_or_unparented_dictionary_keeps_joined_page_scope(
        int sourceStorage,string parentType,string import,string export) =>
        VerifyMultiJoinedPage(sourceStorage,"string",import,export,buildPackage:true,joinedPageCondition:true,
            separateConditionPath:true,physicalLabel:true,parentType:parentType);

    /// <summary>整数级联父键使用实际业务键，联接条件继续沿发布字典求值。</summary>
    [Theory]
    [InlineData(0,"OnlyNew","list")]
    [InlineData(1,"OverWriteByFirstColumn","edit")]
    public Task Multi_integer_logical_parent_keeps_joined_dictionary_scope(int sourceStorage,string import,string export) =>
        VerifyMultiJoinedPage(sourceStorage,"string",import,export,buildPackage:true,joinedPageCondition:true,
            separateConditionPath:true,physicalLabel:true,parentType:"logical-int");

    /// <summary>两个独立级联层位共用实际 bigint 国家键，字典须同时保留两个范围和固定联接。</summary>
    [Theory]
    [InlineData(0,"OnlyNew","list")]
    [InlineData(1,"OverWriteByFirstColumn","edit")]
    public Task Multi_compound_logical_parent_keeps_joined_dictionary_scope(
        int sourceStorage,string import,string export)=>
        VerifyMultiJoinedPage(sourceStorage,"string",import,export,buildPackage:true,joinedPageCondition:true,
            separateConditionPath:true,physicalLabel:true,parentType:"logical-int",compoundParent:true);

    /// <summary>同一个文本业务父键同时限定两个独立级联路径，原候选、Excel 和名称回填共用该范围。</summary>
    [Theory]
    [InlineData(0,"OnlyNew","list")]
    [InlineData(1,"OverWriteByFirstColumn","edit")]
    public Task Multi_compound_text_parent_keeps_joined_dictionary_scope(
        int sourceStorage,string import,string export)=>
        VerifyMultiJoinedPage(sourceStorage,"string",import,export,buildPackage:true,joinedPageCondition:true,
            separateConditionPath:true,physicalLabel:true,parentType:"logical",compoundParent:true);

    /// <summary>同一父表的另一个唯一文本业务键不能冒充已发布的共同父键。</summary>
    [Fact]
    public Task Multi_compound_text_parent_rejects_different_business_keys()=>
        VerifyMultiJoinedPage(0,"string","OnlyNew","list",buildPackage:false,joinedPageCondition:true,
            separateConditionPath:true,physicalLabel:true,compoundParent:true,differentTextKeys:true);

    private static async Task VerifyMultiJoinedPage(int sourceStorage,string kind,string import,string export,bool buildPackage,
        bool joinedPageCondition,bool separateConditionPath,bool physicalLabel,string parentType="logical",bool compoundParent=false,
        bool differentTextKeys=false)
    {
        var catalog=Catalog(sourceStorage,import,export);
        PublishTextParentJoinedLabels(catalog,kind,["83","84"]);
        var logicalParent=parentType is "logical" or "logical-int";
        if(parentType=="logical-int")
        {
            // 两端使用同一个有唯一索引的 bigint 业务键，目标字段继续由稀疏级联恢复。
            foreach(var column in catalog.ConfigColumns.Where(column=>column.ColumnName=="business_key"||column.Id=="45"||
                column.ConfigTableId is "2" or "83" or "84"&&column.ColumnName is "country_id" or "place_id"))
            {
                column.LogicalType="int64";
                column.ColumnLength="";
            }
        }
        if(differentTextKeys)
        {
            catalog.ConfigColumns.Add(new(){Id="business_secondary",ConfigTableId="3",ColumnName="secondary_key",
                LogicalType="string",ColumnLength="50",IsDbNotNull=1});
            var projectId=catalog.ConfigColumns.Single(column=>column.ConfigTableId=="3"&&column.ColumnName=="yz_project_id").Id;
            catalog.ConfigIndexes.Add(JsonSerializer.SerializeToElement(new{id="different_key",configTableId="3",isUnique=1,
                indexName="uniq_project_secondary"}));
            catalog.ConfigIndexColumns.Add(JsonSerializer.SerializeToElement(new{id="different_key_project",configIndexId="different_key",
                configColumnId=projectId,sortNo=1}));
            catalog.ConfigIndexColumns.Add(JsonSerializer.SerializeToElement(new{id="different_key_column",configIndexId="different_key",
                configColumnId="business_secondary",sortNo=2}));
        }
        if(compoundParent)
        {
            // 字典另用一条稀疏链指向同一父表，保留各自路径的业务键类型。
            foreach(var id in new[]{"83","84"})
            {
                var leaves=catalog.ConfigColumns.Where(column=>column.ConfigTableId==id&&
                    column.ColumnName is "country_id" or "area_id" or "place_id").ToArray();
                var detail=System.Text.Json.Nodes.JsonNode.Parse(leaves.Single(column=>column.ControlKind=="CascadeTree").ControlDetail)!;
                if(differentTextKeys)detail["levels"]![0]!["idField"]="secondary_key";
                foreach(var level in detail["levels"]!.AsArray())
                    level!["columnPrefix"]="other_"+level["columnPrefix"]!.GetValue<string>();
                foreach(var column in leaves)
                    catalog.ConfigColumns.Add(new(){Id=column.Id+"2",ConfigTableId=id,YzProjectId=catalog.YzProjectId,
                        ColumnName="other_"+column.ColumnName,LogicalType=column.LogicalType,ColumnLength=column.ColumnLength,
                        LinkConfigTableId=column.LinkConfigTableId,ControlKind=column.ControlKind,
                        ControlDetail=column.ControlKind=="CascadeTree"?detail.ToJsonString():""});
                var pageId=id+"0";
                catalog.ConfigPages.Single(page=>page.Id==pageId).WhereSql+=" AND other_country_id=@parentId";
                catalog.ConfigPageColumns.Add(new(){Id="f"+id+"062",ConfigPageId=pageId,ConfigColumnId=id+"062",
                    YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=1});
            }
        }
        if(!logicalParent)
        {
            // 去掉文本逻辑父用例附带的级联，单独检验这组发布联接和物理父范围。
            var unrelated=catalog.ConfigColumns.Where(column=>column.ConfigTableId is "2" or "83" or "84"&&
                column.ColumnName is "country_id" or "area_id" or "place_id").Select(column=>column.Id).ToHashSet(StringComparer.Ordinal);
            catalog.ConfigColumns.RemoveAll(column=>unrelated.Contains(column.Id));
            catalog.ConfigPageColumns.RemoveAll(field=>unrelated.Contains(field.ConfigColumnId));
            // 首列覆盖使用原业务名称，不将多选／复选数组作为匹配键。
            if(import=="OverWriteByFirstColumn")
                catalog.ConfigColumns.Single(column=>column.Id=="22").SortNo=-1;
            const string projectScope="yz_project_id=@current_yz_project_id";
            foreach(var (table,pageId) in new[]{("2","201"),("83","830"),("84","840")})
            {
                var published=catalog.ConfigPages.Single(item=>item.Id==pageId);
                published.WhereSql=projectScope;
                if(table=="84"&&parentType!="int64")published.Extra["type"]=JsonSerializer.SerializeToElement(2);
                if(parentType=="none")continue;
                catalog.ConfigColumns.Add(new(){Id=table+"15",ConfigTableId=table,YzProjectId=catalog.YzProjectId,
                    ColumnName="owner_country_id",LogicalType=parentType,LinkConfigTableId="3"});
                published.WhereSql+=" AND owner_country_id=@parent_id";
            }
        }
        if(joinedPageCondition)
        {
            // 固定条件另用联接字段，不依赖关系显示名恰好已参与投影。
            var project=catalog.YzProjectId;
            foreach(var (id,limit) in new[]{("83","多选停用"),("84","复选停用")})
            {
                var gate=separateConditionPath?"gate_item":"label_item";
                catalog.ConfigColumns.Add(new(){Id=id+"10",ConfigTableId=id,YzProjectId=project,ColumnName="region_label",
                    LogicalType="string",Extra={["columnFullName"]=JsonSerializer.SerializeToElement(gate+".name"),
                        ["isJoinColumn"]=JsonSerializer.SerializeToElement(1)}});
                if(separateConditionPath)
                    catalog.ConfigColumns.Add(new(){Id=id+"11",ConfigTableId=id,YzProjectId=project,ColumnName="gate_item_id",
                        LogicalType="int64",ControlKind="List",LinkConfigTableId="86",
                        ControlDetail="""{"tableId":"86","pageId":"860","nameField":"name"}""",
                        Extra={["linkConfigPageId"]=JsonSerializer.SerializeToElement("860")}});
                catalog.ConfigPages.Single(page=>page.ConfigTableId==id).WhereSql+=$" AND region_label <> '{limit}'";
                if(physicalLabel)
                {
                    var label=catalog.ConfigColumns.Single(column=>column.Id==id+"02");
                    label.Extra.Remove("isJoinColumn");
                    label.Extra.Remove("columnFullName");
                }
            }
            catalog.ConfigColumns.Add(new(){Id="8606",ConfigTableId="86",YzProjectId=project,ColumnName="store_id",LogicalType="int64"});
            catalog.ConfigPages.Single(page=>page.Id=="860").WhereSql+=" AND store_id=@current_store_id AND name <> '字典停用'";
            if(sourceStorage==1)
            {
                catalog.ConfigColumns.Add(new(){Id="8607",ConfigTableId="2",YzProjectId=project,ColumnName="store_id",LogicalType="int64"});
                catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql+=" AND store_id=@current_store_id";
            }
        }
        if(differentTextKeys)
        {
            var target=catalog.ConfigTables.Single(table=>table.Id=="83");
            var targetPage=catalog.ConfigPages.Single(page=>page.Id=="830");
            Assert.Empty(ProjectScopedSingleRelationTarget.JoinedPageFields(catalog,target,targetPage));
            Assert.Throws<InvalidOperationException>(()=>GenerationModel.Create(catalog,"Example.JoinedTextParentLabels"));
            return;
        }
        var model=GenerationModel.Create(catalog,"Example.JoinedTextParentLabels");
        var owner=model.Tables.Single(table=>table.Source.Id=="2");
        var page=Assert.Single(owner.Pages);
        var multi=MultiSelectCompilation.Resolve(model,owner,page,owner.Columns.Single(column=>column.Source.Id=="704"));
        var checkbox=DatabaseCheckboxCompilation.Resolve(model,owner,page,owner.Columns.Single(column=>column.Source.Id=="706"));
        Assert.True(multi.Definition is not null,multi.Reason);
        Assert.True(checkbox.Definition is {DerivedName:true},checkbox.Reason);
        if(parentType=="logical")
        {
            Assert.True(multi.Definition is {SameParent:true,SharedTextParent:not null},multi.Reason);
            Assert.NotNull(checkbox.Definition.SharedTextParent);
        }
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        if(sourceStorage==1)
        {
            var maintenance=MixedRelationPropagationCompilation.ForModel(model);
            Assert.Contains("textrel_name_dictionary",maintenance.MixedTargets);
            Assert.Contains("multiitem_names",maintenance.MixedSourceColumns["textrel_source"]);
            Assert.Contains("checkitem_names",maintenance.MixedSourceColumns["textrel_source"]);
            if(joinedPageCondition)
            {
                var definitions=RelationPropagationCompilation.Definitions(RelationPropagationCompilation.DerivedBindings(model)
                    .Where(binding=>binding.Source.Source.Id=="2"&&binding.Multiple).ToArray());
                var published=RelationPropagationCompilation.PublishedMaintenanceMetadata(model,definitions);
                var compiler=typeof(Metarill.Data.Queries.MySqlNonRedundantReadCompiler);
                var compiled=compiler.GetMethod("CompileRefresh",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(null,[published,"2",new[]{"multiitem_names","checkitem_names"},definitions,null])!;
                const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var constants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                    compiled.GetType().GetProperty("Constants",hidden)!.GetValue(compiled));
                Assert.Contains("多选停用",constants.Values);
                Assert.Contains("复选停用",constants.Values);
                Assert.Contains("字典停用",constants.Values);
                if(parentType=="logical-int")
                {
                    var reverse=Assert.IsAssignableFrom<IEnumerable<object>>(
                        compiled.GetType().GetProperty("Reverse",hidden)!.GetValue(compiled));
                    var parentEvents=reverse.Where(item=>Equals(item.GetType().GetProperty("TargetTable")!.GetValue(item),
                        "textrel_country")).ToArray();
                    Assert.NotEmpty(parentEvents);
                    Assert.All(parentEvents,item=>
                    {
                        var key=item.GetType().GetProperty("NodeKey",hidden)!.GetValue(item);
                        Assert.NotNull(key);
                        Assert.Equal("business_key",key.GetType().GetProperty("Column")!.GetValue(key));
                    });
                }
            }
        }
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-text-parent-joined-labels");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            string generated;
            if(buildPackage)
            {
                new ApplicationPackager().Create(catalog,directory,model.Namespace,NonRedundantApplicationFixture.Root());
                generated=Path.Combine(directory,"backend/generated");
                var published=await PublishedReadCatalog(directory);
                AssertNamePlan(Metarill.Data.Queries.NonRedundantReads.CompileScopedMultiLabel(published,"83","830","name",["id","name"],[]),"多选停用");
                AssertNamePlan(sourceStorage==0||compoundParent
                    ?Metarill.Data.Queries.NonRedundantReads.CompileScopedCheckboxLabel(published,"84","840","name",["id","name"],[],false)
                    :Metarill.Data.Queries.NonRedundantReads.CompileScopedMultiLabel(published,"84","840","name",["id","name"],[]),"复选停用");
            }
            else
            {
                new CSharpGenerator().Generate(catalog,directory,model.Namespace);
                generated=directory;
            }
            var service=await File.ReadAllTextAsync(Path.Combine(generated,"Services/TextrelSourceService.g.cs"));
            Assert.Contains("CompileScopedMultiLabel(\"83\",\"830\",\"name\"",service);
            if(compoundParent)
                Assert.Contains("CompileScopedCheckboxLabel(\"84\",\"840\",\"name\"",service);
            else if(parentType=="logical")
                Assert.Contains(sourceStorage==0?"CompileScopedCheckboxLabel(\"84\",\"840\",\"name\"":
                    "CompileScopedMultiLabel(\"84\",\"840\",\"name\"",service);
            else
                Assert.Matches("CompileScoped(?:Checkbox|Multi)Label\\(\"84\",\"840\",\"name\"",service);
            Assert.Contains("ImportMulti201_704Async",service);
            Assert.Contains("ImportCheckbox201_706Async",service);
            Assert.Contains("Export201Async",service);
            if(parentType=="logical-int")
            {
                Assert.Contains("row.BusinessKey == targetParentKey",service);
                Assert.DoesNotContain("row.Id == targetParentKey",service);
                var checkboxStart=service.IndexOf(" CheckboxScope201_706Async(",StringComparison.Ordinal);
                Assert.True(checkboxStart>=0);
                var checkboxEnd=service.IndexOf("return query;",checkboxStart,StringComparison.Ordinal);
                Assert.True(checkboxEnd>checkboxStart);
                var checkboxScope=service[checkboxStart..checkboxEnd];
                Assert.Contains("CrossReader201_25(actor,locking,false).PathAsync(0,",checkboxScope);
                Assert.DoesNotContain("row.Id==parentKey",checkboxScope);
            }
            if(buildPackage)
                foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }

        void AssertNamePlan(Metarill.Data.Queries.NonRedundantReadPlan plan,string label)
        {
            Assert.Equal(kind,plan.OutputTypes["name"]);
            if(joinedPageCondition)
            {
                Assert.True(plan.RequiresAccount);
                var actor=new Metarill.Core.Security.RuntimeActor(8,"联接筛选",false,catalog.YzProjectId,"5"){StoreId=29};
                Assert.Throws<UnauthorizedAccessException>(()=>plan.BindAccount(actor with{StoreId=null}));
                plan=plan.BindAccount(actor);
            }
            if(parentType=="logical")AssertTextParentSql(plan);
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var mysql=plan.GetType().GetProperty("MySql",flags)!.GetValue(plan)!;
            var from=Assert.IsType<string>(mysql.GetType().GetProperty("From",flags)!.GetValue(mysql));
            Assert.Contains("`textrel_name_dictionary`",from);
            var scope=Assert.IsType<string>(mysql.GetType().GetProperty("Scope",flags)!.GetValue(mysql));
            var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(mysql.GetType().GetProperty("Constants",flags)!.GetValue(mysql));
            if(logicalParent)
                Assert.Contains(parameters,parameter=>Equals(parameter.Value,"排除地区")&&scope.Contains(parameter.Key,StringComparison.Ordinal));
            if(parentType=="logical-int")
            {
                Assert.Contains("`business_key`",from);
                Assert.Contains("multi_parent.`business_key`=",scope);
                Assert.DoesNotContain("multi_parent.`id`=",scope);
                Assert.Equal("country_id",mysql.GetType().GetProperty("RequiredMultiParent",flags)!.GetValue(mysql));
            }
            if(!logicalParent)
                Assert.Equal(parentType=="none"?null:"owner_country_id",
                    mysql.GetType().GetProperty("RequiredMultiParent",flags)!.GetValue(mysql));
            if(compoundParent)
            {
                var expressions=Assert.IsAssignableFrom<IReadOnlyDictionary<string,string>>(
                    mysql.GetType().GetProperty("Expressions",flags)!.GetValue(mysql));
                var firstParent=expressions["country_id"];
                var secondParent=expressions["other_country_id"];
                Assert.NotEqual(firstParent,secondParent);
                Assert.Contains(firstParent,scope);
                Assert.Contains(secondParent,scope);
            }
            if(joinedPageCondition)
            {
                Assert.DoesNotContain("region_label",scope);
                Assert.Contains(parameters,parameter=>Equals(parameter.Value,label)&&scope.Contains(parameter.Key,StringComparison.Ordinal));
                Assert.Contains("字典停用",parameters.Values);
                Assert.Contains(29L,parameters.Values);
                Assert.Contains(separateConditionPath?"gate_item_id":"label_item_id",from);
                Assert.Contains("store_id",from+scope);
            }
        }
    }

    /// <summary>原多值候选及写入同时绑定文本父键和已发布字典的精确角色范围。</summary>
    [Theory]
    [InlineData(0,"OnlyNew","list")]
    [InlineData(0,"OverWriteByFirstColumn","edit")]
    [InlineData(1,"OnlyNew","list")]
    [InlineData(1,"OverWriteByFirstColumn","edit")]
    public Task Multi_text_parent_keeps_exact_dictionary_permission(int sourceStorage,string import,string export)=>
        VerifyTextParentExactDictionary(sourceStorage,0,import,export);

    /// <summary>字典保存物理父键及显示字段时，两个控件仍按发布页校验精确权限。</summary>
    [Theory]
    [InlineData(0,"OnlyNew","list")]
    [InlineData(0,"OverWriteByFirstColumn","edit")]
    [InlineData(1,"OnlyNew","list")]
    [InlineData(1,"OverWriteByFirstColumn","edit")]
    public Task Stored_dictionary_text_parent_keeps_exact_permission(int sourceStorage,string import,string export)=>
        VerifyTextParentExactDictionary(sourceStorage,1,import,export);

    private static async Task VerifyTextParentExactDictionary(int sourceStorage,int targetStorage,string import,string export)
    {
        var catalog=Catalog(sourceStorage,import,export);
        foreach(var id in new[]{"83","84"})
        {
            ScopeTarget(catalog,id);
            catalog.ConfigTables.Single(table=>table.Id==id).IsRongyu=targetStorage;
            catalog.ConfigColumns.Add(new(){Id=id+"09",ConfigTableId=id,YzProjectId=catalog.YzProjectId,
                ColumnName="scope_code",LogicalType="string"});
            var target=catalog.ConfigPages.Single(page=>page.ConfigTableId==id);
            target.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("scope_code");
            target.WhereSql+=targetStorage==0?" AND area_name <> '排除地区'":
                id=="83"?" AND scope_code <> '排除多选地区'":" AND scope_code <> '排除复选地区'";
        }
        foreach(var admin in catalog.YzAdminTypes)
            admin.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("real_name");
        var model=GenerationModel.Create(catalog,"Example.TextParentExactScope");
        var owner=model.Tables.Single(table=>table.Source.Id=="2");
        var page=Assert.Single(owner.Pages);
        var multi=MultiSelectCompilation.Resolve(model,owner,page,owner.Columns.Single(column=>column.Source.Id=="704"));
        var checkbox=DatabaseCheckboxCompilation.Resolve(model,owner,page,owner.Columns.Single(column=>column.Source.Id=="706"));
        Assert.True(multi.Definition is {SharedTextParent:not null,ExactTarget:not null},multi.Reason);
        Assert.True(checkbox.Definition is {SharedTextParent:not null,ExactTarget:not null},checkbox.Reason);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-text-parent-exact-scope");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new ApplicationPackager().Create(catalog,directory,model.Namespace,NonRedundantApplicationFixture.Root());
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/Services/TextrelSourceService.g.cs"));
            Assert.Contains("ResolveExactDataManage(\"830\", \"list\", \"real_name\")",service);
            Assert.Contains("ResolveExactDataManage(\"840\", \"list\", \"real_name\")",service);
            Assert.Contains("ImportMulti201_704Async",service);
            Assert.Contains("ImportCheckbox201_706Async",service);
            Assert.Contains("Export201Async",service);
            if(targetStorage==0)
            {
                Assert.Contains("\"scope_code\", FixedReadComparison.In",service);
                var published=await PublishedReadCatalog(directory);
                AssertTextParentSql(Metarill.Data.Queries.NonRedundantReads.CompileExactMultiLabel(published,"83","830","name",["id","name"],[]));
                AssertTextParentSql(Metarill.Data.Queries.NonRedundantReads.CompileScopedCheckboxLabel(published,"84","840","name",["id","name"],[],true));
            }
            else
            {
                Assert.Contains("ApplyTextParent(query,session,\"country_id\",targetParentId!,businessKey:true)",service);
                Assert.Contains("ApplyTextParent(query,session,\"country_id\",parentId!,businessKey:true)",service);
                Assert.Contains("multiExactScope.IsRequired) query = Metarill.Data.Queries.ExactDataManageSql.Apply(query, session, \"scope_code\"",service);
                Assert.Contains("multiImportExactScope.IsRequired) query = Metarill.Data.Queries.ExactDataManageSql.Apply(query, session, \"scope_code\"",service);
                Assert.Contains("checkboxExactScope.IsRequired) query = Metarill.Data.Queries.ExactDataManageSql.Apply(query, session, \"scope_code\"",service);
                VerifyStoredExactDictionaryNames(catalog,model,owner,page);
                if(sourceStorage==0)VerifyStoredExactDictionaryNames(await PublishedReadCatalog(directory),model,owner,page);
            }
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>来源名称和回填复用字典各自的文本父范围及物理条件，精简目录重建后仍然相同。</summary>
    private static void VerifyStoredExactDictionaryNames(MetadataCatalog metadata,GenerationModel model,TableModel owner,PageModel page)
    {
        string[] names=["multiitem_names","checkitem_names"];
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        object sql;
        if(owner.Source.IsRongyu==0)
        {
            var plan=Metarill.Data.Queries.NonRedundantReads.CompileProjectPage(metadata,"2",names,[],
                NonRedundantCrossTableCompilation.Bindings(model,owner,page),[],NonRedundantReadCompilation.ProjectMultiTargets(model,owner,page));
            sql=plan.GetType().GetProperty("MySql",flags)!.GetValue(plan)!;
        }
        else
        {
            var maintenance=MixedRelationPropagationCompilation.ForModel(model);
            Assert.Contains("textrel_multiitem",maintenance.MixedTargets);
            Assert.Contains("textrel_checkitem",maintenance.MixedTargets);
            var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(maintenance.GetType().GetProperty("Plans",flags)!.GetValue(maintenance));
            var source=plans["2"]!;
            Assert.Null(source.GetType().GetProperty("Error")!.GetValue(source));
            sql=source.GetType().GetProperty("Read")!.GetValue(source)!;
        }
        var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(sql.GetType().GetProperty("Constants",flags)!.GetValue(sql));
        foreach(var (name,excluded) in names.Zip(new[]{"排除多选地区","排除复选地区"}))
        {
            var expression=Assert.IsType<string>(sql.GetType().GetMethod("Expression",flags)!.Invoke(sql,[name]));
            Assert.Contains("BINARY CAST(multi_value.`country_id` AS CHAR)",expression);
            Assert.Contains("`business_key`",expression);
            Assert.DoesNotContain("multi_parent.",expression);
            Assert.Contains(parameters,parameter=>Equals(parameter.Value,excluded)&&expression.Contains(parameter.Key,StringComparison.Ordinal));
        }
    }

    /// <summary>页面覆盖只能读取字典页已公开的物理简称。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Sparse_checkbox_text_parent_rejects_unpublished_short_name(int targetStorage)
    {
        var catalog=Catalog(0,"OnlyNew","list");
        ScopeTarget(catalog,"84");
        catalog.ConfigTables.Single(item=>item.Id=="84").IsRongyu=targetStorage;
        PublishCheckboxShortName(catalog,storedCache:false);
        catalog.ConfigPageColumns.RemoveAll(field=>field.Id=="f8407");
        var model=GenerationModel.Create(catalog,"Example.SparseCheckboxHiddenShortName");
        var owner=model.Tables.Single(item=>item.Source.Id=="2");
        var result=DatabaseCheckboxCompilation.Resolve(model,owner,Assert.Single(owner.Pages),owner.Columns.Single(field=>field.Source.Id=="706"));
        Assert.Null(result.Definition);
        Assert.Contains("显式目标页必须已开放该字段",result.Reason);
    }

    /// <summary>字典页公开简称时，冗余来源仍显式声明独立物理副本。</summary>
    private static void PublishCheckboxShortName(MetadataCatalog catalog,bool storedCache)
    {
        catalog.ConfigColumns.Add(new(){Id="8407",ConfigTableId="84",YzProjectId=catalog.YzProjectId,ColumnName="short_name",LogicalType="string"});
        catalog.ConfigPageColumns.Add(new(){Id="f8407",ConfigPageId="840",ConfigColumnId="8407",YzProjectId=catalog.YzProjectId,IsShow=1});
        if(storedCache)
        {
            catalog.ConfigColumns.Add(new(){Id="708",ConfigTableId="2",YzProjectId=catalog.YzProjectId,ColumnName="checkitem_short_names",
                LogicalType="string",ColumnLength="max",Extra=new(){
                    ["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
                    ["columnFullName"]=JsonSerializer.SerializeToElement("checkitem.short_name")}});
            catalog.ConfigPageColumns.Add(new(){Id="f708",ConfigPageId="201",ConfigColumnId="708",YzProjectId=catalog.YzProjectId,IsShow=1});
        }
        catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="706").Extra["controlDetail"]=
            JsonSerializer.SerializeToElement("""{"nameField":"short_name"}""");
    }

    /// <summary>发布两端文本父键及同项目字典名称源，单值和多值用例共用。</summary>
    private static void PublishTextParentJoinedLabels(MetadataCatalog catalog,string kind,IReadOnlyList<string> dictionaryIds)
    {
        catalog.ConfigTables.Add(new(){Id="86",YzProjectId=catalog.YzProjectId,TableName="textrel_name_dictionary",IsRongyu=1});
        catalog.ConfigColumns.AddRange([
            new(){Id="8601",ConfigTableId="86",YzProjectId=catalog.YzProjectId,ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="8602",ConfigTableId="86",YzProjectId=catalog.YzProjectId,ColumnName="name",LogicalType=kind,ColumnLength=kind=="decimal"?"24,6":""},
            new(){Id="8603",ConfigTableId="86",YzProjectId=catalog.YzProjectId,ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="8604",ConfigTableId="86",YzProjectId=catalog.YzProjectId,ColumnName="delete_no",LogicalType="datetime"}
        ]);
        catalog.ConfigPages.Add(new(){Id="860",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",ConfigTableId="86",
            Name="名称字典",WhereSql="yz_project_id=@current_yz_project_id"});
        catalog.ConfigPageColumns.Add(new(){Id="f8602",YzProjectId=catalog.YzProjectId,ConfigPageId="860",ConfigColumnId="8602",IsShow=1});
        foreach(var id in dictionaryIds)
        {
            ScopeTarget(catalog,id);
            catalog.ConfigTables.Single(table=>table.Id==id).IsRongyu=0;
            catalog.ConfigPages.Single(page=>page.ConfigTableId==id).WhereSql+=" AND area_name <> '排除地区'";
            var label=catalog.ConfigColumns.Single(column=>column.Id==id+"02");
            label.LogicalType=kind;
            label.ColumnLength=kind=="decimal"?"24,6":"";
            label.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
            label.Extra["columnFullName"]=JsonSerializer.SerializeToElement("label_item.name");
            catalog.ConfigColumns.Add(new(){Id=id+"08",ConfigTableId=id,YzProjectId=catalog.YzProjectId,
                ColumnName="label_item_id",LogicalType="int64",ControlKind="List",LinkConfigTableId="86",
                ControlDetail="""{"tableId":"86","nameField":"name"}"""});
            catalog.ConfigPageColumns.Add(new(){Id="f"+id+"08",ConfigPageId=id+"0",ConfigColumnId=id+"08",YzProjectId=catalog.YzProjectId});
        }
    }

    /// <summary>使用生成应用实际装载的精简目录重放查询，包含页级范围与原控件绑定。</summary>
    internal static async Task<MetadataCatalog> PublishedReadCatalog(string directory,bool maintenanceOnly=false)
    {
        var registration=await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/GeneratedRegistration.g.cs"));
        var snapshot=maintenanceOnly
            ?System.Text.RegularExpressions.Regex.Match(registration,
                """class GeneratedRelationMaintenance[\s\S]*?MetadataCatalog>\(("(?:\\.|[^"\\])*"),options""")
            :System.Text.RegularExpressions.Regex.Match(registration,
                """MetadataCatalog>\(("(?:\\.|[^"\\])*"),new System\.Text\.Json""");
        Assert.True(snapshot.Success,"生成应用须内置实际读取或维护目录。");
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
