using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Files.Tests;

/// <summary>非冗余派生文件不拥有源记录引用，父范围也不能引用未持久化的派生键。</summary>
public sealed class NonRedundantFileStorageTests
{
    /// <summary>发布时编译物理列，派生文件从计划排除；调用者原对象变化不能污染内部深拷贝。</summary>
    [Fact]
    public void File_snapshot_compiles_physical_storage_and_rejects_foreign_plan_objects()
    {
        var catalog=Catalog();var context=new FileMetadataContext([catalog]);var frozen=context.Catalogs.Single();Assert.Equal(new[]{"id","name"},context.PhysicalColumns(frozen,frozen.ConfigTables.Single()).Select(column=>column.ColumnName));
        catalog.ConfigColumns.Single(column=>column.Id=="13").Extra.Clear();Assert.Equal(2,context.PhysicalColumns(frozen,frozen.ConfigTables.Single()).Count);Assert.Throws<InvalidOperationException>(()=>context.PhysicalColumns(catalog,catalog.ConfigTables.Single()));
    }
    /// <summary>即使观察上下文包含派生文件值，也不能创建本对象引用或解析数据库/授权器。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Derived_file_payload_and_delete_do_not_create_or_remove_file_references(bool deleting)
    {
        using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="never_open",ConnectionString="Server=127.0.0.1;Port=1;Database=never_open;User ID=unused;Password=unused;Connection Timeout=1"});var catalog=Catalog();var store=new SqlRowStore(session,new MetadataCatalog());var observer=new FileReferenceMutationObserver(store,new FileMetadataContext([catalog]),new NeverAuthorize(),new SnowflakeIdGenerator(955));
        await observer.BeforeCommitAsync(new RecordMutationContext("4","orders",1,"1",new Dictionary<string,object?>{{"derived_files","[\"9007199254740993\"]"}},deleting,null!));
    }
    /// <summary>父过滤必须有真实物理外键，派生ID不能被当作锁定父记录的输入。</summary>
    [Fact]
    public void Derived_parent_key_is_rejected_before_any_file_record_query()
    {
        var catalog=Catalog();catalog.ConfigPages.Single().WhereSql="parent_id=@parentId";catalog.ConfigColumns.Add(new(){Id="14",ConfigTableId="2",ColumnName="parent_id",LogicalType="int64",LinkConfigTableId="8",Extra=new(){{"isJoinColumn",JsonSerializer.SerializeToElement(1)}}});Assert.Throws<NotSupportedException>(()=>FileReferenceScope.Compile(catalog,catalog.ConfigTables.Single(),catalog.ConfigPages.Single()));
    }
    /// <summary>只含真实业务列和一个显式派生文件字段，不使用真实文件库或账号。</summary>
    private static MetadataCatalog Catalog()=>new()
    {
        YzProjectId="1",ConfigTables=[new(){Id="2",TableName="orders",IsRongyu=0,YzProjectId="1"}],ConfigPages=[new(){Id="4",ConfigTableId="2",YzProjectId="1"}],ConfigColumns=[new(){Id="11",ConfigTableId="2",ColumnName="id",LogicalType="int64"},new(){Id="12",ConfigTableId="2",ColumnName="name"},new(){Id="13",ConfigTableId="2",ColumnName="derived_files",ControlKind="FileLibrary",Extra=new(){{"isJoinColumn",JsonSerializer.SerializeToElement(1)}}}],ConfigPageColumns=[new(){Id="21",ConfigPageId="4",ConfigColumnId="13",IsEdit=1}]
    };
    /// <summary>如错误分支解析到授权依赖，测试必须硬失败，不返回固定成功身份。</summary>
    private sealed class NeverAuthorize:IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>throw new InvalidOperationException("派生文件不应请求授权依赖。");
        public void RequirePagePermission(string pageId,string operation)=>throw new InvalidOperationException("派生文件不应请求授权依赖。");
    }
}
