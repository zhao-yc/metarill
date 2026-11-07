using System.Runtime.CompilerServices;

// 平台锁后读取继续复用内部决策；不为跨程序集提取开放锁集合或准备状态。
[assembly: InternalsVisibleTo("Metarill.Runtime")]
[assembly: InternalsVisibleTo("Metarill.Security.Tests")]
