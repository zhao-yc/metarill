using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Text.Json.Serialization;
using MySqlConnector;

namespace Metarill.Agent.Tests;

internal sealed record RealmFailure(string Type,[property:JsonPropertyName("hresult")]int HResult,int? Mysql);
internal sealed record RealmTaskFault(int TaskIndex,IReadOnlyList<RealmFailure> Failures);
internal sealed record RealmObservation(string Key,Func<Task<object>> Read);

/// <summary>仅供RR测试记录安全故障元信息，绝不把异常正文、SQL、连接或凭据写入receipt。</summary>
internal static class SecurityRealmRrDiagnostics
{
    /// <summary>包装异常仍保留内部MySQL号，静态类型/HResult描述最外层失败；不序列化异常对象。</summary>
    internal static RealmFailure Safe(Exception error)
    {
        int? Mysql(Exception current)=>current is MySqlException mysql?mysql.Number:current.InnerException is{} inner?Mysql(inner):null;
        return new(error.GetType().Name,error.HResult,Mysql(error));
    }
    /// <summary>每次观测独立失败/继续，最后用原异常对象和原堆栈重新抛出，观测失败不能替换业务失败。</summary>
    internal static async Task RethrowAfterObservationsAsync(Exception original,ConcurrentDictionary<string,object> evidence,IReadOnlyList<RealmObservation> observations)
    {
        evidence["operationFailure"]=Safe(original);
        foreach(var observation in observations)
        {
            try{evidence[observation.Key]=await observation.Read();}
            catch(Exception error){evidence[observation.Key+"Failure"]=Safe(error);}
        }
        ExceptionDispatchInfo.Capture(original).Throw();
    }
    /// <summary>排空后保存每个Faulted任务的全部扁平故障；预期拒权也记录，不把它自动当新的测试失败。</summary>
    internal static RealmTaskFault[] TaskFaults(IReadOnlyList<Task> tasks)=>tasks.Select((task,index)=>(task,index)).Where(item=>item.task.IsFaulted)
        .Select(item=>new RealmTaskFault(item.index,item.task.Exception!.Flatten().InnerExceptions.Select(Safe).ToArray())).ToArray();
}
