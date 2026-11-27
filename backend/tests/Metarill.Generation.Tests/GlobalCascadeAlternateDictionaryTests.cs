using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>跨表级联层位读取的显示名称继续使用该目标页所绑定的下拉字典。</summary>
public sealed class GlobalCascadeAlternateDictionaryTests
{
    /// <summary>全局 R0 级联节点从自己的实际业务页绑定字典，其他页面可以有独立范围。</summary>
    [Theory]
    [InlineData(false,1,false)]
    [InlineData(true,1,false)]
    [InlineData(false,0,false)]
    [InlineData(true,0,false)]
    [InlineData(false,0,true)]
    [InlineData(true,0,true)]
    [InlineData(false,1,false,true)]
    [InlineData(true,1,false,true)]
    [InlineData(true,1,false,true,true)]
    [InlineData(true,0,true,true,true)]
    public async Task Global_cascade_label_uses_the_published_dictionary_page(bool cachedSource,int dictionaryStorage,bool secondHop,
        bool localNode=false,bool parentScope=false)
    {
        var catalog=cachedSource?GlobalCrossCachedFreshCatalogTests.Catalog("cross3"):GlobalCrossFreshCatalogTests.Catalog("cross3");
        var sourcePage=catalog.ConfigPages.Single(page=>page.Id=="201");
        sourcePage.IsExport=1;
        sourcePage.IsImport=1;
        var root=catalog.ConfigTables.Single(table=>table.Id=="3");
        var rootPage=catalog.ConfigPages.Single(page=>page.Id=="301");
        root.IsRongyu=0;
        var name=catalog.ConfigColumns.Single(column=>column.Id=="32");
        name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("dictionary.name");
        catalog.ConfigPageColumns.Single(column=>column.ConfigPageId==rootPage.Id&&column.ConfigColumnId==name.Id).IsEdit=0;
        catalog.ConfigTables.Add(new(){Id="6",TableName="nr_global_dictionary",Name="全局字典",YzProjectId=catalog.YzProjectId,
            IsRongyu=dictionaryStorage,Extra=new(){["scopeKind"]=JsonSerializer.SerializeToElement("global")}});
        AddColumn("61","6","id","int64").IsDbNotNull=1;
        AddColumn("62","6","name","string");
        AddColumn("63","6","status","int32");
        AddColumn("65","6","segment","int32");
        var foreign=AddColumn("38","3","dictionary_id","int64");
        foreign.LinkConfigTableId="6";
        foreign.ControlKind="List";
        foreign.ControlDetail=JsonSerializer.Serialize(new{tableId="6",pageId="601",nameField="name"});
        AddPage("601","6","status=17");
        AddPage("602","6","status=3");
        AddField("60162","601","62");
        AddField("60262","602","62");
        AddField("30138","301","38").Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"pageId":"602","whereSql":"segment=9"}""");
        AddPage("302","3","status=1");
        AddField("30238","302","38");
        if(localNode)
        {
            // 项目内层位从实际候选页读取全局字典，保留来源项目及字典门店。
            root.Extra.Remove("scopeKind");
            if(!catalog.ConfigColumns.Any(column=>column.ConfigTableId=="3"&&column.ColumnName=="yz_project_id"))
                AddColumn("333","3","yz_project_id","int64");
            foreach(var nodePage in catalog.ConfigPages.Where(page=>page.ConfigTableId=="3"))
                nodePage.WhereSql+=" AND yz_project_id=@current_yz_project_id";
            AddColumn("66","6","store_id","int64");
            AddColumn("39","3","dictionary_group","int32");
            AddField("30139","301","39");
            catalog.ConfigPages.Single(page=>page.Id=="602").WhereSql+=" AND store_id=@current_store_id";
            catalog.ConfigPageColumns.Single(field=>field.Id=="30138").Extra["controlDetail"]=
                JsonSerializer.SerializeToElement("""{"pageId":"602","whereSql":"segment=@formParameters.dictionaryGroup"}""");
            if(cachedSource)
            {
                AddColumn("299","2","store_id","int64");
                sourcePage.WhereSql+=" AND store_id=@current_store_id";
            }
        }
        if(secondHop)
        {
            var dictionaryName=catalog.ConfigColumns.Single(column=>column.Id=="62");
            dictionaryName.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
            dictionaryName.Extra["columnFullName"]=JsonSerializer.SerializeToElement("leaf.name");
            catalog.ConfigPageColumns.Single(column=>column.Id=="60162").IsEdit=0;
            catalog.ConfigPageColumns.Single(column=>column.Id=="60262").IsEdit=0;
            catalog.ConfigTables.Add(new(){Id="7",TableName="nr_global_leaf",Name="末段字典",YzProjectId=catalog.YzProjectId,
                IsRongyu=1,Extra=new(){["scopeKind"]=JsonSerializer.SerializeToElement("global")}});
            AddColumn("71","7","id","int64").IsDbNotNull=1;
            AddColumn("72","7","name","string");
            AddColumn("73","7","status","int32");
            var leaf=AddColumn("64","6","leaf_id","int64");
            leaf.LinkConfigTableId="7";
            leaf.ControlKind="List";
            leaf.ControlDetail=JsonSerializer.Serialize(new{tableId="7",pageId="701",nameField="name"});
            AddPage("701","7","status=19");
            AddPage("702","7","status=5");
            AddField("70172","701","72");
            AddField("70272","702","72");
            AddField("60164","601","64");
            AddField("60264","602","64").Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"pageId":"702"}""");
        }

        if(parentScope)
        {
            sourcePage.WhereSql+=" AND country_id=@parent_id";
            Assert.Contains(catalog.ConfigColumns,column=>column.ConfigTableId=="2"&&column.ControlKind=="FileLibrary");
            var parent=Assert.Single(Metarill.Data.Queries.RedundantSharedParents.Compile(catalog,"2",sourcePage.Id)).Value;
            Assert.Equal("country_id",parent.ColumnName);
            Assert.Equal(rootPage.Id,Assert.Single(parent.Levels).PageId);
            var files=new Metarill.Runtime.Files.FileMetadataContext([catalog]);
            var published=Assert.Single(files.Catalogs);
            var filePage=published.ConfigPages.Single(item=>item.Id==sourcePage.Id);
            var scope=typeof(Metarill.Runtime.Files.FileMetadataContext).GetMethod("PageScope",
                System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            Assert.NotNull(scope.Invoke(files,[published,filePage]));
        }

        var model=GenerationModel.Create(catalog,"Example.GlobalCascadeDictionary");
        var owner=model.Tables.Single(table=>table.Source.Id=="2");
        var source=owner.Columns.Single(column=>column.Source.Id=="25");
        var resolved=CrossTableReadCompilation.Resolve(model,owner,Assert.Single(owner.Pages),source);
        Assert.True(resolved.Definition is not null,resolved.Reason);
        if(!cachedSource)
            _=NonRedundantReadCompilation.ForPage(model,owner,Assert.Single(owner.Pages));
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-cascade-dictionary");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);
            var application=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=application.GetType("Example.GlobalCascadeDictionary.Generated.Services.NrGlobalSourceService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            const System.Reflection.BindingFlags instance=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            const System.Reflection.BindingFlags plans=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
            Assert.NotNull(service.GetMethod("CrossExcelFind201_25Async",instance));
            AssertDictionaryScope(ReadPlan("CrossTargetPlan201_25_0"));
            if(cachedSource)
            {
                var maintenance=application.GetType("Example.GlobalCascadeDictionary.Generated.GeneratedRelationMaintenance",true)!
                    .GetProperty("Catalog")!.GetValue(null)!;
                var tables=Assert.IsAssignableFrom<System.Collections.IDictionary>(
                    maintenance.GetType().GetProperty("Plans",instance)!.GetValue(maintenance));
                var table=tables[owner.Source.Id]!;
                var refresh=table.GetType().GetProperty("Read")!.GetValue(table)!;
                AssertDictionaryScope(refresh);
                var expression=Assert.IsType<string>(refresh.GetType().GetMethod("Expression",instance)!.Invoke(refresh,["country_name"]));
                Assert.Contains("CASE WHEN",expression);
                Assert.Contains(".`segment`",expression);
                if(localNode)
                {
                    Assert.Contains(".`dictionary_group`",expression);
                    Assert.Contains("b.`store_id`",expression);
                }
                var from=Assert.IsType<string>(refresh.GetType().GetProperty("From",instance)!.GetValue(refresh));
                Assert.DoesNotContain(".`segment`",from);
                var reverse=Assert.IsAssignableFrom<System.Collections.IEnumerable>(
                    refresh.GetType().GetProperty("Reverse",instance)!.GetValue(refresh)).Cast<object>().ToArray();
                AssertDictionaryReverse(reverse,"nr_global_dictionary");
                if(secondHop)AssertDictionaryReverse(reverse,"nr_global_leaf");
            }
            else
            {
                AssertDictionaryScope(ReadPlan("ReadPlan201"));
                AssertDictionaryScope(ReadPlan("ExportReadPlan201"));
            }

            object ReadPlan(string name)
            {
                var read=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(service.GetField(name,plans)!.GetValue(null));
                if(localNode)
                {
                    Assert.True(read.RequiresAccount);
                    var actor=new Metarill.Core.Security.RuntimeActor(8,"级联字典",false,catalog.YzProjectId,sourcePage.YzAdminTypeId);
                    Assert.Throws<UnauthorizedAccessException>(()=>read.BindAccount(actor));
                    read=read.BindAccount(actor with{StoreId=9007199254740993L});
                    var bound=read.GetType().GetProperty("MySql",instance)!.GetValue(read)!;
                    var constants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                        bound.GetType().GetProperty("Constants",instance)!.GetValue(bound));
                    Assert.Contains(9007199254740993L,constants.Values);
                }
                return read.GetType().GetProperty("MySql",instance)!.GetValue(read)!;
            }
            void AssertDictionaryScope(object read)
            {
                var constants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                    read.GetType().GetProperty("Constants",instance)!.GetValue(read));
                Assert.Contains(3,constants.Values);
                if(!localNode)Assert.Contains(9,constants.Values);
                Assert.DoesNotContain(17,constants.Values);
                Assert.DoesNotContain(19,constants.Values);
                if(secondHop)Assert.Contains(5,constants.Values);
            }
            void AssertDictionaryReverse(IEnumerable<object> reverse,string table)
            {
                Assert.Contains(reverse,item=>
                    Equals(table,item.GetType().GetProperty("TargetTable")!.GetValue(item))&&
                    Assert.IsAssignableFrom<IReadOnlyDictionary<string,IReadOnlySet<string>>>(
                        item.GetType().GetProperty("Outputs")!.GetValue(item))
                        .TryGetValue("country_name",out var fields)&&fields.Contains("status")&& (table!="nr_global_dictionary"||fields.Contains("segment")));
            }
        }
        finally
        {
            if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }

        ColumnMetadata AddColumn(string id,string table,string column,string type)
        {
            var item=new ColumnMetadata{Id=id,ConfigTableId=table,ColumnName=column,LogicalType=type,YzProjectId=catalog.YzProjectId};
            catalog.ConfigColumns.Add(item);
            return item;
        }
        void AddPage(string id,string table,string where)=>catalog.ConfigPages.Add(new(){Id=id,ConfigTableId=table,
            YzProjectId=catalog.YzProjectId,YzAdminTypeId=rootPage.YzAdminTypeId,WhereSql=where});
        PageColumnMetadata AddField(string id,string page,string column)
        {
            var field=new PageColumnMetadata{Id=id,ConfigPageId=page,ConfigColumnId=column,
                YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=1};
            catalog.ConfigPageColumns.Add(field);
            return field;
        }
    }
}
