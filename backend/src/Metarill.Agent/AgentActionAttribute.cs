namespace Metarill.Runtime.Agent;

/// <summary>仅显式声明的手写MVC动作进入Agent目录；声明本身不替代Web入口或Service鉴权。</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class AgentActionAttribute(string id, string name, string buttonId) : Attribute
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string ButtonId { get; } = buttonId;
    public string Description { get; set; } = "";
    public string RecordIdField { get; set; } = "id";
}
