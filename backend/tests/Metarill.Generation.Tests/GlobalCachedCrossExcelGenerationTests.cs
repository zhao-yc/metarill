using System.Globalization;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Testing;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>执行全局受管缓存名称的真实生成Excel解析与导出；传输受控，不冒充MySQL或发布验收。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalCachedCrossExcelGenerationTests
{
    private const string Namespace="Example.GlobalCachedCrossExcel";
    private const long Country=9007199254742011,Area=9007199254742021,Place=9007199254742031,Warehouse=9007199254742041;
    /// <summary>各级名称及共享/短级联沿同一生成器往返，原导入导出开关和公开门槛均保留。</summary>
    [Theory]
    [InlineData(0,"string","string",false,false,false)]
    [InlineData(1,"string","int64",false,false,false)]
    [InlineData(2,"int64","string",false,false,false)]
    [InlineData(0,"int64","int64",true,false,false)]
    [InlineData(1,"string","int64",false,true,false)]
    [InlineData(1,"int64","string",false,true,false)]
    [InlineData(1,"string","string",false,true,true)]
    [InlineData(1,"int64","int64",false,true,true)]
    public async Task Generated_global_cached_names_roundtrip_original_paths(int role,string key,string parent,bool allGlobal,bool shared,bool shorter)
    {
        var catalog=shared?GlobalCrossCachedTargetFixture.CreateShared(1,key,parent,shorter):GlobalCrossCachedTargetFixture.Create(1,role,key,parent,allGlobal);
        var before=JsonSerializer.Serialize(catalog);var proof=CrossTableExcelApplicationPreparer.RequireCurrent(catalog,Namespace);Assert.NotEmpty(proof.Gaps);
        Assert.All(proof.Gaps,gap=>Assert.Contains(gap.Reason,new[]{CrossTableExcelRenderer.ImportAcceptancePending,CrossTableExcelRenderer.ExportAcceptancePending}));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-cached-cross-excel");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var generated=Path.Combine(directory,"generated");
        try
        {
            new CSharpGenerator().Generate(catalog,generated,Namespace);
            // 此独占工程把生成源码警告升级为错误，不改变共享编译器或原产品模板。
            await File.WriteAllTextAsync(Path.Combine(directory,"Directory.Build.props"),"<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var hand=Path.Combine(generated,"Services/CrossConsumerService.cs");await File.AppendAllTextAsync(hand,"\n// 本次Excel验证的手写业务保持原样。\n");var retained=await File.ReadAllBytesAsync(hand);new CSharpGenerator().Generate(catalog,generated,Namespace);Assert.Equal(retained,await File.ReadAllBytesAsync(hand));
            using var runtime=new SharedExcelReadTransport(assembly,Namespace,"CrossConsumer",pageId:"30",actor:new RuntimeActor(901,"全局缓存Excel",false,"100","9"));
            var levels=GlobalCrossCachedTargetFixture.Levels(catalog);var names=new[]{"国甲","区甲","城甲"};var ids=new[]{Country,Area,Place};
            for(var index=0;index<levels.Length;index++)Seed(index,ids[index],names[index],index==0?null:ids[index-1]);
            // 状态失效和墓碑同名不得污染唯一性；不同父级同名只能在精确父范围内查找。
            Seed(0,Country+100,"国甲",null,status:0);Seed(0,Country+101,"国甲",null,deleted:true);Seed(0,Country+1,"国乙",null);
            Seed(1,Area+1,"区甲",Country+1);
            if(shared)runtime.Add("CrossWarehouse",new Dictionary<string,object?>{{"Id",Warehouse},{"YzProjectId",100L},{"Status",1},{"Name","仓甲"},{"AreaId",Area}});
            var full=shorter?"国甲/区甲":"国甲/区甲/城甲";var root=shorter?"国甲/":"国甲//";var middle=shorter?full:"国甲/区甲/";
            var complete=await Parse(full,shared?"国甲/区甲/仓甲":null);AssertSlots(complete,Country,Area,shorter?null:Place,shared?Warehouse:null);
            AssertSlots(await Parse(root,shared?"国甲//":null),Country,null,null,null);
            AssertSlots(await Parse(middle,shared?"国甲/区甲/":null),Country,Area,null,null);
            if(shared)
            {
                foreach(var reverse in new[]{false,true})
                {
                    var ordered=new[]{new KeyValuePair<string,object?>("区域",full),new("仓库","国甲/区甲/仓甲")};
                    var values=await runtime.Parse((reverse?ordered.Reverse():ordered).ToDictionary(pair=>pair.Key,pair=>pair.Value));AssertSlots(values,Country,Area,shorter?null:Place,Warehouse);
                    var conflict=new[]{new KeyValuePair<string,object?>("区域",root),new("仓库","国甲/区甲/仓甲")};
                    await Assert.ThrowsAsync<PageImportException>(()=>runtime.Parse((reverse?conflict.Reverse():conflict).ToDictionary(pair=>pair.Key,pair=>pair.Value)));
                }
                var blankPrimary=await runtime.Parse(new Dictionary<string,object?>{{"区域",""},{"仓库","国甲/区甲/仓甲"}});AssertSlots(blankPrimary,Country,Area,null,Warehouse);
            }
            await Assert.ThrowsAsync<PageImportException>(()=>Parse(shorter?"国甲/不存在":"国甲/不存在/城甲",shared?"":null));
            await Assert.ThrowsAsync<PageImportException>(()=>Parse("区甲",shared?"":null));
            Source(1,"完整",Country,Area,shorter?null:Place,shared?Warehouse:null);
            Source(2,"根级",Country,null,null,null);Source(3,"中级",Country,Area,null,null);Source(4,"空值",null,null,null,null);
            // 相同业务库中的其他项目来源记录不得进入当前页导出。
            Source(5,"其它项目",Country,Area,shorter?null:Place,shared?Warehouse:null,101);
            var locks=runtime.Reads.Count(value=>value.StartsWith("lock:",StringComparison.Ordinal));var exported=await runtime.Export();
            var nameIndex=exported.Headers.ToList().IndexOf("名称");var mainIndex=exported.Headers.ToList().IndexOf("区域");var warehouseIndex=exported.Headers.ToList().IndexOf("仓库");Assert.True(nameIndex>=0&&mainIndex>=0);if(shared)Assert.True(warehouseIndex>=0);
            var cells=exported.Rows.ToDictionary(row=>row[nameIndex]!.ToString()!,row=>(Main:row[mainIndex]?.ToString(),Second:shared?row[warehouseIndex]?.ToString():null));
            Assert.Equal(4,cells.Count);Assert.False(cells.ContainsKey("其它项目"));Assert.Equal((full,shared?"国甲/区甲/仓甲":null),cells["完整"]);Assert.Equal((root,shared?"国甲//":null),cells["根级"]);Assert.Equal((middle,shared?"国甲/区甲/":null),cells["中级"]);Assert.Equal(((string?)null,(string?)null),cells["空值"]);
            foreach(var cell in cells){var values=await Parse(cell.Value.Main,cell.Value.Second);if(cell.Key=="完整")AssertSlots(values,Country,Area,shorter?null:Place,shared?Warehouse:null);else if(cell.Key=="根级")AssertSlots(values,Country,null,null,null);else if(cell.Key=="中级")AssertSlots(values,Country,Area,null,null);else AssertSlots(values,null,null,null,null);}
            Assert.Equal(1,runtime.Snapshots);Assert.Equal(locks,runtime.ExportLockCount);Assert.Empty(runtime.Transport.Commands);Assert.Contains("scope:status",runtime.Reads);
            // 受管缓存物理列重名也必须拒绝，不能借上游字典名称、排序或旧缓存结果猜选。
            var cachedIndex=shared?1:role;Seed(cachedIndex,ids[cachedIndex]+200,names[cachedIndex],cachedIndex==0?null:ids[cachedIndex-1]);
            await Assert.ThrowsAsync<PageImportException>(()=>Parse(full,shared?"国甲/区甲/仓甲":null));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>runtime.Export());
            Assert.Equal(before,JsonSerializer.Serialize(catalog));Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
            return;
            Task<Dictionary<string,JsonElement>> Parse(string? main,string? second){var row=new Dictionary<string,object?>{{"区域",main}};if(shared)row["仓库"]=second;return runtime.Parse(row);}
            void Seed(int index,long id,string text,long? parentId,int status=1,bool deleted=false)
            {
                var level=levels[index];var table=catalog.ConfigTables.Single(table=>table.TableName==level.GetProperty("table").GetString());var fields=new Dictionary<string,object?>{{"Id",id},{"Status",status},{GenerationModel.Pascal(level.GetProperty("nameField").GetString()!),text}};
                if(catalog.ConfigColumns.Any(column=>column.ConfigTableId==table.Id&&column.ColumnName=="yz_project_id"))fields["YzProjectId"]=100L;if(deleted)fields["DeleteNo"]=DateTime.UtcNow;
                if(index>0)fields[GenerationModel.Pascal(level.GetProperty("parentField").GetString()!)]=parentId;runtime.Add(GenerationModel.Pascal(table.TableName),fields);
            }
            void Source(long id,string text,long? country,long? area,long? place,long? warehouse,long project=100)
            {
                var fields=new Dictionary<string,object?>{{"Id",id},{"Name",text},{"YzProjectId",project},{"CountryId",country},{"AreaId",area}};if(!shorter)fields["PlaceId"]=place;if(shared)fields["WarehouseId"]=warehouse;runtime.Add("CrossConsumer",fields);
            }
            void AssertSlots(Dictionary<string,JsonElement> values,long? country,long? area,long? place,long? warehouse)
            {
                foreach(var(field,value)in new[]{("countryId",country),("areaId",area),("placeId",place),("warehouseId",warehouse)})
                {if(field=="placeId"&&shorter||field=="warehouseId"&&!shared)continue;Assert.True(values.TryGetValue(field,out var cell),"缺少原级联字段："+field);Assert.Equal(value?.ToString(CultureInfo.InvariantCulture),cell.ValueKind==JsonValueKind.Null?null:cell.ToString());}
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
