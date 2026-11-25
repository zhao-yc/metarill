using System.Collections;
using System.Data.Common;
using Metarill.Core.Exports;
using Metarill.Core.Cascades;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using System.Text.RegularExpressions;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>真实生成/严格编译八种存储键组合；R1实际解析只替代ADO/ORM传输，不冒充实库或公开完整包验收。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CrossTableExcelLosslessGenerationTests
{
    /// <summary>八组合实际编译；R1执行解析和导出，R0不假装执行SQL。</summary>
    [Theory]
    [InlineData("string","string",1)][InlineData("string","int64",1)][InlineData("int64","string",1)][InlineData("int64","int64",1)]
    [InlineData("string","string",0)][InlineData("string","int64",0)][InlineData("int64","string",0)][InlineData("int64","int64",0)]
    public async Task Real_generated_import_export_compile_and_R1_parser_restores_all_levels(string sourceType,string parentType,int redundant)
    {
        var catalog=CrossTableExcelImportColumnsTests.Catalog("pathexcel",sourceType);
        foreach(var column in catalog.ConfigColumns.Where(column=>column.Id is "45" or "55"))column.LogicalType=parentType;
        catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=redundant;
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").IsShow=1;
        catalog.ConfigColumns.Add(new(){Id="28",ConfigTableId="2",ColumnName="country_id",Name="祖先ID",LogicalType=sourceType,LinkConfigTableId="3"});
        catalog.ConfigPageColumns.Add(new(){Id="20128",ConfigPageId="201",ConfigColumnId="28",IsShow=0,IsEdit=1,SortNo=30});
        if(redundant==1)
        {
            var scoped=JsonSerializer.Deserialize<Metarill.Core.Metadata.PageMetadata>(JsonSerializer.Serialize(catalog.ConfigPages.Single(page=>page.Id=="201")))!;
            scoped.Id="202";scoped.Extra.Remove("type");scoped.WhereSql+=" AND country_id=@parent_id";catalog.ConfigPages.Add(scoped);
            foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201").ToArray())
            {var copy=JsonSerializer.Deserialize<Metarill.Core.Metadata.PageColumnMetadata>(JsonSerializer.Serialize(field))!;copy.Id="202"+field.Id;copy.ConfigPageId="202";catalog.ConfigPageColumns.Add(copy);}
            RedundantMultiNameCacheTests.Published(catalog);
        }
        var before=JsonSerializer.Serialize(catalog);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-excel-lossless");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generator=new CSharpGenerator();generator.Generate(catalog,Path.Combine(directory,"generated"),"Example.PathExcel");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            Assert.NotNull(assembly.GetType("Example.PathExcel.Generated.Controllers.PathexcelSourcePage201Controller"));Assert.Equal(before,JsonSerializer.Serialize(catalog));
            if(redundant==1)await ExerciseParser(assembly);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>原页面与 Excel 引用业务键，即使根、区、叶的物理主键完全不同也保持路径。</summary>
    [Fact]
    public async Task Integer_business_keys_reach_original_excel_and_parent_scope()
    {
        var catalog=CrossTableExcelImportColumnsTests.Catalog("pathexcel","string");
        var source=catalog.ConfigColumns.Single(column=>column.Id=="25");
        AddBusinessKeys(catalog,source);
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId==source.Id).IsShow=1;
        var scope = JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(catalog.ConfigPages.Single(page=>page.Id=="201")))!;
        scope.Id="202";
        scope.Extra.Remove("type");
        scope.WhereSql+=" AND country_id=@parent_id";
        catalog.ConfigPages.Add(scope);
        catalog.ConfigColumns.Add(new(){Id="28",ConfigTableId="2",ColumnName="country_id",Name="祖先ID",LogicalType="string",LinkConfigTableId="3"});
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201").ToArray())
        {
            var copy = JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;
            copy.Id="202"+field.Id;
            copy.ConfigPageId="202";
            catalog.ConfigPageColumns.Add(copy);
        }
        foreach(var pageId in new[]{"201","202"})
            catalog.ConfigPageColumns.Add(new(){Id=pageId+"28",ConfigPageId=pageId,ConfigColumnId="28",IsShow=0,IsEdit=1,SortNo=30});
        RedundantMultiNameCacheTests.Published(catalog);
        var model=GenerationModel.Create(catalog,"Example.PathExcel");
        var generated = model.Tables.Single(table=>table.Source.Id=="2");
        var scoped = generated.Pages.Single(page=>page.Source.Id=="202");
        Assert.Contains("business_key",CrossTableReadCompilation.ForPage(model,generated,scoped).Single().Levels.Select(level=>level.KeyColumn));
        var published=GenerationMetadataPublisher.ForRelations(catalog);
        Assert.Equal(3,published.ConfigIndexes.Count);
        var bindings=RelationPropagationCompilation.Bindings(model);
        Assert.Equal(3,bindings.Count);
        _=Metarill.Data.Relations.RelationMutationPlan.Compile(published,RelationPropagationCompilation.Definitions(bindings));
        Assert.All(bindings,binding=>Assert.Equal("business_key",binding.TargetKey));
        Assert.Equal("business_key",Metarill.Data.Queries.RedundantSharedParents.Compile(catalog,"2","202")["28"].Levels[0].KeyColumn);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-business-key");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);
            await ExerciseParser(await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory), businessKeys:true);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>非冗余行沿业务键还原稀疏层，混用主键时各自匹配，源父及 Excel 使用同一发布定义。</summary>
    [Fact]
    public async Task Integer_business_keys_reach_nonredundant_read_and_excel()
    {
        var catalog=CrossTableExcelPublicationTests.ParentScopedCatalog("country_id","OverWriteByFirstColumn","edit");
        var control=catalog.ConfigColumns.Single(column=>column.Id=="25");
        AddBusinessKeys(catalog,control,primaryLevel:1);
        catalog.ConfigTables.Single(table=>table.Id=="3").IsRongyu=0;
        const string ns="Example.NonredundantCrossBusinessKey";
        var model=GenerationModel.Create(catalog,ns);
        var source=model.Tables.Single(table=>table.Source.Id=="2");
        var page=Assert.Single(source.Pages);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        var bindings=NonRedundantCrossTableCompilation.Bindings(model,source,page);
        var sqlPlan=Metarill.Data.Queries.MySqlNonRedundantReadCompiler.CompileWithCrossTables(catalog,"2",
            ["country_id","country_name","area_id","place_id"],bindings);
        var from=Assert.IsType<string>(sqlPlan.GetType().GetProperty("From",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(sqlPlan));
        Assert.Contains("cross0.`business_key` AS CHAR",from);
        Assert.Contains("cross1.`id` AS CHAR",from);
        Assert.Contains("cross2.`business_key` AS CHAR",from);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-business-key-r0");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>没有按项目证明的唯一索引时拒绝整条跨表路径，不能回退到目标主键。</summary>
    [Fact]
    public void Integer_business_keys_require_a_unique_index()
    {
        var catalog=CrossTableReadTests.Catalog();
        AddBusinessKeys(catalog,catalog.ConfigColumns.Single(column=>column.Id=="25"));
        catalog.ConfigIndexes.Clear();
        var model=GenerationModel.Create(catalog,"Example.InvalidCrossBusinessKey");
        var table=model.Tables.Single(table=>table.Source.Id=="2");
        var result=CrossTableReadCompilation.Resolve(model,table,Assert.Single(table.Pages),table.Columns.Single(column=>column.Source.Id=="25"));
        Assert.Null(result.Definition);
        Assert.Contains("唯一索引",result.Reason);
    }

    /// <summary>逐层使用各自目标表的项目联合唯一键，编译时仍允许同路径混用主键。</summary>
    internal static void AddBusinessKeys(MetadataCatalog catalog, ColumnMetadata control, int? primaryLevel=null)
    {
        var detail=JsonNode.Parse(control.ControlDetail)!;
        foreach(var (level,position) in detail["levels"]!.AsArray().Select((item,index)=>(item!,index)))
        {
            if(position==primaryLevel)continue;
            level["idField"]="business_key";
            var target=catalog.ConfigTables.Single(table=>table.TableName==level["table"]!.GetValue<string>());
            var columnId="business_"+target.Id;
            if(catalog.ConfigColumns.Any(column=>column.Id==columnId))continue;
            catalog.ConfigColumns.Add(new(){Id=columnId,ConfigTableId=target.Id,ColumnName="business_key",LogicalType="int64",IsDbNotNull=1});
            var project=catalog.ConfigColumns.Single(column=>column.ConfigTableId==target.Id&&column.ColumnName=="yz_project_id");
            var indexId="business_index_"+target.Id;
            catalog.ConfigIndexes.Add(JsonSerializer.SerializeToElement(new{id=indexId,configTableId=target.Id,isUnique=1,indexName="uniq_project_business"}));
            catalog.ConfigIndexColumns.Add(JsonSerializer.SerializeToElement(new{id=indexId+"a",configIndexId=indexId,configColumnId=project.Id,sortNo=1}));
            catalog.ConfigIndexColumns.Add(JsonSerializer.SerializeToElement(new{id=indexId+"b",configIndexId=indexId,configColumnId=columnId,sortNo=2}));
        }
        control.ControlDetail=detail.ToJsonString();
    }

    /// <summary>同一链混用原主键和文本业务键，原组件接口与 Excel 都保留前导零和大小写。</summary>
    [Theory]
    [InlineData(1,false,"OverWriteByFirstColumn")]
    [InlineData(0,false,"OnlyNew")]
    [InlineData(0,false,"OverWriteByFirstColumn")]
    [InlineData(0,true,"OverWriteByFirstColumn")]
    public async Task Mixed_text_business_keys_use_the_original_page_and_excel(int redundant,bool logicalTarget,string importMode)
    {
        var catalog=CrossTableExcelImportColumnsTests.Catalog("pathexcel","string");
        var control=catalog.ConfigColumns.Single(column=>column.Id=="25");
        AddBusinessKeys(catalog,control,primaryLevel:1);
        foreach(var column in catalog.ConfigColumns.Where(column=>column.ColumnName=="business_key"||column.Id=="45"))
        {
            column.LogicalType="string";
            column.ColumnLength="50";
        }
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId==control.Id).IsShow=1;
        catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=redundant;
        var configuredPage=catalog.ConfigPages.Single(page=>page.Id=="201");
        configuredPage.Extra["importMethod"]=JsonSerializer.SerializeToElement(importMode);
        if(redundant==0)
        {
            catalog.ConfigColumns.Add(new(){Id="281",ConfigTableId="2",ColumnName="area_id",LogicalType="int64",LinkConfigTableId="4"});
            configuredPage.Extra["exportMethod"]=JsonSerializer.SerializeToElement(logicalTarget?"edit":"list");
        }
        if(logicalTarget)
            catalog.ConfigTables.Single(table=>table.Id=="3").IsRongyu=0;
        RedundantMultiNameCacheTests.Published(catalog);
        catalog.ConfigMenus.Add(new(){Id="2200",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",Name="来源",Kind="GENERATED_PAGE",ConfigPageId="201"});
        var model=GenerationModel.Create(catalog,"Example.PathExcel");
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        var bindings=RelationPropagationCompilation.Bindings(model);
        if(redundant==1)
        {
            Assert.Equal(2,bindings.Count(binding=>binding.TargetKey=="business_key"));
            var published=GenerationMetadataPublisher.ForRelations(catalog);
            _=Metarill.Data.Relations.RelationMutationPlan.Compile(published,RelationPropagationCompilation.Definitions(bindings));
        }
        else
        {
            Assert.Empty(bindings);
            var source=model.Tables.Single(table=>table.Source.Id=="2");
            var page=Assert.Single(source.Pages);
            var scopes=NonRedundantCrossTableCompilation.Bindings(model,source,page);
            var sql=Metarill.Data.Queries.MySqlNonRedundantReadCompiler.CompileWithCrossTables(catalog,"2",
                ["country_id","country_name","area_id","place_id"],scopes);
            var from=Assert.IsType<string>(sql.GetType().GetProperty("From",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(sql));
            Assert.Contains("ON BINARY cross2.`business_key`=BINARY",from);
            Assert.Contains("cross1.`pathexcel_country_id` REGEXP '[^[:space:]]'",from);
            Assert.Contains("ON BINARY CAST(cross1.`id` AS CHAR)=BINARY",from);
            Assert.Contains("b.`place_id` REGEXP '[^[:space:]]'",from);
        }

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-text-key");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            if(!logicalTarget)await ExerciseParser(assembly,textKeys:true,sparse:redundant==0);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>真实私有解析器在原DatabaseSession事务中运行；按父/项目/状态过滤，不返回预设成功ID。</summary>
    private static async Task ExerciseParser(Assembly assembly,bool businessKeys=false,bool textKeys=false,bool sparse=false)
    {
        DatabaseSession? currentSession=null;var transport=new TrustedSecurityMemoryProvider();var rows=new Dictionary<Type,List<object>>();var reads=new List<string>();
        object Select(Type type)
        {
            var predicates=new List<Func<object,bool>>();var take=int.MaxValue;object query=null!;
            query=Proxy(typeof(ISelect<>).MakeGenericType(type),(method,args)=>{
                args??=[];
                if(method.Name=="Where")
                {
                    var expression=args!.OfType<LambdaExpression>().SingleOrDefault();
                    if(expression is not null){var predicate=expression.Compile();predicates.Add(row=>(bool)predicate.DynamicInvoke(row)!);}
                    else
                    {
                        // 仅解释本夹具实际发布的单一status等式；未知SQL必须失败，不能静默忽略范围。
                        var sql=Assert.IsType<string>(args[0]);
                        if(textKeys&&sql.StartsWith("BINARY ",StringComparison.Ordinal))
                        {
                            var bound=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(args[1]);
                            var prefix=Regex.Match(sql,@"^BINARY \x60([a-z_]+)\x60 IN \((BINARY @[A-Za-z0-9_]+)(,BINARY @[A-Za-z0-9_]+)*\)$");
                            Assert.True(prefix.Success,sql);
                            var property=string.Concat(prefix.Groups[1].Value.Split('_').Select(part=>char.ToUpperInvariant(part[0])+part[1..]));
                            var member=type.GetProperty(property)!;
                            var values=bound.Values.Select(value=>Assert.IsType<string>(value)).ToHashSet(StringComparer.Ordinal);
                            predicates.Add(row=>member.GetValue(row) is string key&&values.Contains(key));
                            reads.Add("binary:"+property);
                            return query;
                        }
                        var match=Regex.Match(sql,@"^\(*\s*\x60status\x60\s*=\s*@([A-Za-z0-9_]+)\s*\)*$");
                        Assert.True(match.Success,"离线解析器不支持该固定条件SQL");var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(args[1]);Assert.Single(parameters);
                        var expected=Convert.ToInt32(parameters[match.Groups[1].Value],System.Globalization.CultureInfo.InvariantCulture);Assert.Equal(1,expected);
                        predicates.Add(row=>Convert.ToInt32(type.GetProperty("Status")!.GetValue(row),System.Globalization.CultureInfo.InvariantCulture)==expected);reads.Add("scope:status");
                    }
                    return query;
                }
                if(method.Name=="Limit"){take=(int)args![0]!;return query;}
                if(method.Name=="ForUpdate"){Assert.NotNull(currentSession?.CurrentTransaction);reads.Add("lock:"+type.Name);}
                var filtered=rows.GetValueOrDefault(type,[]).Where(row=>predicates.All(predicate=>predicate(row))).Take(take).ToArray();
                if(method.Name=="ToListAsync")
                {var projection=args!.OfType<LambdaExpression>().SingleOrDefault();var output=projection?.ReturnType??type;var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(output))!;foreach(var row in filtered)list.Add(projection is null?row:projection.Compile().DynamicInvoke(row));return TaskValue(list.GetType(),list);}
                if(method.Name=="FirstAsync")
                {var projection=args!.OfType<LambdaExpression>().SingleOrDefault();var row=filtered.FirstOrDefault();return TaskValue(projection?.ReturnType??type,row is null||projection is null?row:projection.Compile().DynamicInvoke(row));}
                return query;
            });return query;
        }
        var orm=(IFreeSql)Proxy(typeof(IFreeSql),(method,args)=>method.Name=="Select"?Select(method.GetGenericArguments()[0]):throw new InvalidOperationException("解析测试禁止写ORM或联网"));
        var provider=new ExcelSnapshotTransport(transport,orm,textKeys);using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline"},orm,provider);currentSession=session;
        var actor=new RuntimeActor(901,"路径解析",false,"9007199254740993","1");var authorization=new ImportOnly(actor,textKeys);
        var mapper=Activator.CreateInstance(assembly.GetType("Example.PathExcel.Generated.Mappers.PathexcelSourceMapper")!,session)!;
        var type=assembly.GetType("Example.PathExcel.Generated.Services.PathexcelSourceService")!;
        var service=Activator.CreateInstance(type,session,mapper,new SnowflakeIdGenerator(949),Array.Empty<Metarill.Core.Persistence.IRecordMutationObserver>(),Array.Empty<Metarill.Core.Persistence.IRecordMutationGuard>(),authorization)!;
        if(textKeys){await ExerciseTextPath();return;}
        Add("Country",11,"国甲");Add("Country",12,"国乙");Add("Area",21,"区甲","PathexcelCountryId",11);Add("Area",22,"区乙","PathexcelCountryId",12);Add("Place",31,"城市甲","PathexcelAreaId",21);Add("Place",32,"城市乙","PathexcelAreaId",22);
        Add("Country",14,"国甲",status:0);Add("Country",15,"国甲",project:2);Add("Country",16,"国甲",deleted:true);
        Add("Place",34,"9007199254740993","PathexcelAreaId",21);
        var numeric=await Parse(new Dictionary<string,object?>{{"级联",9007199254740993L}});Assert.Equal("34",Text(numeric["placeId"]));
        foreach(var item in new[]{("国甲/区甲/城市甲","11","21","31"),("国甲//","11",null,null),("国甲/区甲/","11","21",null),("城市甲","11","21","31"),("国甲/区甲","11","21",null)})
        {
            var values=await Parse(new Dictionary<string,object?>{{"名称","样本"},{"级联",item.Item1}});
            Assert.Equal(item.Item2,Text(values["countryId"]));Assert.Equal(item.Item3,Text(values["areaId"]));Assert.Equal(item.Item4,Text(values["placeId"]));
        }
        var clear=await Parse(new Dictionary<string,object?>{{"级联",null},{"祖先ID","11"}});Assert.Equal("11",Text(clear["countryId"]));Assert.Null(Text(clear["placeId"]));
        foreach(var reverse in new[]{false,true})
        {
            var entries=new[]{new KeyValuePair<string,object?>("祖先ID","12"),new("级联","国甲//")};
            await Assert.ThrowsAsync<PageImportException>(()=>Parse((reverse?entries.Reverse():entries).ToDictionary(pair=>pair.Key,pair=>pair.Value)));
        }
        await Assert.ThrowsAsync<PageImportException>(()=>Parse(new Dictionary<string,object?>{{"级联","国甲/区乙/城市乙"}}));
        await Assert.ThrowsAsync<PageImportException>(()=>Parse(new Dictionary<string,object?>{{"级联","国甲//"}},"202","12"));
        Assert.Equal("12",Text((await Parse(new Dictionary<string,object?>{{"级联",null}},"202","12"))["countryId"]));
        Assert.Equal("12",Text((await Parse(new Dictionary<string,object?>{{"级联","国乙//"}},"202","12"))["countryId"]));
        await Assert.ThrowsAsync<PageImportException>(()=>Parse(new Dictionary<string,object?>{{"祖先ID","12"},{"级联","国乙//"}},"202","12"));
        var locksBeforeExport=reads.Count(value=>value.StartsWith("lock:",StringComparison.Ordinal));
        AddSource(101,"根",11,null,null);AddSource(102,"中",11,21,null);AddSource(103,"叶",11,21,31);
        var exported=await (Task<PageExportRequest>)type.GetMethod("Export201Async")!.Invoke(service,[new PageRequest(),actor,100,CancellationToken.None])!;
        var names=exported.Headers.ToList().IndexOf("名称");var paths=exported.Headers.ToList().IndexOf("级联");Assert.True(names>=0&&paths>=0);
        var cells=exported.Rows.ToDictionary(row=>row[names]!.ToString()!,row=>row[paths]?.ToString());
        Assert.Equal("国甲//",cells["根"]);Assert.Equal("国甲/区甲/",cells["中"]);Assert.Equal("国甲/区甲/城市甲",cells["叶"]);Assert.Equal(1,provider.Snapshots);Assert.Equal(locksBeforeExport,reads.Count(value=>value.StartsWith("lock:",StringComparison.Ordinal)));
        Add("Place",33,"国甲","PathexcelAreaId",21);
        await Assert.ThrowsAsync<PageImportException>(()=>Parse(new Dictionary<string,object?>{{"级联","国甲"}}));
        Assert.Equal("11",Text((await Parse(new Dictionary<string,object?>{{"级联","国甲//"}}))["countryId"]));
        Add("Country",13,"国甲");await Assert.ThrowsAsync<PageImportException>(()=>Parse(new Dictionary<string,object?>{{"级联","国甲//"}}));
        Assert.Contains("scope:status",reads);Assert.Contains(reads,value=>value.StartsWith("lock:",StringComparison.Ordinal));Assert.Empty(transport.Commands);Assert.Equal(7,transport.Rollbacks);
        if(businessKeys)
        {
            Add("Country",9_007_199_254_740_995L,"高位业务键");
            Assert.Equal("9007199254740995",Text((await Parse(new Dictionary<string,object?>{{"级联","高位业务键//"}}))["countryId"]));
        }
        return;

        async Task ExerciseTextPath()
        {
            Add("Country",11,"国甲",key:"001");
            Add("Country",12,"国乙",key:"A-01");
            Add("Country",13,"外部",project:2,key:"outside");
            Add("Area",21,"区甲","PathexcelCountryId","001");
            Add("Area",22,"区乙","PathexcelCountryId","A-01");
            Add("Place",31,"城甲","PathexcelAreaId",21,key:"Leaf-A");
            Add("Place",32,"城乙","PathexcelAreaId",22,key:"Leaf-B");
            var leafRequest=new CascadeTreePathRequest{LeafId=JsonSerializer.SerializeToElement("Leaf-A")};
            var pathMethod=type.GetMethod("CrossPath201_25Async")!;
            var path=await (Task<CrossTablePath>)pathMethod.Invoke(service,[leafRequest,actor,CancellationToken.None])!;
            Assert.Equal(new[]{"001","21","Leaf-A"},path.Path.Select(node=>node.Id));
            var wrongCase=new CascadeTreePathRequest{LeafId=JsonSerializer.SerializeToElement("leaf-a")};
            await Assert.ThrowsAsync<InvalidOperationException>(()=>(Task<CrossTablePath>)pathMethod.Invoke(service,[wrongCase,actor,CancellationToken.None])!);
            var optionsRequest=new CascadeTreeOptionsRequest{LevelKey="地区-二",SelectedPath=JsonSerializer.SerializeToElement(new{countryId="001"})};
            var options=await (Task<IReadOnlyList<CrossTableOption>>)type.GetMethod("CrossOptions201_25Async")!.Invoke(service,[optionsRequest,actor,CancellationToken.None])!;
            Assert.Equal("21",Assert.Single(options).Id);
            var parsed=await Parse(new Dictionary<string,object?>{{"级联","国甲/区甲/城甲"}});
            Assert.Equal("001",Text(parsed["countryId"]));
            Assert.Equal("Leaf-A",Text(parsed["placeId"]));
            await Assert.ThrowsAsync<PageImportException>(()=>Parse(new Dictionary<string,object?>{{"级联","国甲/区乙/城乙"}}));
            var rootOnly=await Parse(new Dictionary<string,object?>{{"级联","国甲//"}});
            Assert.Equal("001",Text(rootOnly["countryId"]));
            Assert.Null(Text(rootOnly["placeId"]));
            var entity=AddSource(101,"叶记录","A-01",22,"Leaf-A");
            var normalize=type.GetMethod("NormalizeCross201_25Async",BindingFlags.Instance|BindingFlags.NonPublic)!;
            await session.InTransactionAsync(async()=>
            {
                await (Task)normalize.Invoke(service,[entity,actor,new HashSet<string>{"place_id"},CancellationToken.None,false])!;
                return true;
            });
            Assert.Equal("001",entity.GetType().GetProperty("CountryId")!.GetValue(entity));
            Assert.Equal("21",Convert.ToString(entity.GetType().GetProperty("AreaId")!.GetValue(entity),System.Globalization.CultureInfo.InvariantCulture));
            if(sparse)
            {
                var pack=type.GetMethod("PackCross201_25",BindingFlags.Static|BindingFlags.NonPublic)!;
                var hydrate=type.GetMethod("HydrateStoredCross201_25Async",BindingFlags.Instance|BindingFlags.NonPublic)!;
                var fields=new HashSet<string>{"place_id"};
                pack.Invoke(null,[entity,fields]);
                Assert.Null(entity.GetType().GetProperty("CountryId")!.GetValue(entity));
                Assert.Null(entity.GetType().GetProperty("AreaId")!.GetValue(entity));
                Assert.Equal("Leaf-A",entity.GetType().GetProperty("PlaceId")!.GetValue(entity));
                await session.InTransactionAsync(async()=>
                {
                    await (Task)hydrate.Invoke(service,[entity,actor,CancellationToken.None])!;
                    return true;
                });
                Assert.Equal("001",entity.GetType().GetProperty("CountryId")!.GetValue(entity));
                Assert.Equal(21L,entity.GetType().GetProperty("AreaId")!.GetValue(entity));
                var middle=AddSource(102,"只到区","001",21,null);
                pack.Invoke(null,[middle,new HashSet<string>{"area_id"}]);
                Assert.Null(middle.GetType().GetProperty("CountryId")!.GetValue(middle));
                Assert.Equal(21L,middle.GetType().GetProperty("AreaId")!.GetValue(middle));
                Assert.Null(middle.GetType().GetProperty("PlaceId")!.GetValue(middle));
                Assert.Empty(transport.Commands);
                return;
            }
            var exported=await (Task<PageExportRequest>)type.GetMethod("Export201Async")!.Invoke(service,[new PageRequest(),actor,100,CancellationToken.None])!;
            Assert.Equal("国甲/区甲/城甲",Assert.Single(exported.Rows)[exported.Headers.ToList().IndexOf("级联")]);
            var filter=new PageRequest{Filters=new(){{"placeId",JsonSerializer.SerializeToElement(new{placeId="leaf-a"})}}};
            var filtered=await (Task<PageExportRequest>)type.GetMethod("Export201Async")!.Invoke(service,[filter,actor,100,CancellationToken.None])!;
            Assert.Empty(filtered.Rows);
            Assert.Contains("binary:PlaceId",reads);
            Assert.Contains("binary:BusinessKey",reads);
            Assert.Contains("binary:PathexcelCountryId",reads);
            Assert.Empty(transport.Commands);
        }

        void Add(string name,long id,string label,string? parent=null,object? parentId=null,int status=1,long project=9007199254740993,bool deleted=false,string? key=null)
        {
            var entityType=assembly.GetType("Example.PathExcel.Generated.Entities.Pathexcel"+name)!;var row=Activator.CreateInstance(entityType)!;
            void Set(string field,object? value){var property=entityType.GetProperty(field)!;var destination=Nullable.GetUnderlyingType(property.PropertyType)??property.PropertyType;property.SetValue(row,value is null?null:Convert.ChangeType(value,destination,System.Globalization.CultureInfo.InvariantCulture));}
            Set("Id",businessKeys?9_007_199_254_740_990L+id:id);if(businessKeys||key is not null)Set("BusinessKey",key is not null?key:id);Set("Name",label);Set("YzProjectId",project);Set("Status",status);if(deleted)Set("DeleteNo",DateTime.UtcNow);if(parent is not null)Set(parent,parentId);
            if(!rows.TryGetValue(entityType,out var data))rows[entityType]=data=[];data.Add(row);
        }
        object AddSource(long id,string name,object country,object? area,object? place)
        {
            var entityType=assembly.GetType("Example.PathExcel.Generated.Entities.PathexcelSource")!;var row=Activator.CreateInstance(entityType)!;
            foreach(var (field,value) in new (string,object?)[]{("Id",id),("Name",name),("YzProjectId",9007199254740993L),("Status",1),("CountryId",country),("AreaId",area),("PlaceId",place)})
            {var property=entityType.GetProperty(field)!;var destination=Nullable.GetUnderlyingType(property.PropertyType)??property.PropertyType;property.SetValue(row,value is null?null:Convert.ChangeType(value,destination,System.Globalization.CultureInfo.InvariantCulture));}
            if(!rows.TryGetValue(entityType,out var data))rows[entityType]=data=[];data.Add(row);return row;
        }
        async Task<Dictionary<string,JsonElement>> Parse(Dictionary<string,object?> row,string page="201",string? parent=null)
        {
            return await session.InTransactionAsync(async()=>{
                Task task;
                var parameters=new List<object?>{JsonSerializer.SerializeToElement(row),parent,actor};
                if(page=="202")parameters.Add((Func<Task<IReadOnlyDictionary<string,JsonElement>>>)ReadFixedParent);
                parameters.Add(CancellationToken.None);
                try{task=(Task)type.GetMethod("ParseImport"+page+"Async",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(service,parameters.ToArray())!;}
                catch(TargetInvocationException error)when(error.InnerException is not null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
                await task;return (Dictionary<string,JsonElement>)task.GetType().GetProperty("Result")!.GetValue(task)!;
            });

            async Task<IReadOnlyDictionary<string,JsonElement>> ReadFixedParent()
            {
                var factory=type.GetMethod("CrossReader202_25",BindingFlags.Instance|BindingFlags.NonPublic)!;
                var reader=(CrossTableReader)factory.Invoke(service,[actor,true,false])!;
                var path=await reader.PathAsync(0,long.Parse(parent!,System.Globalization.CultureInfo.InvariantCulture));
                return new Dictionary<string,JsonElement>{{"countryId",JsonSerializer.SerializeToElement(Assert.Single(path.Path).Id)}};
            }
        }
    }
    /// <summary>只记录生成导出是否请求适配器快照，内部ADO仍为离线传输，不证明MySQL隔离或锁。</summary>
    private sealed class ExcelSnapshotTransport(TrustedSecurityMemoryProvider inner,IFreeSql orm,bool mysql):IDatabaseProvider
    {
        internal int Snapshots;
        public string Name=>mysql?"mysql":"offline";
        public DatabaseCapabilities Capabilities=>inner.Capabilities;
        public void Validate(DatabaseOptions options)=>inner.Validate(options);
        public IFreeSql CreateOrm(DatabaseOptions options)=>orm;
        public DbConnection CreateConnection(DatabaseOptions options)=>inner.CreateConnection(options);
        public string QuoteIdentifier(string identifier)=>inner.QuoteIdentifier(identifier);
        public string ParameterPlaceholder(string name)=>inner.ParameterPlaceholder(name);
        public string BuildSelectForUpdate(string sql)=>inner.BuildSelectForUpdate(sql);
        public ValueTask<DbTransaction> BeginReadSnapshotAsync(DbConnection connection,CancellationToken token=default)
        {Snapshots++;return connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,token);}
        public Task EnsureReadSnapshotAsync(DbTransaction transaction,CancellationToken token=default)=>throw new InvalidOperationException("离线替身不证明嵌套快照");
    }
    /// <summary>统一读取离线回执中的null与精确整数文本。</summary>
    private static string? Text(JsonElement value)=>value.ValueKind==JsonValueKind.Null?null:value.ToString();
    /// <summary>按真实泛型结果构造异步传输，不替代业务方法。</summary>
    private static object TaskValue(Type type,object? value)=>typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type).Invoke(null,[value])!;
    /// <summary>仅代理数据库传输接口；业务与生成表达式仍实际执行。</summary>
    private static object Proxy(Type type,Func<MethodInfo,object?[]?,object?> action){var proxy=DispatchProxy.Create(type,typeof(GeneratedMutationGuardRun.CallProxy));((GeneratedMutationGuardRun.CallProxy)proxy).Action=action;return proxy;}
    /// <summary>导入不借用普通CRUD或目标list权；解析器之外的来源授权由公共入口单独验收。</summary>
    private sealed class ImportOnly(RuntimeActor actor,bool permitList):IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>actor;
        public void RequirePagePermission(string page,string operation)
        {
            if(!permitList||operation!="list"||page is not("201" or "301" or "401" or "501"))
                throw new UnauthorizedAccessException("解析不能增加页面list要求");
        }
    }
}
