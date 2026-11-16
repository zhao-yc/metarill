using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed class PageInitializationTests
{
    /// <summary>新建接入账号、固定值和业务日期，更新与复制沿用原字段规则；实际生成项目能编译。</summary>
    [Fact]
    public async Task Page_initializers_run_only_on_create_before_business_rules()
    {
        var catalog=GeneratorTests.Catalog();
        catalog.ConfigPages[0].IsCopy=catalog.ConfigPages[0].IsImport=1;
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="13").Extra["initValue"]=JsonSerializer.SerializeToElement("初始备注");
        Add("16","owner_id","int64","current_yz_manager_id");
        Add("17","store_id","int64","current_store_id");
        Add("18","booked_at","date","today");
        Add("19","flag","bool","0");
        var model=GenerationModel.Create(catalog,"Example.FormInitializers");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-page-init");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);
            var code=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/CustomerService.g.cs"));
            Assert.Contains("entity.Remark = ",code);
            Assert.Contains("entity.OwnerId = long.Parse(global::Metarill.Core.Security.AccountConditionVariables.Require(actor, \"current_yz_manager_id\")",code);
            Assert.Contains("entity.StoreId = long.Parse(global::Metarill.Core.Security.AccountConditionVariables.Require(actor, \"current_store_id\")",code);
            Assert.Contains("entity.BookedAt = BusinessCalendar.Now.Date;",code);
            Assert.Contains("entity.Flag = false;",code);
            Assert.Equal(1,code.Split("entity.BookedAt = BusinessCalendar.Now.Date;",StringSplitOptions.None).Length-1);
            Assert.True(code.IndexOf("entity.BookedAt = BusinessCalendar.Now.Date;",StringComparison.Ordinal)<code.IndexOf("BeforeSave(entity, actor, true, submitted);",StringComparison.Ordinal));
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            Assert.NotNull(assembly.GetType(model.Namespace+".Generated.Services.CustomerService",true));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
        void Add(string id,string name,string type,string definition)
        {
            catalog.ConfigColumns.Add(new(){Id=id,ConfigTableId="1",ColumnName=name,LogicalType=type});
            catalog.ConfigPageColumns.Add(new(){Id="1"+id,ConfigPageId="101",ConfigColumnId=id,IsEdit=1,Extra=new(){["initValue"]=JsonSerializer.SerializeToElement(definition)}});
        }
    }

    /// <summary>发布期拒绝坏初始值或客户端不应覆写的真实主键。</summary>
    [Theory]
    [InlineData("11","10")]
    [InlineData("12","today")]
    public void Bad_initializers_do_not_publish_partial_files(string field,string value)
    {
        var catalog=GeneratorTests.Catalog();
        var setting=catalog.ConfigPageColumns.SingleOrDefault(item=>item.ConfigColumnId==field);
        if(setting is null){setting=new PageColumnMetadata{Id="1111",ConfigPageId="101",ConfigColumnId=field};catalog.ConfigPageColumns.Add(setting);}
        setting.Extra["initValue"]=JsonSerializer.SerializeToElement(value);
        var target=Path.Combine(Path.GetTempPath(),"metarill-bad-page-init",Guid.NewGuid().ToString("N"));
        Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,target,"Example.InvalidInitializers"));
        Assert.False(Directory.Exists(target));
    }
}
