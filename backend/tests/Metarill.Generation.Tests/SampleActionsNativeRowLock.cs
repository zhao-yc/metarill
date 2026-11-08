using System.Data;
using System.Globalization;
using System.Text.Json;
using Metarill.Data;
using MySqlConnector;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>只在正式独占库中持有合成合同主键，并要求独立观察到真实等待后才释放。</summary>
internal static class SampleActionsNativeRowLock
{
    internal static async Task<T> RunAsync<T>(DatabaseOptions options,string evidence,string run,long recordId,
        string name,bool commit,Func<CancellationToken,Task<T>> send)
    {
        var phase=commit?"commit":"rollback";
        var requestFile=Path.Combine(evidence,$"lock-{phase}-request.json");
        var proofFile=Path.Combine(evidence,$"lock-{phase}-observed.json");
        Assert.False(File.Exists(requestFile)||File.Exists(proofFile));
        using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var token=limit.Token;
        await using var connection=new MySqlConnection(options.ConnectionString);
        await connection.OpenAsync(token);
        await using var identity=connection.CreateCommand();
        identity.CommandText="SELECT CONNECTION_ID()";
        var blocker=Convert.ToInt64(await identity.ExecuteScalarAsync(token),CultureInfo.InvariantCulture);
        Assert.True(blocker>0);
        await using var transaction=await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead,token);
        await using var update=connection.CreateCommand();
        update.Transaction=transaction;
        update.CommandText="UPDATE sample_contract SET name=@name WHERE id=@id";
        update.Parameters.AddWithValue("name",name);
        update.Parameters.AddWithValue("id",recordId);
        Assert.Equal(1,await update.ExecuteNonQueryAsync(token));

        try
        {
            var record=recordId.ToString(CultureInfo.InvariantCulture);
            var temporary=requestFile+".tmp";
            await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(new{run,phase,blocker,record}),token);
            File.Move(temporary,requestFile);
            var waiting=send(token);
            while(!File.Exists(proofFile))
            {
                Assert.False(waiting.IsCompleted,"业务请求在独立证实 MySQL 行锁等待前结束。");
                await Task.Delay(TimeSpan.FromMilliseconds(100),token);
            }
            using var document=JsonDocument.Parse(await File.ReadAllBytesAsync(proofFile,token));
            var proof=document.RootElement;
            Assert.Equal(run,proof.GetProperty("run").GetString());
            Assert.Equal(phase,proof.GetProperty("phase").GetString());
            Assert.Equal(record,proof.GetProperty("record").GetString());
            Assert.Equal(blocker,proof.GetProperty("blocking").GetInt64());
            var requester=proof.GetProperty("requesting").GetInt64();
            Assert.True(requester>0&&requester!=blocker);
            Assert.False(waiting.IsCompleted,"业务请求在阻塞连接释放前结束。");

            if(commit)await transaction.CommitAsync(token);
            else await transaction.RollbackAsync(token);
            return await waiting.WaitAsync(token);
        }
        finally
        {
            // 观测缺失时取消本轮 HTTP；未完成的独占写事务由 Dispose 回滚。
            await limit.CancelAsync();
        }
    }
}
