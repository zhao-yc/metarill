namespace Metarill.Core.Contracts;

/// <summary>生成器固定普通Agent记录写入的原记录入口；页面/实体/Service不可由请求指定。</summary>
[AttributeUsage(AttributeTargets.Class,Inherited=false)]
public sealed class GeneratedAgentWriteAccessAttribute(string projectId,string pageId,string tableId,Type entityType,Type serviceType,string readMethod):Attribute
{
    public string ProjectId {get;}=projectId;
    public string PageId {get;}=pageId;
    public string TableId {get;}=tableId;
    public Type EntityType {get;}=entityType;
    public Type ServiceType {get;}=serviceType;
    public string ReadMethod {get;}=readMethod;
}
