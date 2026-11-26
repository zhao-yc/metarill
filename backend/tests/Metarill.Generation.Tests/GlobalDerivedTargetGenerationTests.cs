using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>全局R0逻辑名称必须闭合原候选、生成、维护与完整包，而不是只在数据层放宽目标。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalDerivedTargetGenerationTests
{
    /// <summary>两种消费者与内外两种外键共用原完整元数据，保留原导入导出及文件控件。</summary>
    [Theory]
    [InlineData(0,"int64","int64")][InlineData(0,"string","int64")]
    [InlineData(0,"int64","string")][InlineData(0,"string","string")]
    [InlineData(1,"int64","int64")][InlineData(1,"string","int64")]
    [InlineData(1,"int64","string")][InlineData(1,"string","string")]
    public void Global_r0_name_forms_a_complete_generation_model(int mode,string outer,string inner)
    {
        var c=Catalog(mode,outer,inner);var before=JsonSerializer.Serialize(c);
        var model=GenerationModel.Create(c,"Example.GlobalDerivedTarget");
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var maintenance=MixedRelationPropagationCompilation.ForModel(model);
        Assert.DoesNotContain("global_source",maintenance.MixedSourceColumns.Keys);
        if(mode==1){Assert.Contains("global_source",maintenance.MixedTargets);Assert.Contains("global_target",maintenance.MixedTargets);Assert.Equal(new[]{"global_source_global_target_name","selected_global_source_global_target_name"},maintenance.MixedSourceColumns["cached_consumer"].Order(StringComparer.Ordinal));}
        else Assert.DoesNotContain("cached_consumer",maintenance.MixedSourceColumns.Keys);
        var producer=model.Tables.Single(t=>t.Source.Id=="1");Assert.Contains(producer.Pages[0].Readable,f=>f.Source.Id=="14");Assert.DoesNotContain(producer.Pages[0].Writable,f=>f.Source.Id=="14");
        Assert.All(c.ConfigPages,p=>{Assert.Equal(1,p.IsImport);Assert.Equal(1,p.IsExport);});
        Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>全局字典的已发布联接字段也可用于原下拉附加条件。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Global_list_condition_uses_a_published_joined_field(int sourceStorage)
    {
        const string ns="Example.GlobalJoinedListCondition";
        var catalog=Catalog(sourceStorage,"int64","int64");
        catalog.ConfigColumns.AddRange([
            new(){Id="25",ConfigTableId="2",YzProjectId="100",ColumnName="segment",LogicalType="int32"},
            new(){Id="16",ConfigTableId="1",YzProjectId="100",ColumnName="terminal_segment",LogicalType="int32",Extra={
                ["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
                ["columnFullName"]=JsonSerializer.SerializeToElement("global_target.segment")}}
        ]);
        var selector=catalog.ConfigColumns.Single(column=>column.Id=="34");
        selector.ControlDetail=JsonSerializer.Serialize(new{tableId="1",pageId="10",
            nameField="global_target_name",whereSql="terminal_segment <> 17"});
        var model=GenerationModel.Create(catalog,ns);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var table=model.Tables.Single(item=>item.Source.Id=="3");
        var relation=Assert.Single(RelationCompilation.ForPage(model,Assert.Single(table.Pages),true,forExcel:true),
            item=>item.SourceColumn.Source.Id=="34");
        Assert.Contains("terminal_segment",relation.Conditions!.JoinedFields);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-list-joined-condition");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,generated,ns);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.CachedConsumerService",true)!;
            const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.NonPublic;
            const string cacheName="global_source_global_target_name";
            var actor=new RuntimeActor(9,"全局联接范围",false,"100","9");
            object Plan(string member)
            {
                var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                    service.GetField(member,hidden|System.Reflection.BindingFlags.Static)!.GetValue(null));
                return plan.GetType().GetProperty("MySql",hidden|System.Reflection.BindingFlags.Instance)!
                    .GetValue(plan.BindAccount(actor))!;
            }
            string Expression(object read,string field)=>Assert.IsType<string>(read.GetType()
                .GetMethod("Expression",hidden|System.Reflection.BindingFlags.Instance)!.Invoke(read,[field]));
            string From(object read)=>Assert.IsType<string>(read.GetType()
                .GetProperty("From",hidden|System.Reflection.BindingFlags.Instance)!.GetValue(read));
            IReadOnlyDictionary<string,object?> Constants(object read)=>
                Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(read.GetType()
                    .GetProperty("Constants",hidden|System.Reflection.BindingFlags.Instance)!.GetValue(read));

            var options=Plan("DerivedOptionsPlan30_34");
            Assert.Contains("`global_target`",From(options));
            Assert.Contains(".`segment`",Expression(options,"terminal_segment"));

            var code=await File.ReadAllTextAsync(Path.Combine(generated,"Services/CachedConsumerService.g.cs"));
            Assert.Contains("OptionsCondition30_34.BindVariables",code);
            Assert.Contains("ImportRelation30_34Async",code);
            Assert.Contains("currentRead:true",code);
            Assert.NotNull(service.GetMethod("Import30Async"));
            Assert.NotNull(service.GetMethod("Export30Async"));
            if(sourceStorage==0)
            {
                foreach(var member in new[]{"ReadPlan30","ExportReadPlan30"})
                {
                    var read=Plan(member);
                    var sql=From(read)+Expression(read,cacheName);
                    Assert.Contains(".`segment`",sql);
                    Assert.Contains(Constants(read),pair=>Equals(pair.Value,17)&&sql.Contains(pair.Key,StringComparison.Ordinal));
                }
            }
            else
            {
                var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                    assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null));
                var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(maintenance.GetType()
                    .GetProperty("Plans",hidden|System.Reflection.BindingFlags.Instance)!.GetValue(maintenance));
                var tablePlan=plans["3"]!;
                var read=tablePlan.GetType().GetProperty("Read")!.GetValue(tablePlan)!;
                var sql=Expression(read,cacheName);
                Assert.Contains(".`segment`",sql);
                Assert.Contains(Constants(read),pair=>Equals(pair.Value,17)&&sql.Contains(pair.Key,StringComparison.Ordinal));
                var reverse=Assert.IsAssignableFrom<System.Collections.IEnumerable>(read.GetType()
                    .GetProperty("Reverse",hidden|System.Reflection.BindingFlags.Instance)!.GetValue(read));
                Assert.Contains(reverse.Cast<object>(),item=>
                    Equals("global_target",item.GetType().GetProperty("TargetTable")!.GetValue(item))&&
                    Assert.IsAssignableFrom<IReadOnlyDictionary<string,IReadOnlySet<string>>>(item.GetType()
                        .GetProperty("Outputs")!.GetValue(item)).TryGetValue(cacheName,out var fields)&&fields.Contains("segment"));
            }
            var app=Path.Combine(directory,"app");
            Assert.True(new ApplicationPackager().Create(catalog,app,ns,NonRedundantApplicationFixture.Root()).FrontendIncluded);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(app,operation);
        }
        finally { if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>实时派生名称、源列表及选择回读沿全局中间页使用同一实际下拉范围。</summary>
    [Theory][InlineData("constant")][InlineData("form")][InlineData("account")][InlineData("rowProject")]
    [InlineData("alternatePage")][InlineData("storedAlternatePage")]
    public async Task Global_R0_live_name_follows_the_actual_intermediate_control(string mode)
    {
        var catalog=Catalog(0,"int64","int64");
        var alternatePage=mode is "alternatePage" or "storedAlternatePage";
        catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=mode=="storedAlternatePage"?1:0;
        Add("25","2","segment","int32");
        var selector=catalog.ConfigColumns.Single(column=>column.Id=="13");
        selector.ControlDetail=JsonSerializer.Serialize(new{tableId="2",pageId="20",nameField="name",whereSql="segment=17"});
        var condition=mode switch
        {
            "form"=>"segment IN (@formParameters.globalSource.lookupSegment,3)",
            "account"=>"store_id=@current_store_id AND segment=3",
            "rowProject"=>"yz_project_id=@current_yz_project_id AND segment=3",
            _=>"segment=3"
        };
        catalog.ConfigPageColumns.Single(field=>field.Id=="1013").Extra["controlDetail"]=
            JsonSerializer.SerializeToElement(JsonSerializer.Serialize(new{whereSql=condition}));
        if(mode=="form")
        {
            Add("26","1","lookup_segment","int64");
            catalog.ConfigPageColumns.Add(new(){Id="1026",ConfigPageId="10",ConfigColumnId="26",YzProjectId="100",IsShow=1,IsEdit=1});
        }
        if(mode=="account")Add("27","2","store_id","int64");
        if(mode=="rowProject")
        {
            Add("28","1","yz_project_id","int64");
            Add("29","2","yz_project_id","string");
        }

        if(alternatePage)
        {
            // 中间页 10 选择字典页 21，另一发布页 11 保留字典页 20。
            selector.ControlDetail=JsonSerializer.Serialize(new{tableId="2",pageId="20",nameField="name"});
            catalog.ConfigPages.Single(page=>page.Id=="20").WhereSql="status=1 AND segment=17";
            catalog.ConfigPages.Add(new(){Id="21",ConfigTableId="2",YzProjectId="100",YzAdminTypeId="9",
                WhereSql="status=1 AND segment=3"});
            catalog.ConfigPageColumns.Add(new(){Id="2122",ConfigPageId="21",ConfigColumnId="22",YzProjectId="100",IsShow=1,IsEdit=1});
            catalog.ConfigPageColumns.Single(field=>field.Id=="1013").Extra["controlDetail"]=
                JsonSerializer.SerializeToElement("""{"pageId":"21"}""");
            catalog.ConfigPages.Add(new(){Id="11",ConfigTableId="1",YzProjectId="100",YzAdminTypeId="9",WhereSql="status=1"});
            catalog.ConfigPageColumns.Add(new(){Id="1113",ConfigPageId="11",ConfigColumnId="13",YzProjectId="100",IsShow=1,IsEdit=1});
            catalog.ConfigPageColumns.Add(new(){Id="1114",ConfigPageId="11",ConfigColumnId="14",YzProjectId="100",IsShow=1});
        }

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-live-control");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.GlobalLiveControl");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var actor=new RuntimeActor(9,"范围测试",false,"100","9"){StoreId=6351};
            var consumer=assembly.GetType("Example.GlobalLiveControl.Generated.Services.CachedConsumerService",true)!;
            var intermediate=assembly.GetType("Example.GlobalLiveControl.Generated.Services.GlobalSourceService",true)!;
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.NonPublic;
            var output=new List<(Type Owner,string Plan,string Field)>{
                (consumer,"ReadPlan30","global_source_global_target_name"),
                (consumer,"ExportReadPlan30","global_source_global_target_name"),
                (consumer,"DerivedOptionsPlan30_34","global_target_name"),
                (consumer,"DerivedOptionsPlan30_38","global_target_name"),
                (intermediate,"ReadPlan10","global_target_name")};
            if(mode=="rowProject")output.Add((intermediate,"CandidateReadPlan10","global_target_name"));
            if(alternatePage)
            {
                output.Add((consumer,"ReadPlan30","selected_global_source_global_target_name"));
                output.Add((consumer,"ExportReadPlan30","selected_global_source_global_target_name"));
                output.Add((intermediate,"ReadPlan11","global_target_name"));
                Assert.NotNull(consumer.GetMethod("Import30Async"));
                Assert.NotNull(consumer.GetMethod("Export30Async"));
            }
            foreach(var (owner,plan,field) in output)
            {
                var current=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                    owner.GetField(plan,flags|System.Reflection.BindingFlags.Static)!.GetValue(null)).BindAccount(actor);
                var mysql=current.GetType().GetProperty("MySql",flags|System.Reflection.BindingFlags.Instance)!.GetValue(current)!;
                var expression=Assert.IsType<string>(mysql.GetType().GetMethod("Expression",flags|System.Reflection.BindingFlags.Instance)!.Invoke(mysql,[field]));
                var scope=expression;
                if(alternatePage)
                {
                    // 同一业务页包含两个字典角色，只核对当前名称表达式使用的真实末端联接。
                    var label=Assert.Single(System.Text.RegularExpressions.Regex.Matches(expression,@"([a-zA-Z_]\w*)\.`name`"));
                    var prefix="`global_target` "+label.Groups[1].Value+" ON ";
                    var from=Assert.IsType<string>(mysql.GetType().GetProperty("From",flags|System.Reflection.BindingFlags.Instance)!.GetValue(mysql));
                    scope+=Assert.Single(from.Split(" LEFT JOIN ",StringSplitOptions.None),join=>join.StartsWith(prefix,StringComparison.Ordinal));
                }
                else
                {
                    Assert.True(Assert.IsType<bool>(mysql.GetType().GetProperty("RequiresPublishedProjectIdentity",flags|System.Reflection.BindingFlags.Instance)!.GetValue(mysql)));
                    Assert.Contains("EXISTS (SELECT 1 FROM `global_target`",expression);
                }
                Assert.Contains(".`segment`",scope);
                Assert.Contains(".`status`",scope);
                var constants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                    mysql.GetType().GetProperty("Constants",flags|System.Reflection.BindingFlags.Instance)!.GetValue(mysql));
                var values=constants.Values;
                var expected=plan=="ReadPlan11"?17:3;
                var excluded=plan=="ReadPlan11"?3:17;
                Assert.Contains(expected,values);
                Assert.DoesNotContain(excluded,values);
                if(alternatePage)
                    Assert.Contains(constants,parameter=>Equals(parameter.Value,expected)&&scope.Contains(parameter.Key,StringComparison.Ordinal));
                if(mode=="form")Assert.Contains((owner==consumer&&plan.StartsWith("Read",StringComparison.Ordinal)||plan.StartsWith("Export",StringComparison.Ordinal)
                    ?"r0":"b")+".`lookup_segment`",expression);
                if(mode=="account")Assert.Contains(6351L,values);
                if(mode=="rowProject")Assert.Contains(".`yz_project_id`=@project_scope_text",expression);
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}

        void Add(string id,string table,string name,string type)=>catalog.ConfigColumns.Add(new()
            {Id=id,ConfigTableId=table,ColumnName=name,LogicalType=type,YzProjectId="100"});
    }

    /// <summary>外层控件没有附加条件时，仍须在维护目录中保存实际中间页的名称范围。</summary>
    [Theory][InlineData("constant",false)][InlineData("form",false)][InlineData("account",false)]
    [InlineData("constant",true)][InlineData("account",true)][InlineData("deep",false)]
    [InlineData("alternatePage",false)][InlineData("storedAlternatePage",false)]
    public async Task Global_R1_name_cache_keeps_downstream_only_control_scope(string mode,bool globalConsumer)
    {
        var catalog=Catalog(1,"int64","int64");
        var owner=catalog.ConfigTables.Single(table=>table.Id=="3");
        if(globalConsumer)owner.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        var alternatePage=mode is "alternatePage" or "storedAlternatePage";
        catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=mode=="storedAlternatePage"?1:0;
        Add("25","2","segment","int32");
        var definition=mode switch
        {
            "form"=>"segment IN (@formParameters.globalSource.lookupSegment,3)",
            "account"=>"store_id=@current_store_id AND segment=3",
            _=>"segment=3"
        };
        if(mode=="form")
        {
            Add("26","1","lookup_segment","int64");
            catalog.ConfigPageColumns.Add(new(){Id="1026",ConfigPageId="10",ConfigColumnId="26",YzProjectId="100",IsShow=1,IsEdit=1});
        }
        if(mode=="account")
        {
            Add("27","2","store_id","int64");
            Add("28","3","store_id","int64");
            catalog.ConfigPages.Single(page=>page.Id=="30").WhereSql+=" AND store_id=@current_store_id";
        }

        var relation=catalog.ConfigColumns.Single(column=>column.Id=="13");
        relation.ControlDetail=JsonSerializer.Serialize(new{tableId="2",pageId="20",nameField="name",whereSql="segment=17"});
        if(alternatePage)
        {
            // 实际中间页改用同一字典的发布页，只有这个页面定义了 segment 限制。
            relation.ControlDetail=JsonSerializer.Serialize(new{tableId="2",pageId="20",nameField="name"});
            catalog.ConfigPages.Add(new(){Id="21",ConfigTableId="2",YzProjectId="100",YzAdminTypeId="9",
                WhereSql="status=1 AND segment=3"});
            catalog.ConfigPageColumns.Add(new(){Id="2122",ConfigPageId="21",ConfigColumnId="22",YzProjectId="100",IsShow=1,IsEdit=1});
            catalog.ConfigPageColumns.Single(field=>field.Id=="1013").Extra["controlDetail"]=
                JsonSerializer.SerializeToElement("""{"pageId":"21"}""");
        }
        else if(mode!="deep")
            catalog.ConfigPageColumns.Single(field=>field.Id=="1013").Extra["controlDetail"]=Overlay(definition);
        else
        {
            // 只有第三段有附加条件，前两层的实际页仍须随副本发布。
            relation.ControlDetail=JsonSerializer.Serialize(new{tableId="2",pageId="20",nameField="name"});
            var name=catalog.ConfigColumns.Single(column=>column.Id=="22");
            name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
            name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("leaf.name");
            catalog.ConfigPageColumns.Single(field=>field.Id=="2022").IsEdit=0;
            catalog.ConfigTables.Add(new(){Id="4",TableName="global_leaf",Name="末端字典",YzProjectId="100",IsRongyu=0,
                Extra=new(){["scopeKind"]=JsonSerializer.SerializeToElement("global")}});
            Add("41","4","id","int64");
            catalog.ConfigColumns.Single(column=>column.Id=="41").IsDbNotNull=1;
            Add("42","4","name","string");
            Add("43","4","delete_no","datetime");
            Add("44","4","segment","int32");
            catalog.ConfigPages.Add(new(){Id="40",ConfigTableId="4",Name="末端字典",YzProjectId="100",YzAdminTypeId="9"});
            catalog.ConfigPageColumns.Add(new(){Id="4042",ConfigPageId="40",ConfigColumnId="42",YzProjectId="100",IsShow=1,IsEdit=1});
            Add("29","2","leaf_id","int64");
            var leaf=catalog.ConfigColumns.Single(column=>column.Id=="29");
            leaf.ControlKind="List";
            leaf.LinkConfigTableId="4";
            leaf.ControlDetail=JsonSerializer.Serialize(new{tableId="4",pageId="40",nameField="name",whereSql="segment=3"});
            catalog.ConfigPageColumns.Add(new(){Id="2029",ConfigPageId="20",ConfigColumnId="29",YzProjectId="100",IsShow=1,IsEdit=1});
        }
        // 同一中间表的另一页可单独限定候选，外层List及单选均明确引用实际页10。
        catalog.ConfigPages.Add(new(){Id="11",ConfigTableId="1",YzProjectId="100",YzAdminTypeId="9",WhereSql="status=7"});
        catalog.ConfigPageColumns.Add(new(){Id="1113",ConfigPageId="11",ConfigColumnId="13",YzProjectId="100",IsShow=1,IsEdit=1,
            Extra=alternatePage?[]:new(){["controlDetail"]=Overlay("segment=19")}});

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-downstream-cache");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.GlobalDownstreamCache");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            if(alternatePage)
            {
                // 验证发布后从冻结目录初始化的原列表、Excel 导出和弹窗单选计划。
                var intermediate=assembly.GetType("Example.GlobalDownstreamCache.Generated.Services.GlobalSourceService",true)!;
                var consumer=assembly.GetType("Example.GlobalDownstreamCache.Generated.Services.CachedConsumerService",true)!;
                const System.Reflection.BindingFlags planFlags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
                const System.Reflection.BindingFlags readFlags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                foreach(var (serviceType,name,restricted) in new[]{
                    (intermediate,"ReadPlan10",true),(intermediate,"ExportReadPlan10",true),
                    (intermediate,"ReadPlan11",false),(consumer,"DerivedOptionsPlan30_38",true)})
                {
                    var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(serviceType.GetField(name,planFlags)!.GetValue(null));
                    Assert.Contains("global_target_name",plan.OutputTypes.Keys);
                    var sql=plan.GetType().GetProperty("MySql",readFlags)!.GetValue(plan)!;
                    var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                        sql.GetType().GetProperty("Constants",readFlags)!.GetValue(sql)).Values;
                    Assert.Equal(restricted,parameters.Contains(3));
                }
            }
            var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                assembly.GetType("Example.GlobalDownstreamCache.Generated.GeneratedRelationMaintenance",true)!
                    .GetProperty("Catalog")!.GetValue(null));
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(maintenance.GetType().GetProperty("Plans",flags)!.GetValue(maintenance));
            var table=plans["3"]!;
            var read=table.GetType().GetProperty("Read")!.GetValue(table)!;
            var values=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                read.GetType().GetProperty("Constants",flags)!.GetValue(read)).Values;
            Assert.Contains(3,values);
            Assert.DoesNotContain(17,values);
            Assert.DoesNotContain(19,values);
            Assert.DoesNotContain(7,values);
            var from=Assert.IsType<string>(read.GetType().GetProperty("From",flags)!.GetValue(read));
            Assert.DoesNotContain(".`segment`",from);
            var fields=new[]{"global_source_global_target_name","selected_global_source_global_target_name"};
            foreach(var field in fields)
            {
                var expression=Assert.IsType<string>(read.GetType().GetMethod("Expression",flags)!.Invoke(read,[field]));
                Assert.Contains(".`segment`",expression);
                Assert.Contains(".`status`",expression);
                if(mode=="form")
                {
                    Assert.Contains(".`lookup_segment`",expression);
                    Assert.DoesNotContain("b.`lookup_segment`",expression);
                }
                if(mode=="account")Assert.Contains("b.`store_id`",expression);
                var reverse=Assert.IsAssignableFrom<System.Collections.IEnumerable>(read.GetType().GetProperty("Reverse",flags)!.GetValue(read));
                Assert.Contains(reverse.Cast<object>(),item=>
                    Equals(mode=="deep"?"global_leaf":"global_target",item.GetType().GetProperty("TargetTable")!.GetValue(item))&&
                    Assert.IsAssignableFrom<IReadOnlyDictionary<string,IReadOnlySet<string>>>(item.GetType().GetProperty("Outputs")!.GetValue(item))
                        .TryGetValue(field,out var dependencies)&&dependencies.Contains("segment"));
                if(mode=="form")
                    Assert.Contains(reverse.Cast<object>(),item=>
                        Equals("global_source",item.GetType().GetProperty("TargetTable")!.GetValue(item))&&
                        Assert.IsAssignableFrom<IReadOnlyDictionary<string,IReadOnlySet<string>>>(item.GetType().GetProperty("Outputs")!.GetValue(item))
                            .TryGetValue(field,out var dependencies)&&dependencies.Contains("lookup_segment"));
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}

        void Add(string id,string table,string name,string type)=>catalog.ConfigColumns.Add(new()
            {Id=id,ConfigTableId=table,ColumnName=name,LogicalType=type,YzProjectId="100"});
        JsonElement Overlay(string where)=>JsonSerializer.SerializeToElement(JsonSerializer.Serialize(new{whereSql=where}));
    }

    /// <summary>多段副本的中间页拒绝不同名称及与已声明字典冲突的页面。</summary>
    [Theory][InlineData("nameField","status")][InlineData("pageId","30")]
    public void Global_R1_name_cache_rejects_a_changed_intermediate_binding(string key,string value)
    {
        var catalog=Catalog(1,"int64","int64");
        var overlay=new Dictionary<string,string>{{key,value}};
        catalog.ConfigPageColumns.Single(field=>field.Id=="1013").Extra["controlDetail"]=
            JsonSerializer.SerializeToElement(JsonSerializer.Serialize(overlay));
        var owner=catalog.ConfigTables.Single(table=>table.Id=="3");
        var target=catalog.ConfigTables.Single(table=>table.Id=="1");
        var foreign=catalog.ConfigColumns.Single(column=>column.Id=="34");
        var failure=Assert.Throws<InvalidOperationException>(()=>GlobalSingleRelationTarget.RequireCached(catalog,owner,foreign,target));
        Assert.Contains(key=="nameField"?"已发布的目标和名称":"真实目标页",failure.Message);
    }

    /// <summary>四个交叉模型实际生成完整包、严格构建并挂载原Vue，两次更新保持手写业务和全部受管产物。</summary>
    [Theory][InlineData(0,"int64","int64")][InlineData(0,"string","string")][InlineData(1,"int64","string")][InlineData(1,"string","int64")]
    public async Task Global_r0_full_application_builds_updates_and_runs_original_controls(int mode,string outer,string inner)
    {
        var c=Catalog(mode,outer,inner);var before=JsonSerializer.Serialize(c);var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-derived-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");var repository=NonRedundantApplicationFixture.Root();
        try
        {
            new ApplicationPackager().Create(c,output,"Example.GlobalDerivedTarget",repository);
            var hand=Path.Combine(output,"backend/generated/Services/CachedConsumerService.cs");await File.WriteAllTextAsync(hand,"namespace Example.GlobalDerivedTarget.Generated.Services;\npublic partial class CachedConsumerService { /// <summary>验收手写扩展，必须跨重复生成保持。</summary>\npublic string PreservedBusinessRule()=>\"全局R0原业务\"; }\n");
            var saved=await File.ReadAllBytesAsync(hand);var generated=Directory.GetFiles(Path.Combine(output,"backend/generated"),"*.g.cs",SearchOption.AllDirectories).ToDictionary(path=>Path.GetRelativePath(output,path),File.ReadAllBytes);
            var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(c,candidate,"Example.GlobalDerivedTarget",repository);var updater=new LocalGenerationUpdater();var operations=new[]{"generate-project-config","sync-runtime","generate-all-page"};var plan=updater.Preview(output,candidate,operations);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);
            Assert.Equal(saved,await File.ReadAllBytesAsync(hand));foreach(var file in generated)Assert.Equal(file.Value,await File.ReadAllBytesAsync(Path.Combine(output,file.Key)));
            var repeat=updater.Preview(output,candidate,operations);Assert.Equal("unchanged",updater.Apply(repeat,repeat.PlanSha256).Status);
            Assert.True(File.Exists(Path.Combine(output,"src/Metarill.Core/Metadata/GlobalDerivedRelationSource.cs")));
            Assert.Contains("Name = \"global_target_name\", IsIgnore = true",await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/Entities/GlobalSource.g.cs")));
            var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",output);foreach(var arg in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--verbosity","quiet","--disable-build-servers"})build.ArgumentList.Add(arg);await NonRedundantApplicationFixture.RunAsync(build);
            var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"GlobalCachedTargetVueProbe.mjs"));probe.ArgumentList.Add(output);await NonRedundantApplicationFixture.RunAsync(probe);
            foreach(var file in new[]{"components/yzp-relation-select/yzp-relation-select.vue","components/yzp-file-library/yzp-file-library.vue","common/yzp_runtime/detailLayoutRuntime.js"})Assert.Equal(File.ReadAllBytes(Path.Combine(repository,"frontend/metarill_uni_admin",file)),File.ReadAllBytes(Path.Combine(output,"frontend",file)));
            // Production真实HTTP只证明匿名认证边界，不把离线端口当作有效登录或业务执行。
            var hostDirectory=Path.Combine(output,"backend/Application.Host");var host=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);host.ArgumentList.Add(Path.Combine(hostDirectory,"bin/Release/net10.0/Application.Host.dll"));host.Environment["ASPNETCORE_ENVIRONMENT"]="Production";host.Environment["DOTNET_ENVIRONMENT"]="Production";host.Environment["Authentication__Mode"]="Web";host.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";host.Environment["Database__Provider"]="mysql";host.Environment["Database__ExpectedDatabase"]="offline";host.Environment["Database__ConnectionString"]="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1";host.Environment["Files__RootDirectory"]=Path.Combine(directory,"uploads");host.Environment["Exports__RootDirectory"]=Path.Combine(directory,"exports");
            var hostLogs=Path.Combine(repository,".local/global-derived-target-hosts",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(hostLogs);host.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=hostLogs;
            await using(var running=await FreshHost.StartAsync(host,"/health")){foreach(var action in new[]{"page","importDatas30","initExport","findNameIdGlobalSource34/page"})await FreshWebHttp.SendAsync(running.Client,HttpMethod.Post,"/api/admin/auto/cached_consumer/30/"+action,new{},System.Net.HttpStatusCode.Unauthorized);}
            Assert.Equal(before,JsonSerializer.Serialize(c));
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>无行项目的全局R0服务仍固定发布项目，普通和超级身份切错项目都在连接前拒绝。</summary>
    [Theory][InlineData(0)][InlineData(1)]
    public async Task Actual_generated_services_reject_foreign_project_before_connecting(int mode)
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-derived-actor");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(Catalog(mode,"int64","int64"),Path.Combine(directory,"generated"),"Example.GlobalDerivedActor");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
            foreach(var administrator in new[]{false,true})foreach(var (name,page) in new[]{("GlobalSource","10"),("GlobalTarget","20"),("CachedConsumer","30")})
            {
                var actor=new RuntimeActor(8,"发布范围观测",administrator,"101","");var mapper=Activator.CreateInstance(assembly.GetType("Example.GlobalDerivedActor.Generated.Mappers."+name+"Mapper",true)!,session)!;var constructor=assembly.GetType("Example.GlobalDerivedActor.Generated.Services."+name+"Service",true)!.GetConstructors().Single();
                var service=constructor.Invoke(constructor.GetParameters().Select(parameter=>parameter.ParameterType==typeof(DatabaseSession)?(object)session:parameter.ParameterType.IsInstanceOfType(mapper)?mapper:parameter.ParameterType==typeof(IIdGenerator)?new SnowflakeIdGenerator(909):parameter.ParameterType==typeof(IEnumerable<IRecordMutationObserver>)?Array.Empty<IRecordMutationObserver>():parameter.ParameterType==typeof(IEnumerable<IRecordMutationGuard>)?Array.Empty<IRecordMutationGuard>():null).ToArray());
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async()=>await(Task)service.GetType().GetMethod("Page"+page+"Async")!.Invoke(service,[new PageRequest(),actor,CancellationToken.None])!);
            }
            Assert.Null(session.CurrentTransaction);
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>原公开完整包保持严格门槛，不以隐藏名称、动态目标或伪造全局声明绕开生成校验。</summary>
    [Theory][InlineData("privateProducer")][InlineData("privateTerminal")][InlineData("hiddenName")][InlineData("dynamicPage")][InlineData("credential")][InlineData("fakePath")][InlineData("wrongProject")][InlineData("foreignName")][InlineData("virtualProducerProject")]
    public void Invalid_global_r0_application_is_rejected_before_staging(string kind)
    {
        var c=Catalog(0,"int64","int64");switch(kind)
        {
            case "virtualProducerProject":c.ConfigColumns.Add(new(){Id="18",ConfigTableId="1",YzProjectId="100",ColumnName="yz_project_id",LogicalType="int64",IsVirtualColumn=1});break;
            case "privateProducer":c.ConfigTables.Single(t=>t.Id=="1").Extra.Clear();break;
            case "privateTerminal":c.ConfigTables.Single(t=>t.Id=="2").Extra.Clear();break;
            case "hiddenName":c.ConfigPageColumns.RemoveAll(f=>f.ConfigColumnId=="14");break;
            case "dynamicPage":c.ConfigPages.Single(p=>p.Id=="10").WhereSql="id=@parent_id";break;
            case "credential":c.ConfigColumns.Single(f=>f.Id=="22").ColumnName="password";break;
            case "fakePath":c.ConfigColumns.Single(f=>f.Id=="14").Extra["columnFullName"]=JsonSerializer.SerializeToElement("missing.name");break;
            case "wrongProject":c.ConfigTables.Single(t=>t.Id=="1").YzProjectId="101";break;
            case "foreignName":
                // 物理整数已可用，负例改为页级名称与发布目录中的固定名称冲突。
                c.ConfigPageColumns.Single(f => f.Id == "3034").Extra["controlDetail"] = JsonSerializer.SerializeToElement(new { nameField = "status" });
                break;
        }
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-derived-reject");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.GlobalDerivedReject",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>复用已经公开的三表完整目录，只替换中间存储模式与其物理外键类型。</summary>
    internal static MetadataCatalog Catalog(int mode,string outer,string inner)
    {
        var c=GlobalCachedTargetGenerationTests.Catalog(mode,outer);
        c.ConfigTables.Single(t=>t.Id=="1").IsRongyu=0;
        c.ConfigColumns.Single(f=>f.Id=="13").LogicalType=inner;
        // 原R0目标页显式开放逻辑名称；页面布局和选择控件仍由原模板决定。
        c.ConfigPageColumns.Add(new(){Id="1014",ConfigPageId="10",ConfigColumnId="14",YzProjectId="100",IsShow=1,IsEdit=0});
        return c;
    }
}
