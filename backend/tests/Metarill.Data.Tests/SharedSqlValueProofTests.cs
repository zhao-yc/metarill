using System.Runtime.CompilerServices;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>Java与C#共同读取独立Python标准库生成的值证明向量；不连接数据库。</summary>
public sealed class SharedSqlValueProofTests
{
    /// <summary>固定16值包含高位整数、精确decimal、浮点位型、空值及日历时间，不能用序列化字符串冒充原值。</summary>
    [Fact]
    public void Shared_v1_vectors_match_actual_CSharp_command_parameter_capture()
    {
        var values=new (string Name,object? Value)[]{("null",null),("empty",""),("text","中文'\\常量"),("id",9007199254740993L),("decimal",0.1234567890123456789012345678m),("decimal-zero",0.00m),("double",Math.ScaleB(1d,-958)),("negative-zero",BitConverter.UInt64BitsToDouble(0x8000000000000000UL)),("single",1.25f),("bytes",new byte[]{0,1,255}),("boolean",true),("date",new DateOnly(2026,11,2)),("datetime",new DateTime(2026,11,2,3,4,5,DateTimeKind.Unspecified).AddTicks(1234560)),("time",new TimeOnly(3,4,5).Add(TimeSpan.FromTicks(1234560))),("duration",TimeSpan.FromHours(26)+TimeSpan.FromTicks(1234560)),("guid",Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"))};
        using var original=new DatabaseCommandObservationTests.Command();using var command=new MySqlFloatingCommand(original);using var trace=DatabaseCommandObservation.Begin(Enumerable.Repeat((byte)11,32).ToArray());
        foreach(var (name,value) in values){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value;command.Parameters.Add(parameter);}
        command.ExecuteScalar();trace.Dispose();Assert.True(trace.Snapshot().InternallyConsistent);var captured=Assert.Single(trace.Snapshot().Commands).Parameters;
        var lines=File.ReadAllLines(Path.Combine(Root(),"tools/performance/sql-value-proof-v1.tsv"));Assert.Equal(values.Length,lines.Length);Assert.Equal(values.Length,captured.Count);
        for(var index=0;index<lines.Length;index++){var expected=lines[index].Split('\t');Assert.Equal(3,expected.Length);Assert.Equal(values[index].Name,expected[0]);Assert.Equal(expected[0],captured[index].Name);Assert.Equal(expected[1],captured[index].ValueKind);Assert.Equal(expected[2],captured[index].ValueHash);}
    }
    /// <summary>只定位仓库内共同向量，不读取私有设置或环境变量。</summary>
    private static string Root([CallerFilePath]string path="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,"../../.."));
}
