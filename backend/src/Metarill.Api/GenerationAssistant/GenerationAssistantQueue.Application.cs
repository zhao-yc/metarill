using Metarill.Generation;

namespace Metarill.Api.GenerationAssistant;

public sealed partial class GenerationAssistantQueue
{
    /// <summary>网页只枚举本账号项目的绑定；真实grant是否仍批准由控制器复核后再展示。</summary>
    public AssistantBindingView[] Bindings(AssistantOwner owner) => Locked(() =>
    {
        RequireOwner(owner, false);
        return Directory.GetFiles(options.RootDirectory, "binding-*.json").Select(path => ReadBinding(Path.GetFileName(path)[8..^5]))
            .Where(binding => binding.AccountId == owner.AccountId && binding.ProjectId == owner.ProjectId).Select(BindingView).OrderBy(binding => binding.BindingId, StringComparer.Ordinal).ToArray();
    });

    /// <summary>只返回同一已发布快照下仍有效的人工确认；每个动作至多选最早一项，不把单动作扩大为all。</summary>
    public AssistantApproval[] Approvals(AssistantOwner owner, AssistantApprovalsRequest request) => Locked(() =>
    {
        RequireOwner(owner, true); var binding = ReadBinding(request.BindingId); RequireScope(binding, owner); RequireHash(request.MetadataSha256);
        var used = Groups().SelectMany(group => group.Ticket.Approvals.Select(approval => approval.RequestId)).ToHashSet(StringComparer.Ordinal);
        return Directory.GetFiles(options.RootDirectory, "task-*.json").Select(path => Expire(ReadTask(Path.GetFileName(path)[5..^5])))
            .Where(task => task.BindingId == binding.BindingId && task.AccountId == owner.AccountId && task.ProjectId == owner.ProjectId && task.GrantId == owner.GrantId
                && task.RuntimeOrigin is null && task.Snapshot.MetadataSha256 == request.MetadataSha256 && clock.GetUtcNow() < task.ExpiresAt && (task.Status is "queued" or "leased" or "preview_ready" or "dependencies_required") && !used.Contains(task.RequestId))
            .OrderBy(task => task.CreatedAt).ThenBy(task => task.RequestId, StringComparer.Ordinal).GroupBy(task => task.Snapshot.Action).Select(group => Approval(group.First())).OrderBy(approval => approval.Action, StringComparer.Ordinal).ToArray();
    });

    /// <summary>短ticket只批准真实确认集合和本机允许范围；完整组先原子持久化，响应未知可按同UUID回读。</summary>
    public AssistantApplyTicket Authorize(AssistantOwner owner, AssistantAuthorizeRequest request, string currentMetadataSha256,AssistantRuntimeOriginProof? proof=null) => Locked(() =>
    {
        RequireOwner(owner, true); var binding = ReadBinding(request.BindingId); RequireScope(binding, owner); Uuid(request.GroupId); RequireHash(request.MetadataSha256); RequireHash(request.PlanSha256);
        var actions = Policy("apply", request.Actions);
        if (binding.Mode != "apply" || actions.Any(action => !binding.AllowedActions.Contains(action, StringComparer.Ordinal))) throw new UnauthorizedAccessException("本机绑定未明确允许本组写入动作，默认只预览。");
        if (currentMetadataSha256 != request.MetadataSha256) throw new InvalidOperationException("平台元数据已变化，请重新点击对应原按钮确认最新快照。");
        if (request.RequestIds is null || request.RequestIds.Length is < 1 or > 3 || request.RequestIds.Distinct(StringComparer.Ordinal).Count() != request.RequestIds.Length) throw new InvalidOperationException("应用组必须明确列出唯一人工确认UUID。");
        foreach (var id in request.RequestIds) Uuid(id);
        var tasks=request.RequestIds.Select(ReadTask).ToArray();var origin=RequireRuntimeSet(owner,binding,tasks,proof);
        var existing = Groups().SingleOrDefault(group => group.Ticket.GroupId == request.GroupId);
        if (existing is not null)
        {
            RequireGroup(existing, owner, binding);
            if (existing.Ticket.PlanSha256 != request.PlanSha256 || existing.Ticket.MetadataSha256 != request.MetadataSha256 || !existing.Ticket.Actions.SequenceEqual(actions) || !existing.Ticket.Approvals.Select(approval => approval.RequestId).Order(StringComparer.Ordinal).SequenceEqual(request.RequestIds.Order(StringComparer.Ordinal))) throw new InvalidOperationException("同一组UUID已绑定其他确认集合或计划。");
            if (clock.GetUtcNow() >= existing.Ticket.ExpiresAt || existing.Receipt is not null) throw new InvalidOperationException("应用ticket已过期或终结，不能重新批准。");
            return existing.Ticket;
        }
        if (Directory.GetFiles(options.RootDirectory, "group-*.json").Length >= options.MaxTasks) throw new InvalidOperationException("应用组达到保留上限，请先审阅归档。");
        var used = Groups().SelectMany(group => group.Ticket.Approvals.Select(approval => approval.RequestId)).ToHashSet(StringComparer.Ordinal);
        foreach (var task in tasks)
        {
            RequireScope(task, owner);
            if (task.BindingId != binding.BindingId || task.RootNamespace != binding.RootNamespace || task.LocalBindingSha256 != binding.LocalBindingSha256 || task.Snapshot.MetadataSha256 != request.MetadataSha256
                || clock.GetUtcNow() >= task.ExpiresAt || task.Status is not ("queued" or "leased" or "preview_ready" or "dependencies_required") || used.Contains(task.RequestId)) throw new InvalidOperationException("确认已过期、跨快照或已归属其他应用组，不能扩大范围。");
        }
        if (!tasks.Select(task => task.Snapshot.Action).Order(StringComparer.Ordinal).SequenceEqual(actions)) throw new InvalidOperationException("计划动作必须恰好等于用户分别确认的动作集合。");
        var now = clock.GetUtcNow(); var expires = now.AddSeconds(options.ApplyTicketSeconds); var earliest = tasks.Min(task => task.ExpiresAt); if (expires > earliest) expires = earliest;
        var ticket = new AssistantApplyTicket(request.GroupId, binding.BindingId, request.MetadataSha256, tasks.Select(Approval).OrderBy(approval => approval.Action, StringComparer.Ordinal).ToArray(), actions, request.PlanSha256, now, expires, "",origin);
        ticket = ticket with { TicketSha256 = Hash(ticket) };
        SaveGroup(new(origin is null?1:2, owner.AccountId, owner.ProjectId, owner.GrantId!.Value, binding.RootNamespace, binding.LocalBindingSha256, ticket, null, "")); return ticket;
    });

