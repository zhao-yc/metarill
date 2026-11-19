using System.Text;

namespace Metarill.Generation;

internal static partial class BackendRenderer
{
    private static bool HasFunctionOptions(GenerationModel model, TableModel table) =>
        table.Pages.Any(page => RelationCompilation.ForPage(model, page).Count != 0);

    /// <summary>原功能表只承载前端表单及已发布外表候选。按钮继续由宿主的手写业务处理器执行。</summary>
    private static IEnumerable<PlannedFile> FunctionTableFiles(GenerationModel model, TableModel table)
    {
        if (HasFunctionOptions(model, table))
        {
            var options = string.Concat(table.Pages.Select(page => Relations(model, page)));
            var service = CSharpGenerator.Header + $$"""
using System.Globalization;
using System.Text.Json;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Queries;
using {{model.Namespace}}.Generated.VOs;
namespace {{model.Namespace}}.Generated.Services;

/// <summary>只读取已授权的目标记录，不为功能表创建实体或数据库写入方法。</summary>
public sealed partial class {{table.Name}}Service(DatabaseSession session, IRuntimeAuthorization relationAuthorization)
{
    private void ValidateActor(RuntimeActor actor)
    {
        if (actor.AccountId <= 0 || actor.ProjectId != {{Literal(model.Catalog.YzProjectId)}} ||
            relationAuthorization.RequireActor() != actor)
            throw new UnauthorizedAccessException("功能表候选需要当前项目真实身份。");
    }
{{GlobalCachedSourceCompilation.ReadSessionDeclaration(model)}}
{{options}}
}
""";
            yield return new($"Services/{table.Name}Service.g.cs", service);
        }

        foreach (var page in table.Pages)
        {
            var body = new StringBuilder();
            AppendCandidateActions(body, model, table, page);
            if (body.Length == 0) continue;
            var serviceParameter = RelationCompilation.ForPage(model, page).Count == 0 ? "" :
                table.Name + "Service service, ";
            var serviceImport = serviceParameter.Length == 0 ? "" : $"using {model.Namespace}.Generated.Services;\n";
            yield return new($"Controllers/{page.Name}Controller.g.cs", CSharpGenerator.Header + $$"""
using Microsoft.AspNetCore.Mvc;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using {{model.Namespace}}.Generated.VOs;
{{serviceImport}}namespace {{model.Namespace}}.Generated.Controllers;

[ApiController]
[Route({{Literal("api/admin/auto/" + table.Source.TableName + "/" + page.Source.Id)}})]
public sealed partial class {{page.Name}}Controller({{serviceParameter}}IRuntimeAuthorization authorization) : ControllerBase
{
    private RuntimeActor Actor(string operation)
    {
        authorization.RequirePagePermission({{Literal(page.Source.Id)}}, operation);
        return authorization.RequireActor();
    }
{{body}}
}
""");
        }
    }
}
