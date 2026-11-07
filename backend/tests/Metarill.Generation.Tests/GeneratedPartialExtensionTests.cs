using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>真实生成/编译证明同一类型扩展，不以字符串包含检查代替partial编译与执行。</summary>
public sealed class GeneratedPartialExtensionTests
{
    /// <summary>冗余与非冗余VO均可扩展；元数据变化后重生成保留所有手写类型和原协议约束。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task All_business_types_allow_same_assembly_partials_and_preserve_them_after_regeneration(bool nonredundant)
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-generated-partials");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        var catalog=nonredundant?NonRedundantGeneratedFixture.Catalog("mt_partial"):GeneratorTests.Catalog();
        catalog.ConfigPages.Single(page=>page.Id=="101").IsImport=1;
        var model=GenerationModel.Create(catalog,"Example.Partials");var table=model.Tables.Single(table=>table.Source.Id=="1");
        var page=table.Pages.Single(page=>page.Source.Id=="101");var type=table.Name;var pageType=page.Name;
        try
        {
            var output=Path.Combine(directory,"generated");var generator=new CSharpGenerator();var first=generator.Generate(catalog,output,model.Namespace);
            var extension=Path.Combine(output,"Extensions/BusinessPartials.cs");Directory.CreateDirectory(Path.GetDirectoryName(extension)!);
            await File.WriteAllTextAsync(extension,Extensions(type,pageType));var handwritten=await File.ReadAllBytesAsync(extension);
            // 真正增加一个元数据字段，不能把同一输入重跑冒充升级后的手写保护。
            catalog.ConfigColumns.Add(new(){Id="190",ConfigTableId="1",ColumnName="extension_probe",LogicalType="int32",Name="扩展验证"});
            catalog.ConfigPageColumns.Add(new(){Id="101190",ConfigPageId="101",ConfigColumnId="190",IsEdit=1,IsShow=1});
            var second=generator.Generate(catalog,output,model.Namespace);Assert.NotEqual(first.MetadataSha256,second.MetadataSha256);
            Assert.Empty(second.CreatedHandwrittenFiles);Assert.Equal(handwritten,await File.ReadAllBytesAsync(extension));
            Assert.DoesNotContain("Extensions/BusinessPartials.cs",second.GeneratedFiles);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            Type Generated(string kind,string name)=>assembly.GetType("Example.Partials.Generated."+kind+"."+name,true)!;
            object Invoke(object value,string method)=>value.GetType().GetMethod(method)!.Invoke(value,null)!;
            var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);json.Converters.Add(new StringLongJsonConverter());
            foreach(var kind in new[]{"Create","Update"})
            {
                var request=Generated("DTOs",pageType+kind+"Request");Assert.True(request.IsSealed);
                var omitted=JsonSerializer.Deserialize("{}",request,json)!;var explicitNull=JsonSerializer.Deserialize("{\"name\":null}",request,json)!;
                Assert.False((bool)Invoke(omitted,"ExtensionNameSpecified"));Assert.True((bool)Invoke(explicitNull,"ExtensionNameSpecified"));
                Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize("{\"extensionNameSpecified\":true}",request,json));
                Assert.NotNull(request.GetProperty("ExtensionProbe"));
            }
            var record=JsonSerializer.Deserialize("{\"id\":\"9007199254740993\"}",Generated("DTOs",pageType+"RecordRequest"),json)!;
            Assert.Equal("9007199254740993",Invoke(record,"ExtensionIdText"));
            var import=JsonSerializer.Deserialize("{\"requestJson\":[{\"名称\":\"甲\"}],\"parentId\":null}",Generated("DTOs",pageType+"ImportRequest"),json)!;
            Assert.Equal(1,Invoke(import,"ExtensionRowCount"));
            var entity=Activator.CreateInstance(Generated("Entities",type))!;entity.GetType().GetProperty("Name")!.SetValue(entity,"  甲  ");Assert.Equal("甲",Invoke(entity,"ExtensionLabel"));
            var vo=JsonSerializer.Deserialize("{\"id\":\"9007199254740993\",\"name\":\"  甲  \"}",Generated("VOs",pageType+"Vo"),json)!;
            Assert.True(vo.GetType().IsSealed);Assert.Equal("甲",Invoke(vo,"ExtensionLabel"));
            using(var payload=JsonDocument.Parse(JsonSerializer.Serialize(vo,vo.GetType(),json)))Assert.False(payload.RootElement.TryGetProperty("extensionLabel",out _));

            // 只构造延迟连接的真实Session，不执行查询、连接或事务；私有依赖必须由同一类型的partial访问。
            using var session=new DatabaseSession(new(){ExpectedDatabase="metarill_partial_compile",ConnectionString="Server=127.0.0.1;Port=1;Database=metarill_partial_compile;User ID=offline;Pooling=false;Connection Timeout=1"});
            var mapper=Activator.CreateInstance(Generated("Mappers",type+"Mapper"),session)!;Assert.True(mapper.GetType().IsSealed);Assert.Equal("metarill_partial_compile",Invoke(mapper,"ExtensionDatabase"));
            var service=Activator.CreateInstance(Generated("Services",type+"Service"),[session,mapper,new SnowflakeIdGenerator(944),Array.Empty<Metarill.Core.Persistence.IRecordMutationObserver>(),null])!;
            Assert.Equal("metarill_partial_compile",Invoke(service,"ExtensionDatabase"));
            var authorization=new PartialExtensionAuthorization();var controller=Activator.CreateInstance(Generated("Controllers",pageType+"Controller"),service,authorization)!;
            Assert.True(controller.GetType().IsSealed);Assert.Equal(authorization.RequireActor(),Invoke(controller,"ExtensionAuthorize"));Assert.Equal("101:list",authorization.Requested);
            Assert.NotNull(controller.GetType().GetMethod("ExtensionAuthorize")!.GetCustomAttribute<Microsoft.AspNetCore.Mvc.NonActionAttribute>());
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>手写文件位于原命名空间、同一程序集，不重新声明主构造函数或生成属性。</summary>
    private static string Extensions(string type,string page)=>$$"""
using System.Globalization;
using Metarill.Core.Security;
using Microsoft.AspNetCore.Mvc;
namespace Example.Partials.Generated.Entities
{
    public partial class {{type}}
    {
        /// <summary>扩展可直接读取同一实体的生成成员。</summary>
        public string ExtensionLabel()=>Normalize(Name);
        /// <summary>手写规则与生成属性属于同一实体类型。</summary>
        private static string Normalize(string? value)=>value?.Trim()??"";
    }
}
namespace Example.Partials.Generated.DTOs
{
    public partial class {{page}}CreateRequest
    {
        /// <summary>扩展保留Optional的原字段存在性。</summary>
        public bool ExtensionNameSpecified()=>Name.IsSpecified;
    }
    public partial class {{page}}UpdateRequest
    {
        /// <summary>扩展保留省略和显式null的区别。</summary>
        public bool ExtensionNameSpecified()=>Name.IsSpecified;
    }
    public partial class {{page}}RecordRequest
    {
        /// <summary>长ID保持十进制文本，不经浮点数。</summary>
        public string ExtensionIdText()=>Id.ToString(CultureInfo.InvariantCulture);
    }
    public partial class {{page}}ImportRequest
    {
        /// <summary>沿原Excel解析JSON数组读取。</summary>
        public int ExtensionRowCount()=>RequestJson.GetArrayLength();
    }
}
namespace Example.Partials.Generated.VOs
{
    public partial class {{page}}Vo
    {
        /// <summary>方法扩展不自动变成JSON属性。</summary>
        public string ExtensionLabel()=>Name?.Trim()??"";
    }
}
namespace Example.Partials.Generated.Mappers
{
    public partial class {{type}}Mapper
    {
        /// <summary>另一partial可访问主构造函数参数。</summary>
        public string ExtensionDatabase()=>session.Options.ExpectedDatabase;
    }
}
namespace Example.Partials.Generated.Services
{
    public partial class {{type}}Service
    {
        /// <summary>另一partial可访问生成的private字段。</summary>
        public string ExtensionDatabase()=>session.Options.ExpectedDatabase;
    }
}
namespace Example.Partials.Generated.Controllers
{
    public partial class {{page}}Controller
    {
        /// <summary>扩展复用生成的private授权边界；辅助方法不暴露为HTTP操作。</summary>
        [NonAction]
        public RuntimeActor ExtensionAuthorize()=>Actor("list");
    }
}
""";
}

/// <summary>固定离线授权器只验证partial调用原私有授权方法，不代表真实登录或权限数据库。</summary>
internal sealed class PartialExtensionAuthorization:IRuntimeAuthorization
{
    internal string? Requested;
    /// <summary>固定服务端身份，不从调用参数推断项目授权。</summary>
    public RuntimeActor RequireActor()=>new(1,"partial编译验收",false,"100","1");
    /// <summary>记录生成私有Actor方法执行的原动作校验。</summary>
    public void RequirePagePermission(string pageId,string operation)=>Requested=pageId+":"+operation;
}