    /// <summary>本机应用前再次在线复核短ticket和当前发布快照；撤权由每次真实Agent认证拒绝。</summary>
    public AssistantApplyTicket VerifyTicket(AssistantOwner owner, AssistantTicketRequest request, string currentMetadataSha256,AssistantRuntimeOriginProof? proof=null) => Locked(() =>
    {
        RequireOwner(owner, true); var binding = ReadBinding(request.BindingId); RequireScope(binding, owner); var group = ReadGroup(request.GroupId); RequireGroup(group, owner, binding);
        _=RequireRuntimeSet(owner,binding,group.Ticket.Approvals.Select(item=>ReadTask(item.RequestId)).ToArray(),proof);
        if (group.Ticket.TicketSha256 != request.TicketSha256 || group.Ticket.PlanSha256 != request.PlanSha256 || group.Ticket.MetadataSha256 != currentMetadataSha256 || clock.GetUtcNow() >= group.Ticket.ExpiresAt || group.Receipt is not null) throw new InvalidOperationException("ticket过期、快照变化或已有结果，未授权新的应用。");
        return group.Ticket;
    });

    /// <summary>应用后可幂等回传同一结果；过期不能抹掉真实结果，撤权仍拒绝HTTP，未知不会成为成功。</summary>
    public AssistantApplicationReceipt ReportApplication(AssistantOwner owner, AssistantApplicationReceipt receipt) => Locked(() =>
    {
        RequireOwner(owner, true); ValidateApplication(receipt); var binding = ReadBinding(receipt.BindingId); RequireScope(binding, owner); var group = ReadGroup(receipt.GroupId); RequireGroup(group, owner, binding);
        if (group.Ticket.TicketSha256 != receipt.TicketSha256 || group.Ticket.PlanSha256 != receipt.PlanSha256) throw new UnauthorizedAccessException("应用回执不属于本机已批准组。");
        if (group.Receipt is not null) { if (Hash(group.Receipt) != Hash(receipt)) throw new InvalidOperationException("应用组已有不同结果，不能覆盖审计。"); return group.Receipt; }
        SaveGroup(group with { Receipt = receipt }); return receipt;
    });

