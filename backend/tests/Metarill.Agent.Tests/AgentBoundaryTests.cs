using Metarill.Runtime.Agent;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Metarill.Agent.Tests;

public sealed class AgentBoundaryTests
{
    [Fact]
    public void PublicRateLimitIsBoundedBySourceAndResetsOnlyAfterWindow()
    {
        var clock = new AgentClock(); var limiter = new AgentPublicRateLimiter(clock);
        limiter.Require("device", "test-source", 1);
        Assert.Equal("slow_down", Assert.Throws<AgentOAuthException>(() => limiter.Require("device", "test-source", 1)).Error);
        limiter.Require("device", "other-source", 1); clock.Advance(60); limiter.Require("device", "test-source", 1);
    }
    [Fact]
    public void WebAndAgentManagementDoNotAcceptForgedTransport()
    {
        var context = new DefaultHttpContext();
        var identity = new AuthenticatedIdentity(new(1, "虚构", true, "1", "1"), new HashSet<string>(), 1, 2);
        context.Items[typeof(AgentRequestIdentity)] = new AgentRequestIdentity(identity, "agent", 2);
        Assert.Throws<UnauthorizedAccessException>(() => AgentRequestAuthentication.RequireWeb(context));
        context.Items[typeof(AgentRequestIdentity)] = new AgentRequestIdentity(identity with { GrantId = null }, "web", null);
        Assert.Throws<UnauthorizedAccessException>(() => AgentRequestAuthentication.RequireAgent(context));
    }
}
