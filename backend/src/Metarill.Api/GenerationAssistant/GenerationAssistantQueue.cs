using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Metarill.Generation;
using Metarill.Runtime.Agent;

namespace Metarill.Api.GenerationAssistant;

/// <summary>单服务器持久队列：每次持锁重新读盘，原子刷盘后才返回；重启不依赖内存状态。</summary>
public sealed partial class GenerationAssistantQueue
{
    private readonly GenerationAssistantOptions options;
    private readonly TimeProvider clock;
    private readonly object sync = new();
    private static readonly string[] Actions = ["generate-project-config", "sync-runtime", "generate-all-page"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true, NewLine = "\n" };
    /// <summary>先固定服务器状态根与有界限制，构造时不创建或扫描本机业务目录。</summary>
    public GenerationAssistantQueue(GenerationAssistantOptions options, TimeProvider? timeProvider = null)
    {
        this.options = options with { RootDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.RootDirectory)) }; clock = timeProvider ?? TimeProvider.System;
        if (options.RequestTtlSeconds is < 30 or > 86400 || options.LeaseSeconds is < 10 or > 3600 || options.LeaseSeconds > options.RequestTtlSeconds || options.MaxTasks is < 1 or > 2000 || options.MaxBindings is < 1 or > 1000 || options.ApplyTicketSeconds is < 10 or > 300 || this.options.RootDirectory == Path.GetPathRoot(this.options.RootDirectory)) throw new InvalidOperationException("生成助手队列限制或服务器状态目录无效。");
        GuardPath(this.options.RootDirectory);
    }

    /// <summary>注册只绑定当前真实grant；重复相同注册可回读，其他grant不能接管同一binding。</summary>
    public AssistantBindingView Bind(AssistantOwner owner, AssistantBindRequest request) => Locked(() =>
    {
        RequireOwner(owner, true); Uuid(request.BindingId); Namespace(request.RootNamespace); RequireHash(request.LocalBindingSha256); var allowed = Policy(request.Mode, request.AllowedActions);
        var path = "binding-" + request.BindingId + ".json";
        if (File.Exists(Safe(path)))
        {
            var old = ReadBinding(request.BindingId); RequireScope(old, owner);
            if (old.RootNamespace != request.RootNamespace || old.LocalBindingSha256 != request.LocalBindingSha256 || old.Mode != request.Mode || !old.AllowedActions.SequenceEqual(allowed)) throw new InvalidOperationException("本机绑定身份已变化，请使用新的绑定编号。");
            return BindingView(old);
        }
        if (Directory.GetFiles(options.RootDirectory, "binding-*.json").Length >= options.MaxBindings) throw new InvalidOperationException("生成助手绑定数量已达到上限。");
        var binding = new AssistantBinding(1, owner.AccountId, owner.ProjectId, owner.GrantId!.Value, request.BindingId, request.RootNamespace, request.LocalBindingSha256, request.Mode, allowed, "");
        Save(path, binding with { IntegritySha256 = Hash(binding) }); return BindingView(binding);
    });

    /// <summary>Web只对自身账号项目的既有绑定排队；UUID绑定完整快照，重复不会再登记或改内容。</summary>
    public AssistantTaskView Enqueue(AssistantOwner owner, string requestId, string bindingId, GenerationUpdateRequest snapshot) => Locked(() => EnqueueCore(owner,requestId,bindingId,snapshot,null,null));

    /// <summary>分端只能由已逐项目真实认证的服务器协调器入队；当前设备配置及旧关联再次复核，不能沿普通v1入口偷渡。</summary>
    public AssistantTaskView EnqueueRuntime(AssistantOwner owner,string requestId,string bindingId,GenerationUpdateRequest snapshot,AssistantRuntimeOrigin origin,AssistantDeviceScope current)
        =>Locked(()=>EnqueueCore(owner,requestId,bindingId,snapshot,origin,current));

    /// <summary>两种协议共享有界、幂等和刷盘顺序；v1缺省字段仍不序列化。</summary>
    private AssistantTaskView EnqueueCore(AssistantOwner owner,string requestId,string bindingId,GenerationUpdateRequest snapshot,AssistantRuntimeOrigin? origin,AssistantDeviceScope? current)
    {
        RequireOwner(owner, false); Uuid(requestId); var binding = ReadBinding(bindingId); RequireScope(binding, owner);
        LocalGenerationUpdater.ValidateRequest(snapshot);
        if (snapshot.ProjectId != owner.ProjectId) throw new UnauthorizedAccessException("离线快照不属于当前项目。");
        ValidateRuntimeSnapshot(owner,snapshot,origin);
        if(origin is not null)RequireCurrentRuntimeOrigin(owner,binding,origin,current??throw new UnauthorizedAccessException("分端入队缺少本次实际配置复核。"));
        var path = "task-" + requestId + ".json";
        if (File.Exists(Safe(path)))
        {
            var old = Expire(ReadTask(requestId)); RequireScope(old, owner);
            if (old.BindingId != bindingId || old.Snapshot.RequestSha256 != snapshot.RequestSha256 || old.RuntimeOrigin!=origin) throw new InvalidOperationException("同一任务UUID不能绑定其他动作或快照。");
            return View(old);
        }
        if (Directory.GetFiles(options.RootDirectory, "task-*.json").Length >= options.MaxTasks) throw new InvalidOperationException("生成助手任务已达到有界保留上限，请先审阅和归档旧记录。");
        var now = clock.GetUtcNow();
        var task = new AssistantTask(origin is null?1:2, owner.AccountId, owner.ProjectId, binding.GrantId, requestId, bindingId, binding.RootNamespace, binding.LocalBindingSha256, snapshot, now, now.AddSeconds(options.RequestTtlSeconds), "queued", null, null, null, "",origin);
        SaveTask(task); return View(task);
    }

    /// <summary>相同绑定的有效lease可重复回读；过期任务终结且不自动重排，不把网络重试当新授权。</summary>
    public AssistantWork? Claim(AssistantOwner owner, string bindingId,AssistantRuntimeOriginProof? proof=null) => Locked(() =>
    {
        RequireOwner(owner,true);var binding=ReadBinding(bindingId);RequireScope(binding,owner);var next=NextClaimable(owner,bindingId);
        if(next is null)return null;
        _=RequireRuntimeSet(owner,binding,[next],proof);
        return LeaseWork(next);
    });

    /// <summary>领取转换共用原lease语义，v1控制器无须先只读扫描再重复扫描。</summary>
    private AssistantWork LeaseWork(AssistantTask next)
    {
        if(next.Status=="leased")return Work(next);
        var expires=clock.GetUtcNow().AddSeconds(options.LeaseSeconds);if(expires>next.ExpiresAt)expires=next.ExpiresAt;
        next=next with{Status="leased",LeaseId=Guid.NewGuid().ToString("D"),LeaseExpiresAt=expires};SaveTask(next);return Work(next);
    }

    /// <summary>与Claim在同一文件锁下使用相同排序，控制器只为将领取的那一项读取当前设备事实。</summary>
    private AssistantTask? NextClaimable(AssistantOwner owner,string bindingId)
    {
        var tasks=Directory.GetFiles(options.RootDirectory,"task-*.json").Select(path=>Expire(ReadTask(Path.GetFileName(path)[5..^5])))
            .Where(task=>task.AccountId==owner.AccountId&&task.ProjectId==owner.ProjectId&&task.GrantId==owner.GrantId&&task.BindingId==bindingId)
            .OrderBy(task=>task.CreatedAt).ThenBy(task=>task.RequestId,StringComparer.Ordinal).ToArray();
        var grouped=Groups().SelectMany(group=>group.Ticket.Approvals.Select(approval=>approval.RequestId)).ToHashSet(StringComparer.Ordinal);
        tasks=tasks.Where(task=>!grouped.Contains(task.RequestId)).ToArray();
        return tasks.FirstOrDefault(task=>task.Status=="leased")??tasks.FirstOrDefault(task=>task.Status=="queued");
    }

    /// <summary>只接收本次grant与lease的预览回执；重复相同回执幂等，变造状态或摘要拒绝。</summary>
    public AssistantTaskView Report(AssistantOwner owner, AssistantPreviewReceipt receipt) => Locked(() =>
    {
        RequireOwner(owner, true); ValidateReceipt(receipt); var binding = ReadBinding(receipt.BindingId); RequireScope(binding, owner);
        var task = Expire(ReadTask(receipt.RequestId)); RequireScope(task, owner);
        if (task.BindingId != receipt.BindingId || task.LeaseId != receipt.LeaseId || task.Snapshot.RequestSha256 != receipt.RequestSha256) throw new UnauthorizedAccessException("预览回执不属于本次任务、绑定或lease。");
        if (task.Preview is not null)
        {
            if (Hash(task.Preview) != Hash(receipt)) throw new InvalidOperationException("任务已存在不同的预览回执，不能覆盖。");
            return View(task);
        }
        if (task.Status != "leased") throw new InvalidOperationException("任务未领取或已过期，不能回传为完成。");
        task = task with { Status = receipt.Status.Replace('-', '_'), Preview = receipt }; SaveTask(task); return View(task);
    });

    /// <summary>网页只查询本账号项目，回执明确未应用，不返回授权token或任何服务器/本机路径。</summary>
    public AssistantTaskView Get(AssistantOwner owner, string requestId) => Locked(() => { RequireOwner(owner, false); var task = ReadTask(requestId); RequireScope(task, owner); return View(Expire(task)); });

    /// <summary>绑定检查供入队前先鉴权，未读取完整元数据之前拒绝跨账号/项目目标。</summary>
    public long RequireBinding(AssistantOwner owner, string bindingId) => Locked(() => { RequireOwner(owner, false); var binding = ReadBinding(bindingId); RequireScope(binding, owner); return binding.GrantId; });

    /// <summary>HTTP正文使用相同严格JSON：大小、未知字段和逐层重复键都拒绝。</summary>
    public static T Parse<T>(JsonElement value) => Decode<T>(Encoding.UTF8.GetBytes(value.GetRawText()), 32768);
    /// <summary>调用角色必须符合真实Web或Agent身份，不能用选择字段替代grant。</summary>
    private static void RequireOwner(AssistantOwner owner, bool agent)
    {
        if (owner.AccountId <= 0 || !long.TryParse(owner.ProjectId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 || id.ToString(CultureInfo.InvariantCulture) != owner.ProjectId || agent && owner.GrantId is not > 0 || !agent && owner.GrantId is not null) throw new UnauthorizedAccessException("生成助手身份类型或项目无效。");
    }
    /// <summary>绑定始终限定账号、项目以及Agent自己的grant。</summary>
    private static void RequireScope(AssistantBinding binding, AssistantOwner owner) { if (binding.AccountId != owner.AccountId || binding.ProjectId != owner.ProjectId || owner.GrantId is not null && binding.GrantId != owner.GrantId) throw new UnauthorizedAccessException("本机绑定不属于当前账号、项目或grant。"); }
    /// <summary>任务读取与回传不能跨账号、项目或grant。</summary>
    private static void RequireScope(AssistantTask task, AssistantOwner owner) { if (task.AccountId != owner.AccountId || task.ProjectId != owner.ProjectId || owner.GrantId is not null && task.GrantId != owner.GrantId) throw new UnauthorizedAccessException("任务不属于当前账号、项目或grant。"); }
    /// <summary>复用现有Agent小写UUID合同。</summary>
    private static void Uuid(string value) => AgentTaskStore.ValidateRequestId(value);
    /// <summary>所有摘要只接受规范小写SHA-256。</summary>
    private static void RequireHash(string? value) { if (value is null || !Regex.IsMatch(value, "^[a-f0-9]{64}$")) throw new InvalidOperationException("生成助手摘要无效。"); }
    /// <summary>绑定namespace只能是有界C#名称，不能当作路径或表达式。</summary>
    private static void Namespace(string value) { if (value is null || value.Length > 200 || !Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")) throw new InvalidOperationException("本机命名空间无效。"); }
    /// <summary>注册回执不含服务器路径或任何凭据。</summary>
    private static AssistantBindingView BindingView(AssistantBinding binding) => new(binding.BindingId, binding.ProjectId, binding.RootNamespace, binding.LocalBindingSha256, binding.Mode, binding.AllowedActions);
    /// <summary>预览状态统一声明not_applied，不借用业务成功状态。</summary>
    private AssistantTaskView View(AssistantTask task)
    {
        var group = Groups().SingleOrDefault(group => group.Ticket.Approvals.Any(approval => approval.RequestId == task.RequestId));
        var result = group?.Receipt;
        return new(task.RequestId, task.BindingId, task.ProjectId, task.Snapshot.Action, result?.Status.Replace('-', '_') ?? (group is null ? task.Status : "awaiting_application"), task.Snapshot.RequestSha256, task.CreatedAt, task.ExpiresAt, task.Preview,
            result?.Status == "applied", result?.Status == "applied" ? "applied" : result?.Status == "rolled-back" ? "rolled_back" : group is null ? "not_applied" : "unknown", result);
    }
    /// <summary>领取只发送固定快照与短lease，不发送可执行命令。</summary>
    private static AssistantWork Work(AssistantTask task) => new(task.RequestId, task.BindingId, task.ProjectId, task.RootNamespace, task.LocalBindingSha256, task.LeaseId!, task.LeaseExpiresAt!.Value, task.Snapshot,false,task.RuntimeOrigin);
    /// <summary>到期只持久终结，禁止静默产生新lease或重新排队。</summary>
    private AssistantTask Expire(AssistantTask task)
    {
        if (task.Status is "queued" or "leased" && (clock.GetUtcNow() >= task.ExpiresAt || task.LeaseExpiresAt is { } lease && clock.GetUtcNow() >= lease)) { task = task with { Status = "expired" }; SaveTask(task); }
        return task;
    }
    /// <summary>状态仅描述预览，不接受succeeded/applied或自由文本诊断。</summary>
    private static void ValidateReceipt(AssistantPreviewReceipt value)
    {
        Uuid(value.BindingId); Uuid(value.RequestId); Uuid(value.LeaseId); RequireHash(value.RequestSha256);
        if (value.RequiredActions is null || value.RequiredActions.Length > 3 || value.RequiredActions.Distinct(StringComparer.Ordinal).Count() != value.RequiredActions.Length || value.RequiredActions.Any(action => !Actions.Contains(action, StringComparer.Ordinal)) || new[] { value.CreatedFiles, value.ReplacedFiles, value.DeletedFiles, value.PreservedFiles }.Any(count => count is < 0 or > 20000)) throw new InvalidOperationException("预览范围或计数无效。");
        if (value.Status == "failed") { if (value.PlanSha256 is not null || value.RequiredActions.Length != 0 || value.CreatedFiles + value.ReplacedFiles + value.DeletedFiles + value.PreservedFiles != 0 || value.ErrorCode is not ("prepare_failed" or "local_prepare_interrupted" or "local_binding_changed")) throw new InvalidOperationException("失败回执只能包含稳定错误代码。"); }
        else { RequireHash(value.PlanSha256); if (value.ErrorCode is not null || value.Status == "preview-ready" && value.RequiredActions.Length != 0 || value.Status == "dependencies-required" && value.RequiredActions.Length == 0 || value.Status is not ("preview-ready" or "dependencies-required")) throw new InvalidOperationException("只接受明确未应用的预览回执。"); }
    }
}
