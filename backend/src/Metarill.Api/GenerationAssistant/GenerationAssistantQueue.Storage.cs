using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Generation;

namespace Metarill.Api.GenerationAssistant;

public sealed partial class GenerationAssistantQueue
{
    private sealed record LegacyAssistantBinding(int SchemaVersion,long AccountId,string ProjectId,long GrantId,string BindingId,string RootNamespace,string LocalBindingSha256,string IntegritySha256);
    /// <summary>同进程串行并持跨进程文件锁，状态只能在真正刷盘后供下一请求读取。</summary>
    private T Locked<T>(Func<T> operation)
    {
        lock (sync)
        {
            GuardPath(options.RootDirectory); Directory.CreateDirectory(options.RootDirectory); GuardPath(options.RootDirectory);
            FileStream mutex;
            try { mutex = new FileStream(Safe(".queue.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
            catch (IOException error) { throw new InvalidOperationException("生成助手队列正在处理另一请求，请按原UUID查询后重试。", error); }
            using (mutex) return operation();
        }
    }
    /// <summary>服务器根目录只与固定文件名组合，逐层拒绝链接，无法由binding或UUID变成任意路径。</summary>
    private string Safe(string name)
    {
        if (name != ".queue.lock" && !Regex.IsMatch(name,@"^device-[1-9][0-9]{0,18}-[1-9][0-9]{0,18}-[1-9][0-9]{0,18}\.json$") && !Regex.IsMatch(name, @"^(?:(?:binding|task|group)-[a-f0-9-]{36}\.json|\.write-[a-f0-9]{32}\.tmp)$")) throw new InvalidOperationException("生成助手状态文件名无效。");
        var path = Path.Combine(options.RootDirectory, name); GuardPath(path); return path;
    }
    /// <summary>普通目录联接、符号链接及文件链接均不作为状态来源。</summary>
    private static void GuardPath(string path)
    {
        for (var item = path; item is not null; item = Path.GetDirectoryName(item))
            if ((File.Exists(item) || Directory.Exists(item)) && (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("生成助手状态路径不能经过链接。");
    }
    /// <summary>临时文件与目标同目录，Flush(true)后原子替换；中断只会留下旧记录或完整新记录。</summary>
    private void Save<T>(string name, T value)
    {
        var bytes = Encode(value); if (bytes.Length > 8 * 1024 * 1024) throw new InvalidOperationException("生成助手状态超过8MiB限制。");
        var temporary = Safe(".write-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); } File.Move(temporary, Safe(name), true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    /// <summary>状态文件有界读取，路径或协议未知即停止。</summary>
    private T Read<T>(string name) { var path = Safe(name); var info = new FileInfo(path); if (!info.Exists || info.Length > 8 * 1024 * 1024) throw new InvalidOperationException("生成助手状态不存在或大小无效。"); return Decode<T>(File.ReadAllBytes(path), 8 * 1024 * 1024); }
    /// <summary>读盘后重新验证身份、摘要和状态；损坏记录阻断，不悄悄创建替代任务。</summary>
    private AssistantBinding ReadBinding(string id)
    {
        Uuid(id); var value = Read<AssistantBinding>("binding-" + id + ".json"); RequireOwner(new(value.AccountId, value.ProjectId, value.GrantId), true); Namespace(value.RootNamespace); RequireHash(value.LocalBindingSha256);
        if (value.SchemaVersion != 1 || value.BindingId != id) throw new InvalidOperationException("本机绑定持久身份无效。");
        if (value.Mode is null && value.AllowedActions is null)
        {
            // 阶段一只预览记录按原字段顺序重算旧摘要，只读映射preview，不修改身份或静默升级写权限。
            var legacy = new LegacyAssistantBinding(value.SchemaVersion,value.AccountId,value.ProjectId,value.GrantId,value.BindingId,value.RootNamespace,value.LocalBindingSha256,"");
            if(value.IntegritySha256 != Hash(legacy)) throw new InvalidOperationException("旧preview绑定摘要无效，保留现场。");
            return value with {Mode="preview",AllowedActions=[]};
        }
        Policy(value.Mode ?? "", value.AllowedActions);
        if (value.IntegritySha256 != Hash(value with { IntegritySha256 = "" })) throw new InvalidOperationException("本机绑定持久摘要无效。"); return value;
    }
    /// <summary>重启重新验证完整快照、绑定、lease和终态回执。</summary>
    private AssistantTask ReadTask(string id)
    {
        Uuid(id); var value = Read<AssistantTask>("task-" + id + ".json"); RequireOwner(new(value.AccountId, value.ProjectId, value.GrantId), true); Namespace(value.RootNamespace); RequireHash(value.LocalBindingSha256); LocalGenerationUpdater.ValidateRequest(value.Snapshot);
        if (value.SchemaVersion != (value.RuntimeOrigin is null?1:2) || value.RequestId != id || value.Snapshot.ProjectId != value.ProjectId || value.ExpiresAt <= value.CreatedAt || value.Status is not ("queued" or "leased" or "preview_ready" or "dependencies_required" or "failed" or "expired") || value.IntegritySha256 != Hash(value with { IntegritySha256 = "" })) throw new InvalidOperationException("生成助手任务持久摘要或状态无效。");
        ValidateRuntimeSnapshot(new(value.AccountId,value.ProjectId,value.GrantId),value.Snapshot,value.RuntimeOrigin);
        var binding = ReadBinding(value.BindingId);
        if (binding.AccountId != value.AccountId || binding.ProjectId != value.ProjectId || binding.GrantId != value.GrantId || binding.RootNamespace != value.RootNamespace || binding.LocalBindingSha256 != value.LocalBindingSha256) throw new InvalidOperationException("任务与持久本机绑定不一致。");
        if (value.Status != "queued" && value.LeaseId is not null) Uuid(value.LeaseId);
        if (value.Status == "leased" && (value.LeaseId is null || value.LeaseExpiresAt is null || value.LeaseExpiresAt > value.ExpiresAt)) throw new InvalidOperationException("任务lease无效。");
        if (value.Preview is { } receipt) { ValidateReceipt(receipt); if (receipt.BindingId != value.BindingId || receipt.RequestId != id || receipt.LeaseId != value.LeaseId || receipt.RequestSha256 != value.Snapshot.RequestSha256 || value.Status != receipt.Status.Replace('-', '_')) throw new InvalidOperationException("持久回执与任务不一致。"); }
        else if (value.Status is "preview_ready" or "dependencies_required" or "failed") throw new InvalidOperationException("预览终态缺少真实回执。");
        return value;
    }
    /// <summary>每次状态转换重新绑定整个持久记录摘要。</summary>
    private void SaveTask(AssistantTask task) => Save("task-" + task.RequestId + ".json", task with { IntegritySha256 = Hash(task with { IntegritySha256 = "" }) });
    /// <summary>协议固定UTF8/LF和属性顺序。</summary>
    private static byte[] Encode<T>(T value) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json) + "\n");
    /// <summary>摘要检测字节漂移，不视作框架签名。</summary>
    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(Encode(value)));
    /// <summary>先限制大小与重复键，再执行拒绝未知字段的解码。</summary>
    private static T Decode<T>(byte[] bytes, int limit)
    {
        if (bytes.Length > limit) throw new InvalidOperationException("生成助手协议超过大小限制。");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 }); CheckKeys(document.RootElement); return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidOperationException("生成助手协议不能为空。");
    }
    /// <summary>递归拒绝大小写重名字段，避免解析器覆盖语义差异。</summary>
    private static void CheckKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object) { var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (var field in value.EnumerateObject()) { if (!names.Add(field.Name)) throw new InvalidOperationException("生成助手协议包含重复字段。"); CheckKeys(field.Value); } }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) CheckKeys(item);
    }
}
