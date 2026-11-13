using Metarill.Core.Contracts;
using Metarill.Platform.Generated.VOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Metarill.Platform.Generated.Controllers;

/// <summary>平台原页面104字段的历史候选URI；直接复用105生成入口的身份、来源页与目标页权限。</summary>
public sealed partial class ConfigPagePage1868973685063311361Controller
{
    /// <summary>兼容原数组与空正文，不另建查询或改变现有上限。</summary>
    [HttpPost("findNameIdYzAdminType2200000000000000104")]
    public Task<R<IReadOnlyList<GeneratedOption>>> findNameIdYzAdminType2200000000000000104(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RelationOptionsLegacyRequest? request, CancellationToken cancellationToken)
        => findNameIdYzAdminType2200000000000000105(request, cancellationToken);

    /// <summary>原字段分页地址沿用当前字段的参数校验、筛选和授权。</summary>
    [HttpPost("findNameIdYzAdminType2200000000000000104/page")]
    public Task<R<PageResult<GeneratedOption>>> findNameIdYzAdminType2200000000000000104Page(
        [FromBody] RelationOptionsPageRequest request, CancellationToken cancellationToken)
        => findNameIdYzAdminType2200000000000000105Page(request, cancellationToken);

    /// <summary>已选值回读仍经当前字段逐项授权，不按客户端ID直接查表。</summary>
    [HttpPost("findNameIdYzAdminType2200000000000000104/selected")]
    public Task<R<IReadOnlyList<GeneratedOption>>> findNameIdYzAdminType2200000000000000104Selected(
        [FromBody] RelationOptionsSelectedRequest request, CancellationToken cancellationToken)
        => findNameIdYzAdminType2200000000000000105Selected(request, cancellationToken);
}

/// <summary>主页面配置原104地址的同程序集扩展；再生成只更新105定义，不覆盖本兼容文件。</summary>
public sealed partial class ConfigPagePage6191175925956608Controller
{
    /// <summary>保留旧数组入口与空正文语义，调用同一生成方法。</summary>
    [HttpPost("findNameIdYzAdminType2200000000000000104")]
    public Task<R<IReadOnlyList<GeneratedOption>>> findNameIdYzAdminType2200000000000000104(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RelationOptionsLegacyRequest? request, CancellationToken cancellationToken)
        => findNameIdYzAdminType2200000000000000105(request, cancellationToken);

    /// <summary>分页兼容地址不会扩大父记录或目标候选范围。</summary>
    [HttpPost("findNameIdYzAdminType2200000000000000104/page")]
    public Task<R<PageResult<GeneratedOption>>> findNameIdYzAdminType2200000000000000104Page(
        [FromBody] RelationOptionsPageRequest request, CancellationToken cancellationToken)
        => findNameIdYzAdminType2200000000000000105Page(request, cancellationToken);

    /// <summary>独立回读使用相同来源、目标页和记录范围检查。</summary>
    [HttpPost("findNameIdYzAdminType2200000000000000104/selected")]
    public Task<R<IReadOnlyList<GeneratedOption>>> findNameIdYzAdminType2200000000000000104Selected(
        [FromBody] RelationOptionsSelectedRequest request, CancellationToken cancellationToken)
        => findNameIdYzAdminType2200000000000000105Selected(request, cancellationToken);
}

/// <summary>单表菜单保留原二级菜单候选地址；读取仍经过当前来源页与真实菜单页的权限和范围。</summary>
public sealed partial class YzRolePermissionPage2063264600601714690Controller
{
    /// <summary>旧数组及空正文直接复用当前菜单候选，不恢复旧双表或绕过原5000上限。</summary>
    [HttpPost("findNameIdConfigSecondMenu2063264600681406465")]
    public Task<R<IReadOnlyList<GeneratedOption>>> findNameIdConfigSecondMenu2063264600681406465(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RelationOptionsLegacyRequest? request,CancellationToken cancellationToken)
        =>findNameIdConfigMenu2063264600681406465(request,cancellationToken);
    /// <summary>旧地址分页与当前单表菜单采用完全相同的查询合同。</summary>
    [HttpPost("findNameIdConfigSecondMenu2063264600681406465/page")]
    public Task<R<PageResult<GeneratedOption>>> findNameIdConfigSecondMenu2063264600681406465Page(
        [FromBody]RelationOptionsPageRequest request,CancellationToken cancellationToken)
        =>findNameIdConfigMenu2063264600681406465Page(request,cancellationToken);
    /// <summary>旧地址已选回读不接受客户端指定菜单表，逐项使用当前授权范围。</summary>
    [HttpPost("findNameIdConfigSecondMenu2063264600681406465/selected")]
    public Task<R<IReadOnlyList<GeneratedOption>>> findNameIdConfigSecondMenu2063264600681406465Selected(
        [FromBody]RelationOptionsSelectedRequest request,CancellationToken cancellationToken)
        =>findNameIdConfigMenu2063264600681406465Selected(request,cancellationToken);
}
