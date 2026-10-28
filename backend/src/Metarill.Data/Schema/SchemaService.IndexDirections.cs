using System.Data.Common;
using System.Text.RegularExpressions;

namespace Metarill.Data.Schema;

public sealed partial class SchemaService
{
    /// <summary>MySQL旧版会忽略DESC；预览和实际执行均验证真实握手版本，避免降级为不同物理结构。</summary>
    private static void RequireIndexDirectionSupport(DbConnection connection,DatabaseSchema desired)
    {
        if(!desired.Tables.SelectMany(table=>table.Indexes??[]).Any(index=>IndexDirectionRules.Read(index).Contains("DESC",StringComparer.Ordinal)))return;
        var version=connection.ServerVersion;
        var match=Regex.Match(version,@"^([0-9]+[.][0-9]+(?:[.][0-9]+)?)(?:[-+][0-9A-Za-z.+-]+)?$",RegexOptions.CultureInvariant);
        if(version.Contains("MariaDB",StringComparison.OrdinalIgnoreCase)||!match.Success||!Version.TryParse(match.Groups[1].Value,out var parsed)||parsed<new Version(8,0,11))
            throw new NotSupportedException("降序索引需要已核实的MySQL 8.0.11及以上正式版InnoDB；当前服务器尚未证明支持，未执行DDL。");
    }
}
