using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace {{NAMESPACE}}.Host;

/// <summary>仅显式开发账户使用的本机私有凭据摘要；固定路径、原子刷盘，错误不回退复活配置密码。</summary>
internal sealed class DevelopmentPasswordStore(string contentRoot, string projectId, string username)
{
    internal sealed record Credential(int SchemaVersion, string ProjectId, string Username, string Algorithm, int Iterations, string Salt, string PasswordHash);
    private const int Iterations = 600000;
    private readonly string folder = Path.Combine(Path.GetFullPath(contentRoot), ".local", "development-auth");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false, WriteIndented = true, NewLine = "\n" };
    private string State => Path.Combine(folder, "credentials.json");
    /// <summary>启动不创建文件；已存在的私有目录缺少凭据表示中断/人为删除，必须保留现场而非回退原密码。</summary>
    public Credential? Read()
    {
        Guard(State);
        if (!Directory.Exists(folder)) return null;
        Secure(folder, true, false);
        if (!File.Exists(State)) throw new InvalidOperationException("开发凭据状态缺失，请保留私有目录核查，未恢复旧配置密码。");
        Secure(State, false, false);
        var info = new FileInfo(State); if (info.Length > 4096) throw new InvalidOperationException("开发凭据状态超限。");
        var bytes = File.ReadAllBytes(State);
        using var document = JsonDocument.Parse(bytes);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Any(field => !keys.Add(field.Name))) throw new InvalidOperationException("开发凭据状态重复或格式无效。");
        var value = JsonSerializer.Deserialize<Credential>(bytes, Json) ?? throw new InvalidOperationException("开发凭据状态为空。");
        if (value.SchemaVersion != 1 || value.ProjectId != projectId || value.Username != username || value.Algorithm != "PBKDF2-SHA256" || value.Iterations != Iterations
            || Convert.FromBase64String(value.Salt).Length != 16 || Convert.FromBase64String(value.PasswordHash).Length != 32) throw new InvalidOperationException("开发凭据状态与显式账户/项目不一致，未回退旧配置密码。");
        return value;
    }
    /// <summary>随机盐和固定PBKDF2成本，文件与内存不保存明文新密码。</summary>
    public Credential Create(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16); var derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        try { return new(1,projectId,username,"PBKDF2-SHA256",Iterations,Convert.ToBase64String(salt),Convert.ToBase64String(derived)); }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }
    /// <summary>固定长度摘要比较，不能将文件中的算法或迭代次数当可执行选择。</summary>
    public static bool Verify(Credential credential, string password)
    {
        var actual = Rfc2898DeriveBytes.Pbkdf2(password,Convert.FromBase64String(credential.Salt),Iterations,HashAlgorithmName.SHA256,32);
        try { return CryptographicOperations.FixedTimeEquals(actual,Convert.FromBase64String(credential.PasswordHash)); }
        finally { CryptographicOperations.ZeroMemory(actual); }
    }
    /// <summary>并发改密先重读原摘要，再写同目录新文件并Flush；只有完整替换成功后调用方才能撤销会话。</summary>
    public void Write(Credential expected, Credential next)
    {
        Guard(State); var created = !Directory.Exists(folder); Directory.CreateDirectory(folder); Secure(folder,true,created);
        var temporary = Path.Combine(folder,".credentials-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            var mutexPath=Path.Combine(folder,".write.lock"); Guard(mutexPath);
            using(var mutex=new FileStream(mutexPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,1,FileOptions.DeleteOnClose))
            {
                Secure(mutexPath,false,true);
                if (File.Exists(State)) { var current=Read()!; if(current != expected) throw new InvalidOperationException("开发凭据已被另一进程修改，请重新认证。"); }
                else if (!created) throw new InvalidOperationException("开发凭据状态缺失，未回退旧密码。");
                var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(next,Json)+"\n");
                Guard(temporary); using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {Secure(temporary,false,true);stream.Write(bytes);stream.Flush(true);}
                Guard(State); File.Move(temporary,State,true); Secure(State,false,false);
            }
        }
        catch
        {
            // 仅撤除本次新建且仍为空的专属目录；不递归清理，也不删除已有凭据/现场。
            if(File.Exists(temporary))File.Delete(temporary);
            if(created && !File.Exists(State) && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder,false);
            throw;
        }
    }
    /// <summary>每级固定祖先和文件都拒绝链接，不能借本机密码接口写到任意目录。</summary>
    private static void Guard(string path)
    {
        for(var current=path;current is not null;current=Path.GetDirectoryName(current))
        {
            // File.Exists会把断链视为不存在；直接检查属性，不能将损坏链接当作首次初始化。
            FileAttributes attributes;
            try { attributes=File.GetAttributes(current); }
            catch(FileNotFoundException) { continue; }
            catch(DirectoryNotFoundException) { continue; }
            if((attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("开发凭据路径不能经过链接。");
        }
    }
    /// <summary>仅新建的专属目录/文件设置权限；既有状态权限不可信则拒绝，不改变其他宿主目录ACL。</summary>
    private static void Secure(string path,bool directory,bool create)
    {
        Guard(path);
        if(OperatingSystem.IsWindows())
        {
            var owner=WindowsIdentity.GetCurrent().User!;
            FileSystemSecurity security=directory?new DirectoryInfo(path).GetAccessControl():new FileInfo(path).GetAccessControl();
            if(create)
            {
                security=directory?new DirectorySecurity():new FileSecurity();security.SetOwner(owner);security.SetAccessRuleProtection(true,false);
                security.AddAccessRule(new FileSystemAccessRule(owner,FileSystemRights.FullControl,directory?InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit:InheritanceFlags.None,PropagationFlags.None,AccessControlType.Allow));
                if(directory)new DirectoryInfo(path).SetAccessControl((DirectorySecurity)security);else new FileInfo(path).SetAccessControl((FileSecurity)security);
            }
            if(!owner.Equals(security.GetOwner(typeof(SecurityIdentifier)))||security.GetAccessRules(true,true,typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Any(rule=>rule.AccessControlType==AccessControlType.Allow&&!owner.Equals(rule.IdentityReference)))throw new InvalidOperationException("开发凭据权限必须仅允许当前用户。");
        }
        else
        {
            var allowed=UnixFileMode.UserRead|UnixFileMode.UserWrite|(directory?UnixFileMode.UserExecute:0);
            if(create)File.SetUnixFileMode(path,allowed);
            if((File.GetUnixFileMode(path)&~allowed)!=0)throw new InvalidOperationException("开发凭据权限必须仅允许当前用户。");
        }
    }
}
