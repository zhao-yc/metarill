using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>来源参与固定层图时按角色取名；同表／同ID仍保留各层父键和部分选择深度。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CrossTableExcelSourceRoleGenerationTests
{
    private const string Prefix="source_excel",Namespace="Example.SourceExcel",Entity="SourceExcelSource";
    /// <summary>来源在根、中、末层单次或重复出现时，实际执行生成的导出及导入，写前由守卫记录将要执行的动作。</summary>
    [Theory]
    [InlineData(1,"string","int64",false,"list","OnlyNew",false)]
    [InlineData(2,"int64","string",false,"edit","OverWriteByFirstColumn",false)]
    [InlineData(4,"string","string",false,"list","OverWriteByFirstColumn",true)]
    [InlineData(3,"string","int64",true,"list","OnlyNew",false)]
    [InlineData(5,"int64","string",true,"edit","OverWriteByFirstColumn",false)]
    [InlineData(6,"string","string",true,"edit","OnlyNew",false)]
    [InlineData(7,"int64","int64",true,"list","OverWriteByFirstColumn",true)]
    [InlineData(3,"int64","int64",false,"edit","OnlyNew",false)]
    [InlineData(5,"string","string",false,"list","OverWriteByFirstColumn",false)]
    [InlineData(6,"int64","string",false,"edit","OverWriteByFirstColumn",true)]
    [InlineData(7,"string","int64",false,"list","OnlyNew",false)]
    public async Task Physical_source_roles_roundtrip_original_excel(int mask,string type,string parent,bool custom,string exportMode,string importMode,bool leafKey)
    {
        var roles=SourceRoles(mask);
        var catalog=roles.Length==1
            ? RedundantMultiNameCacheTests.Published(CrossTableSourceGraphTests.Catalog(Prefix,roles[0],type,parent))
            : RedundantRepeatedCrossSourceTests.Catalog(Prefix,mask,type,parent,custom);
        Enable(catalog,exportMode,importMode,leafKey);
        await Build(catalog,async(assembly,model)=>
        {
            var guard=new CrossTableExcelSharedGenerationTests.StopBeforeExcelWrite();
            using var runtime=new SharedExcelReadTransport(assembly,Namespace,Entity,sourceCondition:true,guard:guard);
            var source=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(source.Pages);var plan=Assert.Single(CrossTableWriteCompilation.ForService(model,source,page));
            Assert.Equal(roles.Length,plan.Levels.Count(level=>level.Read.Target.Source.Id=="2"));
            var names=plan.Levels.Select((level,index)=>level.Read.Target.Source.Id=="2"?(custom?new[]{"国甲","区甲","城甲"}[index]:"源甲"):new[]{"国甲","区甲","城甲"}[index]).ToArray();
            var fields=Base(11,"源甲");foreach(var (level,index) in plan.Levels.Select((level,index)=>(level,index)).Where(item=>item.level.Read.Target.Source.Id=="2"))
            {fields[level.Read.Name.Property]=names[index];if(level.Read.Parent is{} parentColumn)fields[parentColumn.Property]=11L;}
            runtime.Add(Entity,fields);
            foreach(var level in plan.Levels.Where(level=>level.Read.Target.Source.Id!="2"))
            {
                var index=plan.Levels.ToList().IndexOf(level);var target=new Dictionary<string,object?>{{"Id",11L},{"Name",names[index]},{"YzProjectId",9007199254740993L},{"Status",1}};
                if(level.Read.Parent is{} parentColumn)target[parentColumn.Property]=11L;runtime.Add(level.Read.Target.Name,target);
            }
            // 同名来源行分别违背每一个真实条件，合法路径必须仍只有一条候选。
            foreach(var (id,kind) in new[]{(12L,"status"),(13L,"code"),(14L,"amount"),(15L,"deleted"),(16L,"project")})
            {
                var hidden=new Dictionary<string,object?>(fields){["Id"]=id};
                if(kind=="status")hidden["Status"]=0;if(kind=="code")hidden["Code"]="d";if(kind=="amount")hidden["Amount"]=0.01m;if(kind=="deleted")hidden["DeleteNo"]=DateTime.UtcNow;if(kind=="project")hidden["YzProjectId"]=2L;runtime.Add(Entity,hidden);
            }
            var paths=new[]{names[0]+"//",names[0]+"/"+names[1]+"/",string.Join("/",names)};
            for(var depth=0;depth<3;depth++)await AssertParsedDepth(paths[depth],depth);
            if(custom||roles.Length==1&&roles[0]!=2)AssertSlots(await Parse(names[2]),2);
            else if(roles.Length>1)await Assert.ThrowsAsync<PageImportException>(()=>Parse("源甲"));
            await Assert.ThrowsAsync<PageImportException>(()=>Parse("不存在/错误/层级"));
            var firstRole=roles[0];if(custom)
            {var wrong=names.ToArray();wrong[firstRole]="源甲";await Assert.ThrowsAsync<PageImportException>(()=>Parse(string.Join("/",wrong)));}
            for(var depth=0;depth<3;depth++)
            {
                var display=Base(101+depth,new[]{"根展示","中展示","叶展示"}[depth]);
                foreach(var (level,index) in plan.Levels.Select((level,index)=>(level,index)))display[level.Id.Property]=index<=depth?11L:null;
                runtime.Add(Entity,display);
            }
            var before=runtime.Reads.Count(value=>value.StartsWith("lock:",StringComparison.Ordinal));
            var exported=await runtime.Export(new PageRequest{SelectedIds=["101","102","103"]});
            var nameIndex=exported.Headers.ToList().IndexOf("名称");var pathIndex=exported.Headers.ToList().IndexOf("级联");Assert.True(nameIndex>=0&&pathIndex>=0);
            var cells=exported.Rows.ToDictionary(row=>row[nameIndex]!.ToString()!,row=>row[pathIndex]?.ToString());Assert.Equal(3,cells.Count);
            for(var depth=0;depth<3;depth++)
            {
                var name=new[]{"根展示","中展示","叶展示"}[depth];
                Assert.Equal(paths[depth],cells[name]);await AssertParsedDepth(cells[name]!,depth);
            }
            Assert.Equal(1,runtime.Snapshots);Assert.Equal(before,runtime.ExportLockCount);Assert.Empty(runtime.Transport.Commands);
            foreach(var field in new[]{"status","code","amount"})Assert.Contains("scope:"+field,runtime.Reads);

            var overwrite=importMode=="OverWriteByFirstColumn";
            if(overwrite)
            {
                var missing=await Assert.ThrowsAsync<PageImportException>(()=>runtime.Import([
                    leafKey?new(){{"名称","叶展示"},{"级联",paths[1]}}:new(){{"级联",paths[2]}}]));
                Assert.Equal((PageImportError.MissingOverwriteKey,(int?)1,leafKey?"级联":"名称"),
                    (missing.Code.GetValueOrDefault(),missing.RowNumber,missing.ColumnName));
                Assert.Null(guard.Validation);
            }
            var importedName=overwrite?"叶展示":"追加";
            var stopped=await Assert.ThrowsAsync<BusinessRuleException>(()=>runtime.Import([
                new(){{"名称",importedName},{"级联",overwrite?cells[importedName]:paths[1]}}]));
            Assert.Same(guard.Stop,stopped);
            Assert.Equal(overwrite?RecordMutationOperation.Update:RecordMutationOperation.Create,guard.Validation!.Operation);
            if(overwrite)Assert.Equal(103L,guard.Validation.RecordId);
            Assert.Empty(runtime.Transport.Commands);
            return;
            Task<Dictionary<string,JsonElement>> Parse(string text)=>runtime.Parse(new Dictionary<string,object?>{{"级联",text}});
            async Task AssertParsedDepth(string text,int depth)
            {
                if(leafKey&&depth<2)
                {
                    var error=await Assert.ThrowsAsync<PageImportException>(()=>Parse(text));
                    Assert.Equal(PageImportError.MissingOverwriteKey,error.Code);
                }
                else AssertSlots(await Parse(text),depth);
            }
        });
    }
    /// <summary>非冗余物理、派生、混合名称实际生成；逻辑覆盖与导出保留原页面条件的依赖列。</summary>
    [Theory]
    [InlineData(1,"physical","list","OnlyNew",false)]
    [InlineData(2,"derived","edit","OverWriteByFirstColumn",false)]
    [InlineData(4,"physical","list","OverWriteByFirstColumn",true)]
    [InlineData(3,"physical","edit","OverWriteByFirstColumn",true)]
    [InlineData(5,"derived","list","OverWriteByFirstColumn",false)]
    [InlineData(7,"mixed","edit","OnlyNew",false)]
    public Task Sparse_source_role_Excel_compiles_with_original_source_conditions(int mask,string mode,string exportMode,string importMode,bool leafKey)
    {
        var roles=SourceRoles(mask);
        var catalog=roles.Length==1
            ? NonRedundantCrossSourceTests.Catalog(Prefix,roles[0],"string","int64",mode=="derived")
            : RepeatedCrossSourceTests.Catalog(Prefix,mask,"string","int64",mode);
        Enable(catalog,exportMode,importMode,leafKey);
        return Build(catalog,(assembly,model)=>
        {
            var source=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(source.Pages);
            var path=Assert.Single(CrossTableWriteCompilation.ForService(model,source,page));
            Assert.Equal(roles.Length,path.Levels.Count(level=>level.Read.Target.Source.Id=="2"));
            var service=assembly.GetType(Namespace+".Generated.Services."+Entity+"Service",true)!;
            Assert.NotNull(service.GetMethod("Import201Async"));
            var export=ReadPlan("ExportReadPlan201");
            Assert.All(new[]{"country_id","area_id","place_id"},column=>Assert.Contains(column,export.OutputTypes.Keys));
            if(leafKey)Assert.Equal("string",ReadPlan("ImportMatchPlan201").OutputTypes["place_id"]);
            return Task.CompletedTask;
            Metarill.Data.Queries.NonRedundantReadPlan ReadPlan(string name)=>Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                service.GetField(name,BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null));
        });
    }

    /// <summary>使用专用业务父页与不带父参数的原候选页，验证同表目标不会继承来源请求范围。</summary>
    [Theory]
    [InlineData(1,1,0,"list","OnlyNew",false)]
    [InlineData(7,1,1,"edit","OverWriteByFirstColumn",true)]
    [InlineData(4,1,2,"edit","OverWriteByFirstColumn",false)]
    [InlineData(1,0,0,"list","OnlyNew",false)]
    [InlineData(7,0,1,"edit","OverWriteByFirstColumn",true)]
    [InlineData(4,0,2,"edit","OverWriteByFirstColumn",false)]
    public Task Scoped_source_role_excel_uses_actual_parent_path(int mask,int redundant,int depth,string exportMode,string importMode,bool leafKey)
    {
        var roles=SourceRoles(mask);
        var catalog=(redundant,roles.Length) switch
        {
            (1,1)=>RedundantMultiNameCacheTests.Published(CrossTableSourceGraphTests.Catalog(Prefix,roles[0])),
            (1,_)=>RedundantRepeatedCrossSourceTests.Catalog(Prefix,mask,"string","int64",true),
            (0,1)=>NonRedundantCrossSourceTests.Catalog(Prefix,roles[0],"string","int64",false),
            _=>RepeatedCrossSourceTests.Catalog(Prefix,mask,"string","int64","derived")
        };
        var sourcePage=catalog.ConfigPages.Single(page=>page.Id=="201");
        var candidate=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(sourcePage))!;
        candidate.Id="202";
        catalog.ConfigPages.Add(candidate);
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201"&&field.ConfigColumnId!="25").ToArray())
        {
            var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;
            copy.Id+="candidate";copy.ConfigPageId=candidate.Id;catalog.ConfigPageColumns.Add(copy);
        }
        var parentColumn=new[]{"country_id","area_id","place_id"}[depth];
        sourcePage.WhereSql+=" AND "+parentColumn+"=@parent_id";
        Enable(catalog,exportMode,importMode,leafKey);

        return Build(catalog,async(assembly,model)=>
        {
            var source=model.Tables.Single(table=>table.Source.Id=="2");var page=source.Pages.Single(page=>page.Source.Id=="201");
            var path=Assert.Single(CrossTableWriteCompilation.ForService(model,source,page));
            Assert.All(path.Levels.Where(level=>level.Read.Target.Source.Id=="2"),level=>Assert.Equal("202",level.Read.Page.Source.Id));
            if(redundant==0)
            {
                var service=assembly.GetType(Namespace+".Generated.Services."+Entity+"Service",true)!;
                Assert.NotNull(service.GetMethod("Import201Async"));
                Assert.NotNull(service.GetField("LogicalParentPlan201",BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null));
                Assert.NotNull(service.GetField("ExportReadPlan201",BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null));
                if(leafKey)Assert.NotNull(service.GetField("ImportMatchPlan201",BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null));
                return;
            }

            var guard=new CrossTableExcelSharedGenerationTests.StopBeforeExcelWrite();
            using var runtime=new SharedExcelReadTransport(assembly,Namespace,Entity,sourceCondition:true,guard:guard);
            var physicalParents=Metarill.Data.Queries.RedundantSharedParents.Compile(catalog,"2","201");
            Assert.Equal(depth+1,Assert.Single(physicalParents).Value.Levels.Count);
            Seed(11,"甲");
            Seed(12,"乙");
            var first=Base(103,"同名");var other=Base(104,"同名");
            foreach(var level in path.Levels){first[level.Id.Property]=11L;other[level.Id.Property]=12L;}
            runtime.Add(Entity,first);runtime.Add(Entity,other);
            var fullPath=string.Join("/",Names("甲"));
            var exported=await runtime.Export(new PageRequest{ParentId="11"});
            Assert.Equal(fullPath,Assert.Single(exported.Rows)[exported.Headers.ToList().IndexOf("级联")]);
            Assert.Equal(0,runtime.ExportLockCount);

            if(depth<2)
            {
                var conflict=await Assert.ThrowsAsync<PageImportException>(()=>runtime.Import([
                    new(){{"名称","跨父"},{"级联",string.Join("/",Names("乙"))}}],"11"));
                Assert.Equal(PageImportError.Relation,conflict.Code);
                Assert.Null(guard.Validation);
            }
            var overwrite=importMode=="OverWriteByFirstColumn";
            var row=new Dictionary<string,object?>{{"名称",overwrite?"同名":"追加"}};
            if(depth<2)row.Add("级联",fullPath);
            var stopped=await Assert.ThrowsAsync<BusinessRuleException>(()=>runtime.Import([row],"11"));
            Assert.Same(guard.Stop,stopped);
            Assert.Equal(overwrite?RecordMutationOperation.Update:RecordMutationOperation.Create,guard.Validation!.Operation);
            if(overwrite)Assert.Equal(103L,guard.Validation.RecordId);
            Assert.Empty(runtime.Transport.Commands);

            string[] Names(string label)=>path.Levels.Select((level,index)=>
                level.Read.Target.Source.Id=="2"&&roles.Length==1?"源"+label:new[]{"国","区","城"}[index]+label).ToArray();
            void Seed(long id,string label)
            {
                var fields=Base(id,"源"+label);var names=Names(label);
                foreach(var (level,index) in path.Levels.Select((level,index)=>(level,index)))
                {
                    var target=level.Read.Target.Source.Id=="2"?fields:new Dictionary<string,object?>
                        {{"Id",id},{"Name",names[index]},{"YzProjectId",9007199254740993L},{"Status",1}};
                    target[level.Read.Name.Property]=names[index];
                    if(level.Read.Parent is{} parent)target[parent.Property]=id;
                    if(level.Read.Target.Source.Id!="2")runtime.Add(level.Read.Target.Name,target);
                }
                runtime.Add(Entity,fields);
            }
        });
    }

    /// <summary>使用与生产一致的字段例，测试可按表级顺序选物理文本或逻辑末级为首列。</summary>
    private static void Enable(MetadataCatalog catalog,string exportMode,string importMode,bool leafKey)
    {
        var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.IsImport=1;page.IsExport=1;
        page.Extra["importMethod"]=JsonSerializer.SerializeToElement(importMode);
        page.Extra["exportMethod"]=JsonSerializer.SerializeToElement(exportMode);
        foreach(var (id,name) in new[]{("22","名称"),("25","级联")})
        {
            var column=catalog.ConfigColumns.Single(column=>column.Id==id);column.Name=name;
            if(id==(leafKey?"25":"22"))column.SortNo=-1;
            var field=catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId==id);
            field.IsShow=1;field.IsEdit=1;field.Extra["isDetailShow"]=JsonSerializer.SerializeToElement(1);
        }
    }

    /// <summary>按层位展开同一来源表的候选角色。</summary>
    private static int[] SourceRoles(int mask)=>Enumerable.Range(0,3).Where(index=>(mask&(1<<index))!=0).ToArray();

    /// <summary>验证公开 Full 的生成门禁、实际编译以及再次生成时对手写 partial 的保护。</summary>
    private static async Task Build(MetadataCatalog catalog,Func<Assembly,GenerationModel,Task> exercise)
    {
        var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,Namespace);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));new FrontendPackager().Validate(catalog);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-source-role-excel");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var generated=Path.Combine(directory,"generated");
        try
        {
            new CSharpGenerator().Generate(catalog,generated,Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var hand=Path.Combine(generated,"Services/"+Entity+"Service.cs");await File.AppendAllTextAsync(hand,"\n// 来源角色Excel手写扩展必须保留。\n");var retained=await File.ReadAllBytesAsync(hand);new CSharpGenerator().Generate(catalog,generated,Namespace);Assert.Equal(retained,await File.ReadAllBytesAsync(hand));
            Assert.Equal(before,JsonSerializer.Serialize(catalog));
            await exercise(assembly,model);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>来源节点与消费行均服从原项目、status、code和金额下限，不绕过复合来源条件。</summary>
    private static Dictionary<string,object?> Base(long id,string name)=>new(){{"Id",id},{"Name",name},{"YzProjectId",9007199254740993L},{"Status",1},{"Code","e"},{"Amount",1m}};
    /// <summary>同表各层的相同整数ID按固定深度分别还原，未选择后段保持null。</summary>
    private static void AssertSlots(Dictionary<string,JsonElement> values,int depth)
    {var columns=new[]{"countryId","areaId","placeId"};for(var index=0;index<3;index++)Assert.Equal(index<=depth?"11":null,values[columns[index]].ValueKind==JsonValueKind.Null?null:values[columns[index]].ToString());}
}
