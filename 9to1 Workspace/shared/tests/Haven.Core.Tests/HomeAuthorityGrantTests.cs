using System.Text.Json;
using Haven.Application;
using Haven.Core;
using NineToOne.Cui.AI;

namespace Haven.Core.Tests;

/// <summary>
/// Protects the Home-scoped authority seams resolved by the canonical composition for MCP and Computer Use.
/// </summary>
public sealed class HomeAuthorityGrantTests
{
    [Fact]
    public async Task McpAuthorityRefusesWithoutAGrantAndAcceptsTheExactConnectionScope()
    {
        var engine = new PermissionDecisionEngine();
        var authority = new HomeMcpInvocationAuthorizer(engine);
        var connection = ReadyConnection();
        var arguments = JsonSerializer.SerializeToElement(new { path = "/Verse/Test.verse" });

        Assert.False(await authority.AuthorizeAsync(connection, "read_verse", arguments, CancellationToken.None));

        engine.Grant(ExternalConnectionNaming.CapabilityKey(connection.Id));
        Assert.True(await authority.AuthorizeAsync(connection, "read_verse", arguments, CancellationToken.None));

        engine.Revoke(ExternalConnectionNaming.CapabilityKey(connection.Id));
        Assert.False(await authority.AuthorizeAsync(connection, "read_verse", arguments, CancellationToken.None));
    }

    [Fact]
    public async Task ComputerUseAuthorityRefusesWithoutAGrantAndAcceptsTheExactCanonicalTarget()
    {
        var engine = new PermissionDecisionEngine();
        var authority = new HomeComputerUseAdmission(engine);
        var request = EligibleRequest();

        Assert.False(await authority.AuthorizeAsync(request, "computer_click", JsonSerializer.SerializeToElement(new { }), CancellationToken.None));

        engine.Grant("computeruse:" + request.TargetAppId);
        Assert.True(await authority.AuthorizeAsync(request, "computer_snapshot", JsonSerializer.SerializeToElement(new { }), CancellationToken.None));
    }

    [Fact]
    public async Task ComputerUseAuthorityCannotBeGrantedForARequestWithoutAnExplicitEligibleTarget()
    {
        var engine = new PermissionDecisionEngine();
        var authority = new HomeComputerUseAdmission(engine);
        var request = new ComputerUseRequest("request-1", "notepad", []);

        engine.Grant("computeruse:notepad");
        Assert.False(await authority.AuthorizeAsync(request, "computer_snapshot", JsonSerializer.SerializeToElement(new { }), CancellationToken.None));
    }

    private static ComputerUseRequest EligibleRequest()
    {
        var tokens = new List<InvocationToken>
        {
            new("token-system", new InvocationResource(InvocationKind.System, InvocationCompose.ComputerUseCapabilityId, "Computer Use"),
                0, 12, "computer use", Guid.NewGuid().ToString("N")),
            new("token-app", new InvocationResource(InvocationKind.App, "notepad", "Notepad", Revision: "r1",
                InteractionPath: AppInteractionPath.ComputerUseRequired, Classification: AppClassification.OrdinaryApplication), 13, 20, "notepad")
        };
        return new ComputerUseRequest("request-1", "notepad", tokens);
    }

    private static ExternalConnection ReadyConnection()
    {
        var now = DateTimeOffset.UtcNow;
        return new ExternalConnection(Guid.NewGuid(), "Remote MCP", "mcp.test", ExternalConnectionKind.Mcp, "custom-mcp", true,
            ExternalConnectionState.Ready, "Connected", "{}", null, null, null, now, now);
    }
}
