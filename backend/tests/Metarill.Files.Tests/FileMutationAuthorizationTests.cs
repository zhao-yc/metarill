using Metarill.Core.Persistence;
using Metarill.Runtime.Files;
using Xunit;

namespace Metarill.Files.Tests;

/// <summary>文件引用采用原入口权限，不要求仅导入/复制页面另开普通新增编辑。</summary>
public sealed class FileMutationAuthorizationTests
{
    /// <summary>原构造函数默认规则不变；导入允许实际新增或更新，复制只能对应实际新记录。</summary>
    [Theory]
    [InlineData(null,false,false,"update")][InlineData(null,false,true,"create")][InlineData(null,true,false,"delete")]
    [InlineData(RecordMutationOperation.Import,false,false,"import")][InlineData(RecordMutationOperation.Import,false,true,"import")]
    [InlineData(RecordMutationOperation.Copy,false,true,"copy")][InlineData(RecordMutationOperation.Create,false,true,"create")]
    [InlineData(RecordMutationOperation.Update,false,false,"update")][InlineData(RecordMutationOperation.Delete,true,false,"delete")]
    public void Original_operation_is_checked_against_actual_mutation_state(RecordMutationOperation? operation,bool deleting,bool creating,string expected)
    {
        var context=Context(operation,deleting,creating);Assert.Equal(expected,FileReferenceMutationObserver.AuthorizationAction(context));
    }
    /// <summary>未知操作和矛盾标志不能借导入或复制降低权限。</summary>
    [Theory]
    [InlineData(RecordMutationOperation.Import,true,false)][InlineData(RecordMutationOperation.Copy,false,false)]
    [InlineData(RecordMutationOperation.Copy,true,true)][InlineData(RecordMutationOperation.Create,false,false)]
    [InlineData(RecordMutationOperation.Update,false,true)][InlineData(RecordMutationOperation.Delete,false,false)]
    [InlineData((RecordMutationOperation)999,false,false)][InlineData(null,true,true)]
    public void Inconsistent_or_unknown_operation_fails_closed(RecordMutationOperation? operation,bool deleting,bool creating)=>Assert.Throws<UnauthorizedAccessException>(()=>FileReferenceMutationObserver.AuthorizationAction(Context(operation,deleting,creating)));
    /// <summary>纯分类测试不注入数据库；实际身份、权限和事务由完整包实库场景另验。</summary>
    private static RecordMutationContext Context(RecordMutationOperation? operation,bool deleting,bool creating)=>new("101","fixture",1,"54",new Dictionary<string,object?>(),deleting,null!,creating){AuthorizationOperation=operation};
}
