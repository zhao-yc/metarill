using System.Text.Json;
using Metarill.Core.Cascades;
using Metarill.Core.Contracts;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>完整路径Excel协议与实际生成编译，不使用ID或同名叶子捷径。</summary>
public sealed class CascadeTreeExcelTests
{
    /// <summary>多字符分隔符和数字名称按原格式反查，重复层级明确拒绝。</summary>
    [Fact]
    public async Task Full_path_resolves_unique_levels_and_numeric_names_without_id_fallback()
    {
        var nodes=new[]{new CascadeTreeNode(9007199254740993,"1234",null),new(2,"叶/区","9007199254740993"),new(3,"另一根",null),new(4,"叶/区","3")};
        Task<IReadOnlyList<CascadeTreeNode>> Find(string name,long? parent,CancellationToken ct)=>Task.FromResult<IReadOnlyList<CascadeTreeNode>>(nodes.Where(node=>node.Name==name&&node.ParentId==parent?.ToString()).ToArray());
        Assert.Equal(2,await CascadeTreeExcelPath.ResolveAsync("1234 > 叶/区"," > ",Find));Assert.Equal(4,await CascadeTreeExcelPath.ResolveAsync("另一根 > 叶/区"," > ",Find));
        await Assert.ThrowsAsync<PageImportException>(()=>CascadeTreeExcelPath.ResolveAsync("叶/区"," > ",Find));
        await Assert.ThrowsAsync<PageImportException>(()=>CascadeTreeExcelPath.ResolveAsync("1234"," > ",(_,_,_)=>Task.FromResult<IReadOnlyList<CascadeTreeNode>>([nodes[0],nodes[2]])));
        var path=new CascadeTreePath([new("9007199254740993","1234"),new("2","叶/区")],"不可信预计算值");Assert.Equal("1234 > 叶/区",CascadeTreeExcelPath.Format(path," > "));
        Assert.Throws<InvalidOperationException>(()=>CascadeTreeExcelPath.Format(path,"/"));Assert.Throws<InvalidOperationException>(()=>CascadeTreeExcelPath.Format(null,"/"));
        Assert.Throws<InvalidOperationException>(()=>CascadeTreeExcelPath.Format(new([new("1"," 空白 ")],""),"/"));
    }
    /// <summary>追加/覆盖均编译内部导入动作，普通写入继续检查目标list，不接受客户端动作选择。</summary>
    [Theory]
    [InlineData("string","string")]
    [InlineData("string","int64")]
    [InlineData("int64","string")]
    [InlineData("int64","int64")]
    public async Task Actual_generated_import_and_export_compile_for_all_id_types(string sourceType,string parentType)
    {
        var catalog=Catalog("excel",sourceType,parentType);var saved=JsonSerializer.Serialize(catalog);var boundary=Path.Combine(Path.GetTempPath(),"metarill-cascade-excel");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CascadeExcel");var source=File.ReadAllText(Path.Combine(directory,"generated/Services/ExcelSourceService.g.cs"));Assert.Contains("ImportCascade201_25Async",source);Assert.Contains("ExcelCascadeTreeId(entity)",source);Assert.Contains("cascadeOperation != RecordMutationOperation.Import",source);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType("Example.CascadeExcel.Generated.Controllers.ExcelSourcePage201Controller"));Assert.Equal(saved,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>原自树允许使用已声明的唯一业务键，业务行、候选和 XLSX 全部保留该键。</summary>
    [Theory]
    [InlineData(0,"int64",false)]
    [InlineData(1,"string",false)]
    [InlineData(0,"string",true)]
    [InlineData(1,"string",true)]
    public async Task Ordinary_self_tree_uses_its_unique_business_node_key(int targetMode,string sourceType,bool textKey)
    {
        const string ns="Example.TreeBusinessKey";
        var catalog=RedundantMultiNameCacheTests.Published(Catalog("businesskey",sourceType,textKey?"string":"int64"));
        catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=0;
        catalog.ConfigTables.Single(table=>table.Id=="3").IsRongyu=targetMode;
        foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="2"))page.Extra.Remove("type");
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigColumnId=="26"))
            field.Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        var selection=catalog.ConfigColumns.Single(column=>column.Id=="25");
        selection.ControlDetail=selection.ControlDetail.Replace("\"idField\":\"id\"","\"idField\":\"node_key\"",StringComparison.Ordinal);
        catalog.ConfigColumns.Add(new(){Id="38",ConfigTableId="3",YzProjectId=catalog.YzProjectId,
            ColumnName="node_key",LogicalType=textKey?"string":"int64",ColumnLength="48",IsDbNotNull=1});
        catalog.ConfigIndexes.Add(JsonSerializer.SerializeToElement(new{id="71",configTableId="3",indexName="uq_tree_node_key",
            isUnique=1,isEnabled=1,yzProjectId=catalog.YzProjectId}));
        if(targetMode==0)catalog.ConfigIndexColumns.Add(JsonSerializer.SerializeToElement(new{id="711",configIndexId="71",
            configColumnId="33",sortNo=10,yzProjectId=catalog.YzProjectId}));
        catalog.ConfigIndexColumns.Add(JsonSerializer.SerializeToElement(new{id="712",configIndexId="71",configColumnId="38",
            sortNo=20,yzProjectId=catalog.YzProjectId}));

        var model=GenerationModel.Create(catalog,ns);
        var owner=model.Tables.Single(table=>table.Source.Id=="2");
        var pageModel=owner.Pages.Single(page=>page.Source.Id=="201");
        var read=CascadeTreeReadCompilation.Resolve(model,owner,pageModel,owner.Columns.Single(column=>column.Source.Id=="25"));
        Assert.True(read.Definition is not null,read.Reason);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-self-tree-business-key");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/BusinesskeySourceService.g.cs"));
            if(textKey)
            {
                Assert.Contains("TextCascadeTreeSql.Match",service);
                Assert.Contains(".Where(row=>row.Id>after)",service);
                Assert.Contains("FixedReadFilter.ForTextTree(\"tree_id\",ids)",service);
                Assert.Contains("CascadeTreeExcelPath.ResolveTextAsync",service);
            }
            else
            {
                Assert.Contains("Where(row=>row.NodeKey==id)",service);
                Assert.Contains("row.NodeKey>after",service);
                Assert.Contains("keys.Contains(row.NodeKey!.Value)",service);
            }
            Assert.Contains("await CascadeReader201_25(actor,true,false,requireTargetList).PathAsync",service);
            Assert.Contains("ImportCascade201_25Async",service);
            Assert.Contains("ImportCascade202_25Async",service);
            Assert.Contains("ExcelCascadeTreeId(entity)",service);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var serviceType=assembly.GetType(ns+".Generated.Services.BusinesskeySourceService",true)!;
            foreach(var member in new[]{"ReadPlan201","ExportReadPlan201"})
            {
                var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                    serviceType.GetField(member,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null));
                var mysql=plan.GetType().GetProperty("MySql",hidden)!.GetValue(plan)!;
                var leaf=Assert.IsType<string>(mysql.GetType().GetMethod("Expression",hidden)!.Invoke(mysql,["tree_name"]));
                Assert.Contains("`node_key`",leaf);
                if(textKey)Assert.Contains("REGEXP '^[[:space:]]*$'",leaf);
            }
            var app=Path.Combine(directory,"app");
            Assert.True(new ApplicationPackager().Create(catalog,app,ns,NonRedundantApplicationFixture.Root()).FrontendIncluded);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(app,operation);
        }
        finally { if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary); }

        catalog.ConfigIndexes.Clear();
        var missing=GenerationModel.Create(catalog,ns);
        var table=missing.Tables.Single(item=>item.Source.Id=="2");
        var unproven=CascadeTreeReadCompilation.Resolve(missing,table,table.Pages.Single(item=>item.Source.Id=="201"),
            table.Columns.Single(item=>item.Source.Id=="25"));
        Assert.Null(unproven.Definition);
    }

    /// <summary>R1整包必须下发固定字典条件、写前业务键和可加载的维护目录。</summary>
    [Theory]
    [InlineData(0,"int64",false)]
    [InlineData(1,"string",false)]
    [InlineData(0,"string",true)]
    [InlineData(1,"string",true)]
    public async Task Redundant_self_tree_business_key_builds_with_maintenance_and_old_key_snapshot(int targetMode,string sourceType,bool textKey)
    {
        const string ns="Example.RedundantTreeBusinessKey";
        var catalog=RedundantMultiNameCacheTests.Published(Catalog("cachekey",sourceType,textKey?"string":"int64"));
        catalog.ConfigTables.Single(table=>table.Id=="3").IsRongyu=targetMode;
        foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="2"))page.Extra.Remove("type");
        var targetPage=catalog.ConfigPages.Single(page=>page.Id=="301");
        targetPage.IsSave=1;
        targetPage.IsDelete=1;
        catalog.ConfigColumns.Single(column=>column.Id=="25").ControlDetail=
            catalog.ConfigColumns.Single(column=>column.Id=="25").ControlDetail.Replace("\"idField\":\"id\"","\"idField\":\"node_key\"",StringComparison.Ordinal);
        catalog.ConfigColumns.Add(new(){Id="38",ConfigTableId="3",YzProjectId=catalog.YzProjectId,
            ColumnName="node_key",LogicalType=textKey?"string":"int64",ColumnLength="48",IsDbNotNull=1});
        catalog.ConfigPageColumns.Add(new(){Id="30138",ConfigPageId="301",ConfigColumnId="38",IsEdit=1});
        catalog.ConfigIndexes.Add(JsonSerializer.SerializeToElement(new{id="71",configTableId="3",indexName="uq_tree_node_key",
            isUnique=1,isEnabled=1,yzProjectId=catalog.YzProjectId}));
        if(targetMode==0)catalog.ConfigIndexColumns.Add(JsonSerializer.SerializeToElement(new{id="711",configIndexId="71",
            configColumnId="33",sortNo=10,yzProjectId=catalog.YzProjectId}));
        catalog.ConfigIndexColumns.Add(JsonSerializer.SerializeToElement(new{id="712",configIndexId="71",configColumnId="38",
            sortNo=20,yzProjectId=catalog.YzProjectId}));

        var model=GenerationModel.Create(catalog,ns);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cache-business-key");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,generated,ns);
            var service=await File.ReadAllTextAsync(Path.Combine(generated,"Services/CachekeyTreeService.g.cs"));
            Assert.Contains("[\"node_key\"] = entity.NodeKey",service);
            Assert.Contains("previousRelationKeys: previousRelationKeys",service);
            var consumer=await File.ReadAllTextAsync(Path.Combine(generated,"Services/CachekeySourceService.g.cs"));
            Assert.Contains("ImportCascade201_25Async",consumer);
            Assert.Contains("ImportCascade202_25Async",consumer);
            Assert.Contains("ExcelCascadeTreeId(entity)",consumer);
            Assert.DoesNotContain("previousRelationKeys",consumer);
            if(textKey)Assert.Contains("query=TextCascadeTreeSql.Match(query,session,\"tree_id\"",consumer);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var declaration=assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!;
            var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(declaration.GetProperty("Catalog")!.GetValue(null));
            var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(maintenance.GetType().GetProperty("Plans",hidden)!.GetValue(maintenance));
            var sourcePlan=plans["2"] ?? throw new InvalidOperationException("生成目录缺少来源表维护计划。");
            Assert.Null(sourcePlan.GetType().GetProperty("Error")!.GetValue(sourcePlan));
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>原字节键贯通父对象、候选、后代、回显和 Excel，前导零、字母大小写及空格保留。</summary>
    [Fact]
    public async Task Text_key_cascade_round_trips_original_tokens()
    {
        var nodes=new TextCascadeTreeNode[]
        {
            new("001",10,"根一"," \t"),new("1",20,"根二",null),new("Ab ",30,"子","001"),new("ab ",40,"子","1")
        };
        var reader=new TextSelfTreeReader(
            (parent,_)=>Task.FromResult<IReadOnlyList<TextCascadeTreeNode>>(nodes.Where(node=>
                (string.IsNullOrWhiteSpace(node.ParentKey)?null:node.ParentKey)==parent).ToArray()),
            (keys,_)=>Task.FromResult<IReadOnlySet<string>>(nodes.Select(node=>node.ParentKey).OfType<string>().Where(keys.Contains).ToHashSet()),
            (keys,_)=>Task.FromResult<IReadOnlyList<TextCascadeTreeNode>>(nodes.Where(node=>keys.Contains(node.Key)).ToArray()),
            (keys,after,take,_)=>Task.FromResult<IReadOnlyList<TextCascadeTreeNode>>(nodes.Where(node=>
                node.Cursor>after&&node.ParentKey is {} parent&&keys.Contains(parent)).Take(take).ToArray()),"/");
        Assert.Equal("001",new CascadeTreeOptionsRequest{SelectedPath=JsonSerializer.SerializeToElement(new{parentId="001"})}.ParseTextParent());
        Assert.Equal("Ab ",TextSelfTreeReader.FilterId(JsonSerializer.SerializeToElement(new{treeId="Ab "}),"treeId","treeName"));
        Assert.Equal("1e2",JsonSerializer.Deserialize<CascadeTreePathRequest>("""{"LeafId":1e2}""")!.ParseTextLeaf());
        Assert.Equal(new[]{"001","1"},(await reader.OptionsAsync(null)).Select(option=>option.Id));
        Assert.Equal("Ab ",Assert.Single(await reader.OptionsAsync("001")).Id);
        Assert.Equal(new[]{"001","Ab "},await reader.FilterIdsAsync("001",false));
        Assert.Equal(new[]{"1","ab "},await reader.FilterIdsAsync("1",false));
        var path=await reader.PathAsync("Ab ");
        Assert.Equal("根一/子",CascadeTreeExcelPath.Format(path,"/"));
        Assert.Equal("Ab ",await CascadeTreeExcelPath.ResolveTextAsync("根一/子","/",(name,parent,_)=>
            Task.FromResult<IReadOnlyList<TextCascadeTreeNode>>(nodes.Where(node=>node.Name==name&&
                (string.IsNullOrWhiteSpace(node.ParentKey)?null:node.ParentKey)==parent).ToArray())));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.PathAsync("Ab"));
    }

    /// <summary>导入页无普通新增/编辑入口；覆盖按明确第一物理业务字段，不改变原页面协议。</summary>
    internal static Metarill.Core.Metadata.MetadataCatalog Catalog(string prefix="excel",string sourceType="string",string parentType="int64")
    {
        var catalog=CascadeTreeViewCompilationTests.Catalog(prefix,sourceType,parentType);
        foreach(var column in catalog.ConfigColumns){column.Name=column.ColumnName;if(column.ConfigTableId=="2")column.SortNo=column.Id switch{"22"=>1,"25"=>2,"27"=>3,_=>0};}
        catalog.ConfigColumns.Single(column=>column.Id=="22").Name="记录名";catalog.ConfigColumns.Single(column=>column.Id=="25").Name="树路径";catalog.ConfigColumns.Single(column=>column.Id=="27").Name="金额";
        var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.IsImport=1;page.IsNew=0;page.IsSave=0;
        var overwrite=JsonSerializer.Deserialize<Metarill.Core.Metadata.PageMetadata>(JsonSerializer.Serialize(page))!;overwrite.Id="202";overwrite.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");catalog.ConfigPages.Add(overwrite);
        foreach(var old in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201").ToArray()){var field=JsonSerializer.Deserialize<Metarill.Core.Metadata.PageColumnMetadata>(JsonSerializer.Serialize(old))!;field.Id="202"+old.Id;field.ConfigPageId="202";catalog.ConfigPageColumns.Add(field);}return catalog;
    }
}
