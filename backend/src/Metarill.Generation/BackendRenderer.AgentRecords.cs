using System.Text;
namespace Metarill.Generation;

internal static partial class BackendRenderer
{
    /// <summary>只公开页面已启用的记录写操作；创建和整批导入没有当前原记录，不进入此合同。</summary>
    private static IReadOnlyList<string> AgentRecordOperations(PageModel page)
    {
        var operations=new List<string>();if(Metarill.Core.Metadata.PageUpdatePolicy.CanUpdate(page.Source))operations.Add("update");
        if(page.Source.IsDelete==1)operations.Add("delete");if(page.Source.IsCopy==1)operations.Add("copy");return operations;
    }
    /// <summary>复用实际写入scope读取原实体，身份/操作权限在SQL前验证，不额外索取页面query权限。</summary>
    private static string AgentRecordAccess(GenerationModel model,TableModel table,PageModel page)
    {
        var operations=AgentRecordOperations(page);if(operations.Count==0)return "";
        return new StringBuilder().AppendLine("    /// <summary>仅供宿主Agent记录写预检，路径及操作来自固定发布能力，不暴露HTTP读接口。</summary>")
            .AppendLine($"    public async Task<{table.Name}?> ReadAgentRecord{page.Source.Id}Async(long id,string? parentId,string operation,RuntimeActor actor,global::Metarill.Core.Security.IRuntimeAuthorization authorization,CancellationToken cancellationToken)")
            .AppendLine("    {")
            .AppendLine("        ValidateActor(actor);if(id<=0)throw new ArgumentException(\"记录标识无效。\");")
            .AppendLine($"        if(operation is not ({string.Join(" or ",operations.Select(Literal))}))throw new UnauthorizedAccessException(\"操作不属于当前已发布页面。\");")
            .AppendLine("        if(authorization.RequireActor()!=actor)throw new UnauthorizedAccessException(\"记录身份与当前真实身份不一致。\");")
            .AppendLine($"        authorization.RequirePagePermission({Literal(page.Source.Id)},operation);")
            .AppendLine($"        return await Scope{page.Source.Id}(actor,parentId{ExactDataManageCompilation.Argument(page,"operation")}{(ExactDataManageCompilation.Resolve(model,table,page) is {Derived:true}?",exactLocking:false":"")}).Where(row=>row.Id==id).FirstAsync(cancellationToken);")
            .AppendLine("    }").ToString();
    }
    /// <summary>同一程序集固定读取类型及方法；旧产物未再生成时不能伪装新预检合同。</summary>
    private static string AgentRecordAccessAttribute(GenerationModel model,TableModel table,PageModel page)=>AgentRecordOperations(page).Count==0?"":
        $"[global::Metarill.Core.Contracts.GeneratedAgentWriteAccess({Literal(model.Catalog.YzProjectId)}, {Literal(page.Source.Id)}, {Literal(table.Source.Id)}, typeof(global::{model.Namespace}.Generated.Entities.{table.Name}), typeof({table.Name}Service), nameof({table.Name}Service.ReadAgentRecord{page.Source.Id}Async))]"+Environment.NewLine;
}
