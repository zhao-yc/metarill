using System.Runtime.CompilerServices;

// 保留原控制器类型查找，实际MVC应用部件在Program显式注册共享Agent程序集。
[assembly: TypeForwardedTo(typeof(Metarill.Api.AgentControllers.AgentOAuthController))]
[assembly: TypeForwardedTo(typeof(Metarill.Api.AgentControllers.AgentManagementController))]
