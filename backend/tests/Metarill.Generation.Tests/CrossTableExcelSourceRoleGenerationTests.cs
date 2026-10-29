using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>来源参与固定层图时按角色取名；同表／同ID仍保留各层父键和部分选择深度。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CrossTableExcelSourceRoleGenerationTests
{
    private const string Prefix="source_excel",Namespace="Example.SourceExcel",Entity="SourceExcelSource";
    /// <summary>四种重复角色图分别覆盖原同名字段和每角色独立物理名称，实际执行解析与导出。</summary>
    [Theory]
    [InlineData(3,"string","int64",true)][InlineData(5,"int64","string",true)][InlineData(6,"string","string",true)][InlineData(7,"int64","int64",true)]
    [InlineData(3,"int64","int64",false)][InlineData(5,"string","string",false)][InlineData(6,"int64","string",false)][InlineData(7,"string","int64",false)]
    public async Task Repeated_physical_roles_keep_same_ID_and_distinct_role_names(int mask,string type,string parent,bool custom)
    {
        var catalog=RedundantRepeatedCrossSourceTests.Catalog(Prefix,mask,type,parent,custom);Enable(catalog);
        await Build(catalog,async(assembly,model)=>
        {
            using var runtime=new SharedExcelReadTransport(assembly,Namespace,Entity,sourceCondition:true);
            var source=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(source.Pages);var plan=Assert.Single(CrossTableWriteCompilation.ForService(model,source,page));
            Assert.Equal(RepeatedCrossSourceTests.Roles(mask).Length,plan.Levels.Count(level=>level.Read.Target.Source.Id=="2"));
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
            for(var depth=0;depth<3;depth++)AssertSlots(await Parse(paths[depth]),depth);
            if(custom)AssertSlots(await Parse(names[2]),2);
            else await Assert.ThrowsAsync<PageImportException>(()=>Parse("源甲"));
            await Assert.ThrowsAsync<PageImportException>(()=>Parse("不存在/错误/层级"));
            var firstRole=RepeatedCrossSourceTests.Roles(mask)[0];if(custom)
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
            for(var depth=0;depth<3;depth++){var name=new[]{"根展示","中展示","叶展示"}[depth];Assert.Equal(paths[depth],cells[name]);AssertSlots(await Parse(cells[name]!),depth);}
            Assert.Equal(1,runtime.Snapshots);Assert.Equal(before,runtime.ExportLockCount);Assert.Empty(runtime.Transport.Commands);
            foreach(var field in new[]{"status","code","amount"})Assert.Contains("scope:"+field,runtime.Reads);
            return;
            Task<Dictionary<string,JsonElement>> Parse(string text)=>runtime.Parse(new Dictionary<string,object?>{{"级联",text}});
        });
    }
    /// <summary>R0同源的物理／派生／混合名称只证明生成可编译，实际SQL和存储另验。</summary>
    [Theory][InlineData("physical")][InlineData("derived")][InlineData("mixed")]
    public Task R0_repeated_source_Excel_compiles_with_original_source_conditions(string mode)
    {
        var catalog=RepeatedCrossSourceTests.Catalog(Prefix,7,"string","int64",mode);Enable(catalog);
        return Build(catalog,(_,model)=>{var source=model.Tables.Single(table=>table.Source.Id=="2");var plan=Assert.Single(CrossTableWriteCompilation.ForService(model,source,Assert.Single(source.Pages)));Assert.Equal(3,plan.Levels.Count(level=>level.Read.Target.Source.Id=="2"));return Task.CompletedTask;});
    }
    /// <summary>启用原Excel字段且仅改测试元数据标题；不改生成器／原前端结构来迁就组合。</summary>
    private static void Enable(MetadataCatalog catalog)
    {
        var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.IsImport=1;page.IsExport=1;
        foreach(var (id,name) in new[]{("22","名称"),("25","级联")})
        {catalog.ConfigColumns.Single(column=>column.Id==id).Name=name;var field=catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId==id);field.IsShow=1;field.IsEdit=1;}
    }
    /// <summary>每份原模型实际生成并严格编译，手写partial不覆盖，公开Full仍拒绝未验Excel用途。</summary>
    private static async Task Build(MetadataCatalog catalog,Func<Assembly,GenerationModel,Task> exercise)
    {
        var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,Namespace);var proof=CrossTableExcelApplicationPreparer.RequireCurrent(catalog,Namespace);Assert.NotEmpty(proof.Gaps);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-source-role-excel");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var generated=Path.Combine(directory,"generated");
        try
        {
            new CSharpGenerator().Generate(catalog,generated,Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var hand=Path.Combine(generated,"Services/"+Entity+"Service.cs");await File.AppendAllTextAsync(hand,"\n// 来源角色Excel手写扩展必须保留。\n");var retained=await File.ReadAllBytesAsync(hand);new CSharpGenerator().Generate(catalog,generated,Namespace);Assert.Equal(retained,await File.ReadAllBytesAsync(hand));
            Assert.Equal(before,JsonSerializer.Serialize(catalog));Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
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