    /// <summary>本机策略是固定三动作的显式集合，preview没有任何写权限。</summary>
    private static string[] Policy(string mode, string[]? actions)
    {
        actions ??= [];
        if (mode is not ("preview" or "apply") || mode == "preview" && actions.Length != 0 || mode == "apply" && actions.Length is < 1 or > 3 || actions.Distinct(StringComparer.Ordinal).Count() != actions.Length || actions.Any(action => !Actions.Contains(action, StringComparer.Ordinal))) throw new InvalidOperationException("本机写入模式或允许动作无效。");
        return actions.Order(StringComparer.Ordinal).ToArray();
    }
    /// <summary>审批快照摘要保持原始请求身份，不上传本机位置。</summary>
    private static AssistantApproval Approval(AssistantTask task) => new(task.RequestId, task.Snapshot.Action, task.Snapshot.RequestSha256, task.Snapshot.MetadataSha256);
    /// <summary>分组身份必须与当前真实grant和不可改绑摘要完全相符。</summary>
    private static void RequireGroup(AssistantApplyGroup group, AssistantOwner owner, AssistantBinding binding)
    { if (group.AccountId != owner.AccountId || group.ProjectId != owner.ProjectId || group.GrantId != owner.GrantId || group.RootNamespace != binding.RootNamespace || group.LocalBindingSha256 != binding.LocalBindingSha256 || group.Ticket.BindingId != binding.BindingId) throw new UnauthorizedAccessException("应用组不属于当前账号、项目、grant或绑定。"); }
    /// <summary>应用终态只允许有界计数和固定错误，任意stdout、路径或伪成功不能进入回执。</summary>
    private static void ValidateApplication(AssistantApplicationReceipt value)
    {
        Uuid(value.BindingId); Uuid(value.GroupId); RequireHash(value.TicketSha256); RequireHash(value.PlanSha256);
        if (new[] { value.WrittenFiles, value.DeletedFiles, value.PreservedFiles }.Any(count => count is < 0 or > 20000) || value.Status is not ("applied" or "rolled-back" or "manual-recovery") || value.Status == "applied" && value.ErrorCode is not null
            || value.Status != "applied" && (value.WrittenFiles != 0 || value.DeletedFiles != 0 || value.PreservedFiles != 0 || value.Status == "rolled-back" && value.ErrorCode != "application_rolled_back" || value.Status == "manual-recovery" && value.ErrorCode is not ("application_unknown" or "recovery_required"))) throw new InvalidOperationException("应用回执状态或计数无效。");
    }
    /// <summary>组日志有界、逐层拒绝链接，读盘重算ticket和完整持久记录摘要。</summary>
    private AssistantApplyGroup ReadGroup(string id)
    {
        Uuid(id); var group = Read<AssistantApplyGroup>("group-" + id + ".json"); var ticket = group.Ticket; var binding = ReadBinding(ticket.BindingId); RequireGroup(group, new(group.AccountId, group.ProjectId, group.GrantId), binding);
        if (group.SchemaVersion != (ticket.RuntimeOrigin is null?1:2) || ticket.GroupId != id || ticket.ExpiresAt <= ticket.IssuedAt || ticket.TicketSha256 != Hash(ticket with { TicketSha256 = "" }) || group.IntegritySha256 != Hash(group with { IntegritySha256 = "" })) throw new InvalidOperationException("持久应用组或ticket摘要无效。");
        Policy("apply", ticket.Actions); RequireHash(ticket.MetadataSha256); RequireHash(ticket.PlanSha256);
        if (ticket.Approvals is null || ticket.Approvals.Length != ticket.Actions.Length || !ticket.Approvals.Select(approval => approval.Action).SequenceEqual(ticket.Actions)) throw new InvalidOperationException("持久组动作与确认不一致。");
        foreach (var approval in ticket.Approvals) { var task = ReadTask(approval.RequestId); if (task.RuntimeOrigin!=ticket.RuntimeOrigin || ticket.RuntimeOrigin is not null&&ticket.Approvals.Length!=1 || Approval(task) != approval || task.BindingId != binding.BindingId || task.GrantId != group.GrantId || task.AccountId != group.AccountId || task.ProjectId != group.ProjectId || approval.MetadataSha256 != ticket.MetadataSha256) throw new InvalidOperationException("持久应用组确认身份无效。"); }
        if (group.Receipt is { } receipt) { ValidateApplication(receipt); if (receipt.GroupId != id || receipt.BindingId != binding.BindingId || receipt.TicketSha256 != ticket.TicketSha256 || receipt.PlanSha256 != ticket.PlanSha256) throw new InvalidOperationException("应用结果与原ticket不符。"); }
        return group;
    }
    /// <summary>所有组转换先刷盘，再回网络结果。</summary>
    private void SaveGroup(AssistantApplyGroup group) => Save("group-" + group.Ticket.GroupId + ".json", group with { IntegritySha256 = Hash(group with { IntegritySha256 = "" }) });
    /// <summary>只扫描服务器固定前缀；未知损坏日志阻断，不隐式抢占已有组。</summary>
    private AssistantApplyGroup[] Groups() => Directory.GetFiles(options.RootDirectory, "group-*.json").Select(path => ReadGroup(Path.GetFileName(path)[6..^5])).ToArray();
}
